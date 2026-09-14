using System;

namespace DragNWash.ModFramework
{
    /// <summary>Which section of the game's Options screen a row joins.</summary>
    public enum OptionsSection
    {
        /// <summary>Gameplay settings.</summary>
        Gameplay,
        /// <summary>Audio settings.</summary>
        Audio,
        /// <summary>Graphics settings.</summary>
        Graphics,
    }

    /// <summary>
    /// A dropdown row in the game's own Options screen. It follows the game's
    /// flow: picking a choice previews it and brings up the game's Save button,
    /// Save keeps it, and Back without saving returns to the saved choice.
    /// </summary>
    public sealed class OptionsChoice
    {
        /// <summary>Unique id, for example "com.example.mymod.difficulty". Required.</summary>
        public string Id { get; set; }

        /// <summary>Row label, in English; translation mods translate it like any UI text.</summary>
        public string Label { get; set; }

        /// <summary>The choices, in English. Required, at least two.</summary>
        public string[] Choices { get; set; }

        /// <summary>Section the row joins. Defaults to Gameplay.</summary>
        public OptionsSection Section { get; set; } = OptionsSection.Gameplay;

        /// <summary>Index chosen by the game's "Set Default" button. -1 leaves the row alone.</summary>
        public int DefaultIndex { get; set; } = -1;

        /// <summary>Returns the index that is currently saved. Required.</summary>
        public Func<int> GetSaved { get; set; }

        /// <summary>Called with the saved index when the player presses Save. Required.</summary>
        public Action<int> Save { get; set; }

        /// <summary>
        /// Optional: called whenever the shown choice changes, including going back
        /// to the saved one, so a mod can apply it at once as a preview.
        /// </summary>
        public Action<int> Preview { get; set; }
    }

    /// <summary>Adds rows to the game's own Options screen.</summary>
    public static class GameOptions
    {
        /// <summary>
        /// Adds a dropdown row. Can be called at any time; the row appears once the
        /// game's settings exist. Adding the same id again is ignored.
        /// </summary>
        public static void AddChoice(OptionsChoice choice)
        {
            if (choice == null || string.IsNullOrEmpty(choice.Id) || string.IsNullOrEmpty(choice.Label) ||
                choice.Choices == null || choice.Choices.Length < 2 || choice.GetSaved == null || choice.Save == null)
            {
                throw new ArgumentException("OptionsChoice needs Id, Label, at least two Choices, GetSaved and Save.", nameof(choice));
            }
            Options.OptionsRows.Add(choice);
        }

        /// <summary>
        /// Adds an Off/On row. <paramref name="save"/> receives the saved value and
        /// <paramref name="preview"/>, if given, the value being shown.
        /// </summary>
        public static void AddToggle(string id, string label, Func<bool> getSaved, Action<bool> save,
            Action<bool> preview = null, OptionsSection section = OptionsSection.Gameplay, bool? defaultValue = null)
        {
            AddChoice(new OptionsChoice
            {
                Id = id,
                Label = label,
                Choices = new[] { "Off", "On" },
                Section = section,
                DefaultIndex = defaultValue.HasValue ? (defaultValue.Value ? 1 : 0) : -1,
                GetSaved = () => getSaved() ? 1 : 0,
                Save = i => save(i == 1),
                Preview = preview == null ? (Action<int>)null : i => preview(i == 1),
            });
        }

        /// <summary>
        /// Shows the saved value again after a mod changed it elsewhere (its own
        /// menu, a config file edit).
        /// </summary>
        public static void Refresh(string id)
        {
            Options.OptionsRows.Refresh(id);
        }
    }
}
