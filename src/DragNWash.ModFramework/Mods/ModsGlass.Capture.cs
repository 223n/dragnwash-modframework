using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace DragNWash.ModFramework.Mods
{
    // The blurred copy of the game's picture behind the Mods screen's panels.
    //
    // How: while the screen is open, a render pass is added to the game
    // camera through URP's public way for scripts (beginCameraRendering, then
    // ScriptableRenderer.EnqueuePass; the renderer's asset and its features
    // are left alone). It runs after post-processing and before the UI, so
    // it sees the picture the player sees, tone-mapped and anti-aliased, with
    // no menu in it. All of the blur is URP's own blit (Blitter, with its
    // clamped bilinear sampler); there is no shader of the framework's own:
    //
    // 1. Shrink: the picture is halved again and again, down to 1/32 of the
    //    screen (1/64 from about 1300 lines up). A halving with bilinear
    //    sampling averages each 2x2 pixels, so the smallest picture is the
    //    average of each 32x32 (or 64x64) square.
    // 2. Soften: at that size, a copy half a pixel down and right, and back
    //    half a pixel up and left, one to four times. Each pair spreads every
    //    pixel over its neighbours as 1-2-1 across and down, and the two
    //    halves cancel out, so nothing moves. This is what turns the squares
    //    into soft colour fields.
    // 3. Grow: back up to 1/8, one doubling at a time, which smooths the
    //    joins; the backdrops show the 1/8 picture, bilinear again.
    //
    // Run on a line of pixels, that is close to a Gaussian blur of 42 px at
    // 1080 lines (the mock's is 40), 28 px at the Steam Deck's 800 and 55 px
    // at 1440: about the same share of the screen at any size, as the mock's
    // is. A 1/32 picture is 60x33 at 1920x1080, so the soften blits cost next
    // to nothing; the first halving, which reads the whole screen once, is
    // most of the work.
    //
    // Why this way: Unity 6.3's URP draws only through the render graph, and
    // this is the path it keeps working on every graphics API (Direct3D 11
    // and 12, Vulkan on the Steam Deck) the same way, since it is how URP
    // copies its own textures. A blit from one texture to another never
    // flips the picture on any of them (only one to the screen itself can),
    // so the copy is the right way up everywhere. Reading the screen
    // afterwards (ScreenCapture) would copy the Mods screen itself into the
    // glass, and URP's opaque texture misses what is drawn after opaques and
    // the post-processing.
    //
    // The textures are made when the screen opens, again when the window
    // changes size or the graphics driver lost them, and let go when it
    // closes. Nothing is allocated while the camera draws.
    internal static class ModsGlassCapture
    {
        // The blur's strength at 1080 lines, in pixels: the mock's CSS blur.
        private const float BlurAt1080 = 40f;

        // How much the shrink and the grow blur by themselves, as a share of
        // (the smallest picture's pixel)^2; each soften pair adds 0.5 more.
        // Measured by running the steps on a line of pixels.
        private const float ShrinkBlur = 0.244f;

        // The level the backdrops show: 1/8 (level 0 is 1/2).
        private const int ResultLevel = 2;

        private static GlassPass _pass;
        private static Action<ScriptableRenderContext, Camera> _onBeginCamera;

        // 1/2, 1/4, 1/8 ... down to the smallest, and a second texture of the
        // smallest size for the soften blits to go back and forth with.
        private static RenderTexture[] _levels;
        private static RTHandle[] _levelHandles;
        private static RenderTexture _spare;
        private static RTHandle _spareHandle;
        // The levels as this frame's render graph knows them.
        private static TextureHandle[] _imported;
        private static int _softenPairs;
        private static Vector2 _halfPixel;
        private static int _width, _height;

        // What the backdrops show: the 1/8 picture, blurred.
        internal static Texture Result => _levels != null ? _levels[ResultLevel] : null;

        // The graphics driver let go of a texture, as it can on a device
        // reset, a display change or waking from power saving.
        internal static bool Lost
        {
            get
            {
                if (_levels == null)
                {
                    return false;
                }
                for (int i = 0; i < _levels.Length; i++)
                {
                    if (_levels[i] == null || !_levels[i].IsCreated())
                    {
                        return true;
                    }
                }
                return _spare == null || !_spare.IsCreated();
            }
        }

        internal static void Start(int width, int height)
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
            {
                throw new NotSupportedException("the game is not drawn with URP");
            }
            if (_pass == null)
            {
                _pass = new GlassPass();
            }
            Make(width, height);
            if (_onBeginCamera == null)
            {
                _onBeginCamera = OnBeginCamera;
            }
            RenderPipelineManager.beginCameraRendering -= _onBeginCamera;
            RenderPipelineManager.beginCameraRendering += _onBeginCamera;
        }

        internal static void Stop()
        {
            if (_onBeginCamera != null)
            {
                RenderPipelineManager.beginCameraRendering -= _onBeginCamera;
            }
            Release();
        }

        // Makes the textures again for a new size, or at the same size when
        // `force` (they were lost).
        internal static void Resize(int width, int height, bool force)
        {
            if (!force && width == _width && height == _height && _levels != null)
            {
                return;
            }
            Make(width, height);
        }

        private static void Make(int width, int height)
        {
            Release();
            try
            {
                // Each step adds to the blur by the square of the smallest
                // picture's pixel, so the smallest size and the soften pairs
                // are the ones that come nearest the mock's blur at this height.
                float blur = BlurAt1080 * Mathf.Max(1, height) / 1080f;
                int steps = blur * blur > 32f * 32f * (ShrinkBlur + 2f) ? 6 : 5;
                float pixel = 1 << steps;
                _softenPairs = Mathf.Clamp(Mathf.RoundToInt(2f * (blur * blur / (pixel * pixel) - ShrinkBlur)), 0, 4);

                // 8 bits a channel, sRGB in a linear project: the picture after
                // tone mapping is 0 to 1, and a blurred copy needs no more.
                GraphicsFormat format = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
                _levels = new RenderTexture[steps];
                _levelHandles = new RTHandle[steps];
                _imported = new TextureHandle[steps];
                int w = width, h = height;
                for (int i = 0; i < steps; i++)
                {
                    w = Mathf.Max(1, w / 2);
                    h = Mathf.Max(1, h / 2);
                    _levels[i] = Texture("ModsGlass 1/" + (2 << i), w, h, format);
                    _levelHandles[i] = RTHandles.Alloc(_levels[i]);
                }
                _spare = Texture("ModsGlass soften", w, h, format);
                _spareHandle = RTHandles.Alloc(_spare);
                _halfPixel = new Vector2(0.5f / w, 0.5f / h);
                _width = width;
                _height = height;
            }
            catch
            {
                // None of it is used; what was made is let go at once.
                Release();
                throw;
            }
        }

        private static RenderTexture Texture(string name, int width, int height, GraphicsFormat format)
        {
            var descriptor = new RenderTextureDescriptor(Mathf.Max(1, width), Mathf.Max(1, height), format, 0)
            {
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
            };
            var texture = new RenderTexture(descriptor)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!texture.Create())
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidOperationException($"could not make a {descriptor.width}x{descriptor.height} {format} texture");
            }
            return texture;
        }

        private static void Release()
        {
            if (_levels != null)
            {
                for (int i = 0; i < _levels.Length; i++)
                {
                    Release(ref _levelHandles[i], ref _levels[i]);
                }
            }
            Release(ref _spareHandle, ref _spare);
            _levels = null;
            _levelHandles = null;
            _imported = null;
            _width = _height = 0;
        }

        private static void Release(ref RTHandle handle, ref RenderTexture texture)
        {
            // The handle only wraps the texture: RTHandles.Alloc of a
            // RenderTexture takes no ownership, and its Release leaves the
            // texture alone. The texture is ours to destroy.
            handle?.Release();
            handle = null;
            if (texture != null)
            {
                texture.Release();
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
        }

        // The pass goes to the game's main camera, or to an overlay camera
        // stacked on it, and only does its work in the camera that finishes
        // the picture (the last of a stack). With no main camera, to any
        // camera drawing to the screen.
        private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!ModsGlass.CaptureWanted || _levels == null)
            {
                return;
            }
            try
            {
                if (camera == null || camera.cameraType != CameraType.Game || camera.targetTexture != null)
                {
                    return;
                }
                if (!camera.TryGetComponent(out UniversalAdditionalCameraData data))
                {
                    return;
                }
                Camera main = Camera.main;
                if (main != null && camera != main && data.renderType == CameraRenderType.Base)
                {
                    // Another camera drawing a picture of its own to the
                    // screen (for effects or a UI): not the scene.
                    return;
                }
                ScriptableRenderer renderer = data.scriptableRenderer;
                renderer?.EnqueuePass(_pass);
            }
            catch (Exception ex)
            {
                ModsGlass.Fail("could not add the copy to the camera", ex);
            }
        }

        private sealed class GlassPass : ScriptableRenderPass
        {
            internal GlassPass()
            {
                // After post-processing, so the copy is tone-mapped like the
                // screen. Asking for an intermediate texture makes URP draw
                // the picture there rather than straight to the screen, which
                // can't be read.
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                try
                {
                    UniversalCameraData camera = frameData.Get<UniversalCameraData>();
                    if (!camera.resolveFinalTarget || _levels == null)
                    {
                        return;
                    }
                    if (camera.isHDROutputActive)
                    {
                        ModsGlass.Fail("HDR output is on, and its picture isn't one the glass can use", null);
                        return;
                    }
                    UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                    if (resources.isActiveTargetBackBuffer)
                    {
                        ModsGlass.Fail("URP drew the picture straight to the screen, where it can't be read", null);
                        return;
                    }
                    TextureHandle source = resources.activeColorTexture;
                    if (!source.IsValid())
                    {
                        return;
                    }
                    // Each texture is imported once, so the graph knows that
                    // a later blit reads what an earlier one wrote.
                    for (int i = 0; i < _levels.Length; i++)
                    {
                        _imported[i] = renderGraph.ImportTexture(_levelHandles[i]);
                    }
                    TextureHandle spare = renderGraph.ImportTexture(_spareHandle);

                    TextureHandle from = source;
                    for (int i = 0; i < _imported.Length; i++)
                    {
                        renderGraph.AddBlitPass(from, _imported[i], Vector2.one, Vector2.zero, passName: "Mods glass: shrink");
                        from = _imported[i];
                    }
                    for (int i = 0; i < _softenPairs; i++)
                    {
                        renderGraph.AddBlitPass(from, spare, Vector2.one, _halfPixel, passName: "Mods glass: soften");
                        renderGraph.AddBlitPass(spare, from, Vector2.one, -_halfPixel, passName: "Mods glass: soften");
                    }
                    for (int i = _imported.Length - 2; i >= ResultLevel; i--)
                    {
                        renderGraph.AddBlitPass(_imported[i + 1], _imported[i], Vector2.one, Vector2.zero, passName: "Mods glass: grow");
                    }
                    ModsGlass.Captured();
                }
                catch (Exception ex)
                {
                    ModsGlass.Fail("could not copy the game's picture", ex);
                }
            }
        }
    }
}
