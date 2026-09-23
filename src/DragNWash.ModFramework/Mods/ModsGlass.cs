using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // Frosted glass behind the Mods screen's two panels: while the screen is
    // open, a small blurred copy of the game's camera picture sits under each
    // panel's tint, placed so it lines up with the scene behind it.
    //
    // [Mods screen] Glass picks how often the copy is made: Snapshot (every
    // 0.2 s, the default), Every frame, or Off (the tint alone, nothing
    // copied). Anything that goes wrong making the copy logs one warning and
    // leaves the screen on the tint alone until the game restarts.
    //
    // This class touches no URP type: the copy itself is ModsGlassCapture,
    // reached only through the NoInlining methods at the bottom, so a game
    // update that changes URP fails inside a try here instead of taking the
    // Mods screen with it.
    internal sealed class ModsGlass : MonoBehaviour
    {
        internal const string Section = "Mods screen";
        internal const string Key = "Glass";
        internal const string ModeSnapshot = "Snapshot";
        internal const string ModeEveryFrame = "Every frame";
        internal const string ModeOff = "Off";

        // How often Snapshot takes the picture again.
        private const float SnapshotSeconds = 0.2f;

        // The blurred picture is dimmed as the mock's brightness(0.55). A UI
        // colour is gamma, so this dims the same as the browser does.
        private static readonly Color BackdropColor = new Color(0.55f, 0.55f, 0.55f, 1f);

        private static ConfigEntry<string> _mode;
        private static bool _modeChanged;

        // Set once something failed; the glass stays off until the game restarts.
        private static bool _failed;

        // Read by the capture while the camera draws.
        internal static bool CaptureWanted;
        private static bool _everyFrame;
        private static bool _captured;

        // Set up by ModsScreen.SplitForDetails.
        internal Image ListTint;
        internal Image DetailsTint;
        internal Image ListEdge;
        internal Image DetailsEdge;
        internal RawImage ListBackdrop;
        internal RawImage DetailsBackdrop;

        private bool _capturing;
        private float _next;
        private int _width;
        private int _height;
        private readonly Vector3[] _corners = new Vector3[4];

        internal static void Install(ConfigFile config)
        {
            try
            {
                _mode = config.Bind(Section, Key, ModeSnapshot,
                    new ConfigDescription("What the list and the details sit on. Snapshot: a blurred picture of the game behind them, taken again every 0.2 seconds. Every frame: the same picture, taken every frame, so it moves with the game; it costs a little more. Off: a see-through dark tint, with no picture taken. When the picture can't be made on this computer, the screen uses Off by itself until the game restarts.",
                        new AcceptableValueList<string>(ModeSnapshot, ModeEveryFrame, ModeOff),
                        new SettingMeta { DisplayName = "Frosted glass" },
                        new SectionMeta { DisplayName = "Mods screen" }));
                _mode.SettingChanged += (sender, args) => _modeChanged = true;
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not add the Mods screen's glass setting: {ex.Message}");
            }
        }

        private static string Mode => _mode != null ? _mode.Value : ModeOff;

        // From the capture, while the camera draws: a picture was taken.
        internal static void Captured()
        {
            _captured = true;
            if (!_everyFrame)
            {
                CaptureWanted = false;
            }
        }

        // From anywhere: the glass can't be made. One warning, then the tint
        // alone for the rest of the session. What was made is let go on the
        // next Update, outside the camera's drawing.
        internal static void Fail(string why, Exception ex)
        {
            CaptureWanted = false;
            if (_failed)
            {
                return;
            }
            _failed = true;
            ModFramework.Log.LogWarning($"The Mods screen's frosted glass is off until the game restarts: {why}{(ex != null ? $" ({ex.GetType().Name}: {ex.Message})" : "")}. The panels keep their tint.");
        }

        private void OnEnable()
        {
            _modeChanged = false;
            Apply();
        }

        private void OnDisable()
        {
            StopCapture();
        }

        private void Update()
        {
            if (_modeChanged || _failed && _capturing)
            {
                _modeChanged = false;
                Apply();
            }
            if (!_capturing)
            {
                return;
            }
            if (Screen.width != _width || Screen.height != _height)
            {
                Resize();
                if (!_capturing)
                {
                    return;
                }
            }
            if (_everyFrame)
            {
                CaptureWanted = true;
            }
            else if (Time.unscaledTime >= _next)
            {
                CaptureWanted = true;
                _next = Time.unscaledTime + SnapshotSeconds;
            }
        }

        private void LateUpdate()
        {
            if (!_capturing || !_captured)
            {
                return;
            }
            Place(ListBackdrop);
            Place(DetailsBackdrop);
        }

        // Starts or stops the copy for the setting, and colours the panels:
        // glass while a copy can be made, the tint alone otherwise.
        private void Apply()
        {
            string mode = Mode;
            bool want = mode != ModeOff && !_failed;
            _everyFrame = mode == ModeEveryFrame;
            if (want && !_capturing)
            {
                StartCapture();
            }
            else if (!want && _capturing)
            {
                StopCapture();
            }
            bool glass = _capturing;
            if (ModsLook.Glass != glass)
            {
                // The open screen builds itself again with these (ModsMenu.RunPending).
                ModsLook.UseLook(glass);
            }
            Paint(ListTint, ModsLook.Panel);
            Paint(DetailsTint, ModsLook.Panel);
            Paint(ListEdge, ModsLook.Hairline);
            Paint(DetailsEdge, ModsLook.Hairline);
        }

        private static void Paint(Image image, Color color)
        {
            if (image != null)
            {
                image.color = color;
            }
        }

        private void StartCapture()
        {
            _captured = false;
            HideBackdrops(null);
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Fail("there is no graphics device", null);
                return;
            }
            try
            {
                _width = Screen.width;
                _height = Screen.height;
                StartCore(_width, _height);
                _capturing = true;
                CaptureWanted = true;
                _next = Time.unscaledTime + SnapshotSeconds;
                HideBackdrops(ResultCore());
            }
            catch (Exception ex)
            {
                Fail("could not start copying the game's picture", ex);
                StopCapture();
            }
        }

        private void StopCapture()
        {
            CaptureWanted = false;
            _captured = false;
            HideBackdrops(null);
            if (!_capturing)
            {
                return;
            }
            _capturing = false;
            try
            {
                StopCore();
            }
            catch (Exception ex)
            {
                Fail("could not stop copying the game's picture", ex);
            }
        }

        // The window changed size: the textures are made again at the new size.
        private void Resize()
        {
            try
            {
                _width = Screen.width;
                _height = Screen.height;
                _captured = false;
                HideBackdrops(null);
                ResizeCore(_width, _height);
                CaptureWanted = true;
                HideBackdrops(ResultCore());
            }
            catch (Exception ex)
            {
                Fail("could not make the picture's textures again after the window changed size", ex);
                StopCapture();
                Apply();
            }
        }

        // The backdrops wait, hidden, for the first picture.
        private void HideBackdrops(Texture texture)
        {
            Hide(ListBackdrop, texture);
            Hide(DetailsBackdrop, texture);
        }

        private static void Hide(RawImage backdrop, Texture texture)
        {
            if (backdrop != null)
            {
                backdrop.texture = texture;
                backdrop.color = BackdropColor;
                backdrop.enabled = false;
            }
        }

        // Shows the part of the picture that is behind the backdrop on screen.
        private void Place(RawImage backdrop)
        {
            if (backdrop == null || backdrop.texture == null)
            {
                return;
            }
            if (!backdrop.enabled)
            {
                backdrop.enabled = true;
            }
            Canvas canvas = backdrop.canvas != null ? backdrop.canvas.rootCanvas : null;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            backdrop.rectTransform.GetWorldCorners(_corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, _corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, _corners[2]);
            float width = Mathf.Max(1f, Screen.width);
            float height = Mathf.Max(1f, Screen.height);
            var uv = new Rect(min.x / width, min.y / height, (max.x - min.x) / width, (max.y - min.y) / height);
            if (backdrop.uvRect != uv)
            {
                backdrop.uvRect = uv;
            }
        }

        // ---- the only ways into ModsGlassCapture ----

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void StartCore(int width, int height)
        {
            ModsGlassCapture.Start(width, height);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void StopCore()
        {
            ModsGlassCapture.Stop();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ResizeCore(int width, int height)
        {
            ModsGlassCapture.Resize(width, height);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Texture ResultCore()
        {
            return ModsGlassCapture.Result;
        }
    }
}
