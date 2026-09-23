using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DragNWash.ModFramework.Mods
{
    // Gamepad and Steam Deck help for the Mods screen.
    //
    // A rounded frame in the accent colour around whatever is selected, so a
    // player using the pad or the Deck can see where they are: the hover tint
    // alone is too faint on the dark panel.
    //
    // Presses of A, R2 and the stick buttons (Steam Input reports a trackpad click
    // as a stick press) click the selected button when the game's UI input did
    // not already click it that frame. On the Steam Deck these presses did not
    // reach the Mods screen's buttons.
    //
    // LB and RB go to the tab on the left or right, Y to the search field, and
    // whatever gets selected is scrolled into view in the list or the tab.
    //
    // The keyboard too: the arrow keys move between the list and the details
    // (and from Back into the list), and Enter presses the selected button.
    internal sealed class PadSupport : MonoBehaviour
    {
        internal ModsMenu Menu;

        private const float Thickness = 2f;
        private const float Gap = 4f;
        private static readonly Color FrameColor = Color.white;

        private GameObject _frame;
        private GameObject _framed;
        private int _clickedFrame = -1;
        private Button _listening;
        private GameObject _selected;
        private GameObject _lastSelected;
        private int _selectedFrame = -1;
        private bool _reported;

        private void OnDisable()
        {
            HideFrame();
            _selected = null;
        }

        private void LateUpdate()
        {
            try
            {
                GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                // Whether the game's UI input already moved the selection this frame.
                bool movedByGame = !ReferenceEquals(selected, _lastSelected);
                _lastSelected = selected;
                if (Shortcuts(selected))
                {
                    return;
                }
                if (!movedByGame && Arrows(selected))
                {
                    _lastSelected = EventSystem.current.currentSelectedGameObject;
                    return;
                }
                if (selected == null || !selected.activeInHierarchy || !selected.transform.IsChildOf(Menu.transform))
                {
                    HideFrame();
                    _selected = null;
                    return;
                }
                // Only the list and the details: the game's own buttons on the left
                // (Back) already show the game's pointing hand.
                Transform split = Menu.Details != null ? Menu.Details.parent : null;
                // Not a scrollbar a click left selected: the pad never goes there.
                if (split != null && selected.transform.IsChildOf(split) && selected.GetComponent<Scrollbar>() == null)
                {
                    ShowFrame(selected);
                }
                else
                {
                    HideFrame();
                }
                if (!ReferenceEquals(selected, _selected))
                {
                    _selected = selected;
                    _selectedFrame = Time.frameCount;
                }
                // For a few frames: a panel built this frame is laid out later.
                if (Time.frameCount - _selectedFrame <= 2)
                {
                    KeepInView(selected);
                }

                // A text field starts typing on A, not on being passed over
                // (shouldActivateOnSelect is off). Where the game's UI input
                // does not pass A on (the Steam Deck), start it here; a field
                // the game already started is only asked again, which is harmless.
                TMP_InputField field = selected.GetComponent<TMP_InputField>();
                if (field != null)
                {
                    if (PadPressedThisFrame() && !field.isFocused && field.IsInteractable() && _selectedFrame != Time.frameCount)
                    {
                        field.ActivateInputField();
                    }
                    return;
                }

                Button button = selected.GetComponent<Button>();
                if (button != null && !ReferenceEquals(button, _listening))
                {
                    button.onClick.RemoveListener(NoteClick);
                    button.onClick.AddListener(NoteClick);
                    _listening = button;
                }
                if (!SubmitPressedThisFrame() || button == null || !button.IsInteractable() ||
                    selected.GetComponent<ShortcutCapture>() != null)
                {
                    return;
                }
                // The game's UI input clicks on the same frame; give it that frame.
                // A button that only became selected this frame was not the one the
                // press was meant for: pressing Mods on the Options screen opens this
                // screen and selects its Back button in that very frame.
                if (_clickedFrame == Time.frameCount || _selectedFrame == Time.frameCount)
                {
                    return;
                }
                if (!_reported)
                {
                    _reported = true;
                    ModFramework.Log.LogInfo($"Mods screen: a pad or Enter press on \"{selected.name}\" was not handled by the game's UI input; clicking it.");
                }
                button.onClick.Invoke();
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Mods screen gamepad help failed: {ex.Message}");
                enabled = false;
            }
        }

        // The shoulder buttons and Y. Not while typing in a field.
        private bool Shortcuts(GameObject selected)
        {
            Gamepad pad = Gamepad.current;
            // Only while this screen is the one showing.
            if (pad == null || Menu == null || Menu.Details == null || !Menu.Details.gameObject.activeInHierarchy ||
                selected != null && !selected.transform.IsChildOf(Menu.transform))
            {
                return false;
            }
            TMP_InputField field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
            if (field != null && field.isFocused)
            {
                return false;
            }
            if (pad.leftShoulder.wasPressedThisFrame)
            {
                Menu.StepTab(-1);
                return true;
            }
            if (pad.rightShoulder.wasPressedThisFrame)
            {
                Menu.StepTab(1);
                return true;
            }
            if (pad.buttonNorth.wasPressedThisFrame)
            {
                Menu.FocusSearch();
                return true;
            }
            return false;
        }

        // The arrow keys, and the d-pad's right on the game's buttons. The game's
        // menu input moves only between its own buttons (Back and the others on
        // the left), so from there nothing led into the list, and on some setups
        // the arrow keys did not move between the list's rows either. Only when
        // the game's input left the selection where it was this frame, and never
        // while a field is typing or a key is being taken.
        private bool Arrows(GameObject selected)
        {
            if (selected == null || Menu == null || Menu.Details == null || !Menu.Details.gameObject.activeInHierarchy ||
                !selected.activeInHierarchy || !selected.transform.IsChildOf(Menu.transform) ||
                selected.GetComponent<ShortcutCapture>() != null)
            {
                return false;
            }
            TMP_InputField field = selected.GetComponent<TMP_InputField>();
            if (field != null && field.isFocused)
            {
                return false;
            }
            Keyboard keys = Keyboard.current;
            Gamepad pad = Gamepad.current;
            bool up = keys != null && keys.upArrowKey.wasPressedThisFrame;
            bool down = keys != null && keys.downArrowKey.wasPressedThisFrame;
            bool left = keys != null && keys.leftArrowKey.wasPressedThisFrame;
            bool right = keys != null && keys.rightArrowKey.wasPressedThisFrame;
            if (!up && !down && !left && !right && !(pad != null && pad.dpad.right.wasPressedThisFrame))
            {
                return false;
            }

            Transform split = Menu.Details.parent;
            if (split == null || !selected.transform.IsChildOf(split))
            {
                // On the game's buttons: right goes into the list.
                return (right || pad != null && pad.dpad.right.wasPressedThisFrame) && Menu.FocusList();
            }
            Selectable current = selected.GetComponent<Selectable>();
            if (current == null)
            {
                return false;
            }
            Selectable next = up ? current.FindSelectableOnUp()
                : down ? current.FindSelectableOnDown()
                : left ? current.FindSelectableOnLeft()
                : right ? current.FindSelectableOnRight()
                : null;
            if (next == null || !next.gameObject.activeInHierarchy || !next.transform.IsChildOf(Menu.transform))
            {
                return false;
            }
            EventSystem.current.SetSelectedGameObject(next.gameObject);
            return true;
        }

        // Scrolls the list or the tab so the selected thing shows, with a
        // little room around it; one taller than the view shows its top.
        private static void KeepInView(GameObject selected)
        {
            ScrollRect scroll = selected.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null || !scroll.vertical || !selected.transform.IsChildOf(scroll.content))
            {
                return;
            }
            RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            if (scroll.content.parent != viewport)
            {
                return;
            }
            var target = (RectTransform)selected.transform;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            float bottom = viewport.InverseTransformPoint(corners[0]).y;
            float top = viewport.InverseTransformPoint(corners[1]).y;
            Rect view = viewport.rect;
            const float margin = 12f;
            float shift = 0f;
            if (bottom < view.yMin + margin)
            {
                shift = view.yMin + margin - bottom;
            }
            if (top + shift > view.yMax - margin)
            {
                shift = view.yMax - margin - top;
            }
            if (Mathf.Abs(shift) < 0.5f)
            {
                return;
            }
            scroll.velocity = Vector2.zero;
            scroll.content.anchoredPosition += new Vector2(0f, shift);
        }

        // Added to the selected button's onClick, so a click by the game's UI input is seen.
        private void NoteClick()
        {
            _clickedFrame = Time.frameCount;
        }

        // A, or Enter on the keyboard: pressing the selected button. Not for
        // text fields, where Enter is what ends the typing.
        internal static bool SubmitPressedThisFrame()
        {
            Keyboard keys = Keyboard.current;
            return PadPressedThisFrame() ||
                   keys != null && (keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame);
        }

        internal static bool PadPressedThisFrame()
        {
            Gamepad pad = Gamepad.current;
            return pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame ||
                                   pad.rightStickButton.wasPressedThisFrame || pad.leftStickButton.wasPressedThisFrame);
        }

        private void ShowFrame(GameObject target)
        {
            if (_frame != null && _framed == target)
            {
                // On top of anything added to the target since (a Saved tag),
                // without touching the hierarchy every frame.
                Transform frame = _frame.transform;
                if (frame.GetSiblingIndex() != target.transform.childCount - 1)
                {
                    frame.SetAsLastSibling();
                }
                return;
            }
            if (_frame == null)
            {
                _frame = BuildFrame();
            }
            var rect = (RectTransform)_frame.transform;
            rect.SetParent(target.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            float margin = ModsLook.Outline != null ? Gap : Thickness;
            rect.offsetMin = new Vector2(-margin, -margin);
            rect.offsetMax = new Vector2(margin, margin);
            rect.SetAsLastSibling();
            _frame.SetActive(true);
            _framed = target;
        }

        private void HideFrame()
        {
            if (_frame != null)
            {
                _frame.SetActive(false);
            }
            _framed = null;
        }

        private static GameObject BuildFrame()
        {
            var frame = new GameObject("SelectionFrame", typeof(RectTransform));
            if (ModsLook.Outline != null)
            {
                Image outline = ModsLook.Shape(frame, ModsLook.Outline, ModsLook.Accent, 14f);
                outline.raycastTarget = false;
                return frame;
            }
            // Without the shapes: a thin white line on each edge.
            // Left, right, bottom, top edges.
            Edge(frame, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(Thickness, 0f));
            Edge(frame, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-Thickness, 0f), new Vector2(0f, 0f));
            Edge(frame, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, Thickness));
            Edge(frame, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -Thickness), new Vector2(0f, 0f));
            return frame;
        }

        private static void Edge(GameObject frame, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var edge = new GameObject("Edge", typeof(RectTransform));
            var rect = (RectTransform)edge.transform;
            rect.SetParent(frame.transform, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            Image image = edge.AddComponent<Image>();
            image.color = FrameColor;
            image.raycastTarget = false;
        }
    }
}
