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
    // no menu in it. It shrinks that picture to 1/2, 1/4 and 1/8 and back up
    // to 1/4 with URP's own blit (Blitter, with its bilinear sampler), which
    // is all the blur there is: no shader of the framework's own.
    //
    // Why this way: Unity 6.3's URP draws only through the render graph, and
    // this is the path it keeps working on every graphics API (Direct3D 11
    // and 12, Vulkan on the Steam Deck) the same way, since it is how URP
    // copies its own textures. Reading the screen afterwards
    // (ScreenCapture) would copy the Mods screen itself into the glass, and
    // URP's opaque texture misses what is drawn after opaques and the
    // post-processing.
    //
    // The textures are made once when the screen opens, again when the window
    // changes size, and let go when it closes.
    internal static class ModsGlassCapture
    {
        private static GlassPass _pass;
        private static Action<ScriptableRenderContext, Camera> _onBeginCamera;
        private static RenderTexture _half, _quarter, _eighth;
        private static RTHandle _halfHandle, _quarterHandle, _eighthHandle;
        private static int _width, _height;

        // What the backdrops show: the 1/4 picture, blurred.
        internal static Texture Result => _quarter;

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

        internal static void Resize(int width, int height)
        {
            if (width == _width && height == _height && _quarter != null)
            {
                return;
            }
            Release();
            Make(width, height);
        }

        private static void Make(int width, int height)
        {
            Release();
            _width = width;
            _height = height;
            // 8 bits a channel, sRGB in a linear project: the picture after
            // tone mapping is 0 to 1, and a quarter of the screen needs no more.
            GraphicsFormat format = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            _half = Texture("ModsGlassHalf", width / 2, height / 2, format);
            _quarter = Texture("ModsGlassQuarter", width / 4, height / 4, format);
            _eighth = Texture("ModsGlassEighth", width / 8, height / 8, format);
            _halfHandle = RTHandles.Alloc(_half);
            _quarterHandle = RTHandles.Alloc(_quarter);
            _eighthHandle = RTHandles.Alloc(_eighth);
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
            Release(ref _halfHandle, ref _half);
            Release(ref _quarterHandle, ref _quarter);
            Release(ref _eighthHandle, ref _eighth);
            _width = _height = 0;
        }

        private static void Release(ref RTHandle handle, ref RenderTexture texture)
        {
            // The handle only wraps the texture; the texture is ours to destroy.
            handle?.Release();
            handle = null;
            if (texture != null)
            {
                texture.Release();
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
        }

        // Every camera that draws to the screen gets the pass when a picture
        // is wanted; the pass itself only works in the camera that finishes
        // the picture (the last of a stack).
        private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!ModsGlass.CaptureWanted || _quarter == null)
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
                    if (!camera.resolveFinalTarget || _quarterHandle == null)
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
                    TextureHandle half = renderGraph.ImportTexture(_halfHandle);
                    TextureHandle quarter = renderGraph.ImportTexture(_quarterHandle);
                    TextureHandle eighth = renderGraph.ImportTexture(_eighthHandle);
                    // Each step halves the size with bilinear sampling, so
                    // each pixel is the average of four: 1/8 is an 8x8 box,
                    // and going back up to 1/4 smooths the boxes' edges.
                    renderGraph.AddBlitPass(source, half, Vector2.one, Vector2.zero, passName: "Mods glass 1/2");
                    renderGraph.AddBlitPass(half, quarter, Vector2.one, Vector2.zero, passName: "Mods glass 1/4");
                    renderGraph.AddBlitPass(quarter, eighth, Vector2.one, Vector2.zero, passName: "Mods glass 1/8");
                    renderGraph.AddBlitPass(eighth, quarter, Vector2.one, Vector2.zero, passName: "Mods glass back to 1/4");
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
