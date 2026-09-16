using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityScriptableSettings;

namespace DragNWash.ModFramework.Options
{
    // Rows in the game's Options screen, the way the localization mod added its
    // language picker: UnityScriptableSettings' spawner builds one row per
    // Setting in SettingsManager's list whenever Options opens, so a Setting is
    // added to that list and the game builds the row itself, with its own
    // prefab, styling and pad navigation. Nothing in the game's UI is cloned.
    internal static class OptionsRows
    {
        private static readonly List<OptionsChoice> Choices = new List<OptionsChoice>();
        private static readonly Dictionary<string, ChoiceSetting> Settings = new Dictionary<string, ChoiceSetting>(StringComparer.Ordinal);
        private static readonly FieldInfo InstanceField = AccessTools.Field(typeof(SettingsManager), "instance");
        private static readonly FieldInfo ListField = AccessTools.Field(typeof(SettingsManager), "settings");
        private static readonly FieldInfo LabelField = AccessTools.Field(typeof(Setting), "label");
        private static readonly FieldInfo DropdownsField = AccessTools.Field(typeof(ScriptableSettingSpawner), "dropdowns");
        private static bool _broken;

        internal static IEnumerable<OptionsChoice> All => Choices;

        internal static void Install(Harmony harmony)
        {
            try
            {
                MethodInfo create = AccessTools.Method(typeof(ScriptableSettingSpawner), "CreateDropDown", new[] { typeof(SettingInt) });
                if (create != null)
                {
                    harmony.Patch(create, postfix: new HarmonyMethod(typeof(OptionsRows), nameof(AfterCreateDropDown)));
                }
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not hook the Options dropdowns: {ex.Message}");
            }
        }

        internal static void Add(OptionsChoice choice)
        {
            OptionsChoice existing = Choices.FirstOrDefault(c => c.Id == choice.Id);
            if (existing != null)
            {
                // The same row again: a reloaded mod (ModReload) adding its row
                // back. The row keeps its place; the callbacks are the new build's.
                existing.GetSaved = choice.GetSaved;
                existing.Save = choice.Save;
                existing.Preview = choice.Preview;
                existing.DefaultIndex = choice.DefaultIndex;
                return;
            }
            Choices.Add(choice);
        }

        internal static void Refresh(string id)
        {
            if (Settings.TryGetValue(id, out ChoiceSetting setting) && setting != null)
            {
                setting.ShowSaved(notify: true);
            }
        }

        // From Plugin.Update: the game's settings live in the first scene.
        internal static void Tick()
        {
            if (_broken || Choices.Count == Settings.Count || Time.frameCount % 30 != 0)
            {
                return;
            }
            try
            {
                if (InstanceField == null || ListField == null || LabelField == null)
                {
                    _broken = true;
                    ModFramework.Log.LogWarning("The game's settings library has changed shape; rows cannot be added to the Options screen.");
                    return;
                }
                object instance = InstanceField.GetValue(null);
                var list = instance != null ? ListField.GetValue(instance) as List<Setting> : null;
                if (list == null || list.Count == 0)
                {
                    return;
                }

                foreach (OptionsChoice choice in Choices)
                {
                    if (Settings.ContainsKey(choice.Id))
                    {
                        continue;
                    }
                    SettingGroup group = FindGroup(list, choice.Section);
                    ChoiceSetting setting = ScriptableObject.CreateInstance<ChoiceSetting>();
                    setting.name = choice.Id;
                    // Only the settings list references it; the game unloads
                    // unused assets on scene changes.
                    setting.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    LabelField.SetValue(setting, new ScriptableSettingString(choice.Label));
                    setting.group = group;
                    setting.Configure(choice);

                    // Not SettingsManager.AddSetting: it re-sorts the list with an
                    // unstable sort and can shuffle the game's rows.
                    int insertAt = list.Count;
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        if (list[i] != null && list[i].group == group)
                        {
                            insertAt = i + 1;
                            break;
                        }
                    }
                    list.Insert(insertAt, setting);
                    Settings[choice.Id] = setting;
                    ModFramework.Log.LogInfo($"Added \"{choice.Label}\" to the Options screen.");
                }
            }
            catch (Exception ex)
            {
                _broken = true;
                ModFramework.Log.LogWarning($"Could not add rows to the Options screen: {ex}");
            }
        }

        private static SettingGroup FindGroup(List<Setting> list, OptionsSection section)
        {
            string wanted = section.ToString();
            SettingGroup last = null;
            foreach (Setting s in list)
            {
                if (s == null || s.group == null)
                {
                    continue;
                }
                last = s.group;
                string label = null;
                try
                {
                    label = s.group.GetLabel().backupString;
                }
                catch
                {
                }
                if ((label ?? "").IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.group.name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return s.group;
                }
            }
            return last;
        }

        // The game's dropdown prefab is sized for two or three choices.
        private static void AfterCreateDropDown(ScriptableSettingSpawner __instance, SettingInt option)
        {
            if (!(option is ChoiceSetting ours) || DropdownsField == null)
            {
                return;
            }
            try
            {
                var map = DropdownsField.GetValue(__instance) as Dictionary<Setting, TMP_Dropdown>;
                if (map == null || !map.TryGetValue(option, out TMP_Dropdown dropdown) || dropdown == null || dropdown.template == null)
                {
                    return;
                }
                float itemHeight = 20f;
                if (dropdown.itemText != null && dropdown.itemText.transform.parent is RectTransform item && item.rect.height >= 1f)
                {
                    itemHeight = item.rect.height;
                }
                float wanted = itemHeight * Mathf.Min(ours.Count, 10) + 8f;
                RectTransform template = dropdown.template;
                if (template.sizeDelta.y < wanted)
                {
                    template.sizeDelta = new Vector2(template.sizeDelta.x, wanted);
                }
                ScrollRect scroll = template.GetComponent<ScrollRect>();
                if (scroll != null && scroll.scrollSensitivity < itemHeight)
                {
                    scroll.scrollSensitivity = itemHeight;
                }
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not size an Options dropdown: {ex.Message}");
            }
        }
    }

    // The Setting the game's spawner turns into a dropdown row.
    internal sealed class ChoiceSetting : SettingDropdown
    {
        private OptionsChoice _choice;

        internal int Count => _choice?.Choices.Length ?? 0;

        internal void Configure(OptionsChoice choice)
        {
            _choice = choice;
            dropdownOptions = choice.Choices.Select(c => new ScriptableSettingString(c)).ToArray();
            ShowSaved(notify: false);
        }

        internal void ShowSaved(bool notify)
        {
            int saved = Saved();
            if (saved == selectedValue)
            {
                return;
            }
            selectedValue = saved;
            if (notify)
            {
                NotifyChange();
            }
        }

        // The player picked a choice: show it and let the mod preview it.
        public override void SetValue(int value)
        {
            if (_choice == null)
            {
                return;
            }
            value = Mathf.Clamp(value, 0, Count - 1);
            if (value == selectedValue)
            {
                return;
            }
            selectedValue = value;
            Call(_choice.Preview, value);
            NotifyChange();
        }

        public override int GetValue()
        {
            return selectedValue;
        }

        // Save pressed.
        public override void Save()
        {
            if (_choice != null && selectedValue != Saved())
            {
                Call(_choice.Save, selectedValue);
            }
        }

        // Back without saving: return to the saved choice.
        public override void Load()
        {
            if (_choice == null)
            {
                return;
            }
            int saved = Saved();
            if (saved != selectedValue)
            {
                selectedValue = saved;
                Call(_choice.Preview, saved);
                NotifyChange();
            }
        }

        // "Set Default": like picking the default, still needs Save.
        public override void ResetToDefault()
        {
            if (_choice != null && _choice.DefaultIndex >= 0)
            {
                SetValue(_choice.DefaultIndex);
            }
        }

        private int Saved()
        {
            try
            {
                return Mathf.Clamp(_choice.GetSaved(), 0, Count - 1);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"{_choice.Id}: GetSaved threw: {ex.Message}");
                return 0;
            }
        }

        private void Call(Action<int> action, int value)
        {
            if (action == null)
            {
                return;
            }
            try
            {
                action(value);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"{_choice.Id}: a callback threw: {ex}");
            }
        }
    }
}
