using System;

namespace Fumo.NativePlayable
{
    public enum NativeMenuPage { None, Main, Tracks, Characters, Vehicles, Pause, Results, Custom, Ceremony, RaceModes, Extras, Credits, MusicRoom, Online, Options, Skins, Cups }
    public enum NativeMenuAction { None, OpenTracks, OpenCharacters, OpenVehicles, StartRace, BackToMain, BackToTracks, BackToCharacters, Quit, Resume, Restart, MainMenu, TimeTrials, GrandPrix, CustomRace, Settings, Continue, Profiles, Rebind, ApplySettings, CustomLaps, CustomRacers, CustomItems, OpenRaceModes, BackToRaceModes, Extras, Credits, MusicRoom, Online, BackToExtras, QuickRace, OpenSkins, OpenCups, ConfirmCup, ConfirmCircuit, BackToVehicles, BackToCup, CustomRespawn, CustomModifier }

    public sealed class NativeMenuNavigation
    {
        public NativeMenuPage Page { get; private set; }
        public int SelectedIndex { get; private set; }
        public int CharacterCount { get; set; } = 1;
        public int VehicleCount { get; set; } = 1;
        public int SkinCount { get; set; } = 1;
        public int CustomCount { get; set; } = 11;
        public NativeMenuAction[] MainActions { get; set; } = { NativeMenuAction.OpenTracks, NativeMenuAction.Quit };
        public bool[] MainDisabled { get; set; }
        public bool IsDisabled(int index) => Page == NativeMenuPage.Main && MainDisabled != null && index < MainDisabled.Length && MainDisabled[index];
        public NativeMenuAction[] ResultActions { get; set; } = { NativeMenuAction.Restart, NativeMenuAction.MainMenu, NativeMenuAction.Quit };
        int mainIndex, raceModeIndex;
        public int Count => Page == NativeMenuPage.Tracks ? NativeTrackCatalog.Count : Page == NativeMenuPage.Main ? MainActions.Length
            : Page == NativeMenuPage.Characters ? CharacterCount : Page == NativeMenuPage.Vehicles ? VehicleCount
            : Page == NativeMenuPage.Skins ? SkinCount : Page == NativeMenuPage.Cups ? 1
            : Page == NativeMenuPage.Extras ? 2 : Page == NativeMenuPage.Credits || Page == NativeMenuPage.Online ? 1 : Page == NativeMenuPage.MusicRoom ? 1 : Page == NativeMenuPage.RaceModes ? 3 : Page == NativeMenuPage.Pause ? 3 : Page == NativeMenuPage.Results ? ResultActions.Length : Page == NativeMenuPage.Custom ? CustomCount : Page == NativeMenuPage.Ceremony ? 1 : 0;

        public bool SetPage(NativeMenuPage page)
        {
            if (Page == page) return false;
            if (Page == NativeMenuPage.Main) mainIndex = SelectedIndex;
            if (Page == NativeMenuPage.RaceModes) raceModeIndex = SelectedIndex;
            Page = page;
            SelectedIndex = page == NativeMenuPage.Main ? mainIndex : page == NativeMenuPage.RaceModes ? raceModeIndex : 0;
            SelectedIndex = Math.Min(SelectedIndex, Math.Max(0, Count - 1));
            return true;
        }

        public bool Select(int index)
        {
            if (index < 0 || index >= Count || index == SelectedIndex || IsDisabled(index)) return false;
            SelectedIndex = index;
            return true;
        }

        public bool Move(int delta)
        {
            if (Count == 0) return false;
            int target = ((SelectedIndex + delta) % Count + Count) % Count;
            for (int i = 0; i < Count && IsDisabled(target); i++) target = ((target + Math.Sign(delta)) % Count + Count) % Count;
            return Select(target);
        }

        public NativeMenuAction Confirm()
        {
            if (IsDisabled(SelectedIndex)) return NativeMenuAction.None;
            if (Page == NativeMenuPage.Main)
                return MainActions[SelectedIndex];
            if (Page == NativeMenuPage.RaceModes) return new[] { NativeMenuAction.GrandPrix, NativeMenuAction.QuickRace, NativeMenuAction.CustomRace }[SelectedIndex];
            if (Page == NativeMenuPage.Extras) return SelectedIndex == 0 ? NativeMenuAction.Credits : NativeMenuAction.MusicRoom;
            if (Page == NativeMenuPage.Credits || Page == NativeMenuPage.MusicRoom) return NativeMenuAction.BackToExtras;
            if (Page == NativeMenuPage.Online) return NativeMenuAction.BackToMain;
            if (Page == NativeMenuPage.Tracks) return NativeMenuAction.ConfirmCircuit;
            if (Page == NativeMenuPage.Characters) return NativeMenuAction.OpenVehicles;
            if (Page == NativeMenuPage.Vehicles) return NativeMenuAction.OpenSkins;
            if (Page == NativeMenuPage.Skins) return NativeMenuAction.OpenCups;
            if (Page == NativeMenuPage.Cups) return NativeMenuAction.ConfirmCup;
            if (Page == NativeMenuPage.Pause)
            {
                if (SelectedIndex == 0) return NativeMenuAction.Resume;
                if (SelectedIndex == 1) return NativeMenuAction.Restart;
                return NativeMenuAction.MainMenu;
            }
            if (Page == NativeMenuPage.Results) return ResultActions[SelectedIndex];
            if (Page == NativeMenuPage.Ceremony) return NativeMenuAction.MainMenu;
            if (Page == NativeMenuPage.Custom)
            {
                return SelectedIndex == 0 ? NativeMenuAction.StartRace : SelectedIndex == 1 ? NativeMenuAction.CustomLaps : SelectedIndex == 2 ? NativeMenuAction.CustomRacers : SelectedIndex == 3 ? NativeMenuAction.CustomItems : SelectedIndex == 4 ? NativeMenuAction.CustomRespawn : NativeMenuAction.CustomModifier;
            }
            return NativeMenuAction.None;
        }

        public NativeMenuAction Cancel()
        {
            if (Page == NativeMenuPage.Credits || Page == NativeMenuPage.MusicRoom) return NativeMenuAction.BackToExtras;
            if (Page == NativeMenuPage.Extras || Page == NativeMenuPage.Online) return NativeMenuAction.BackToMain;
            if (Page == NativeMenuPage.Pause) return NativeMenuAction.Resume;
            if (Page == NativeMenuPage.Custom || Page == NativeMenuPage.Tracks) return NativeMenuAction.BackToCup;
            if (Page == NativeMenuPage.RaceModes) return NativeMenuAction.BackToMain;
            if (Page == NativeMenuPage.Ceremony) return NativeMenuAction.MainMenu;
            if (Page == NativeMenuPage.Characters) return NativeMenuAction.BackToMain;
            if (Page == NativeMenuPage.Cups) return NativeMenuAction.BackToCharacters;
            if (Page == NativeMenuPage.Skins) return NativeMenuAction.BackToVehicles;
            return Page == NativeMenuPage.Vehicles ? NativeMenuAction.BackToCharacters : NativeMenuAction.None;
        }
    }

    public static class NativeTrackCatalog
    {
        static readonly string[] ids = { "forest", "mountains", "purplefield", "wintermistylake" };
        static readonly string[] names = { "Forest", "Mountains", "Purplefield", "Winter Misty Lake" };
        static readonly string[] scenes = { "ForestRace", "MountainsRace", "PurplefieldRace", "WinterMistyLakeRace" };
        static readonly string[] bestKeys = { "NativeForestBest", "NativeMountainsBest", "NativePurplefieldBest", "NativeWinterMistyLakeBest" };

        public static int Count => ids.Length;
        public static string IdAt(int index) => ids[index];
        public static string NameAt(int index) => names[index];
        public static string SceneAt(int index) => scenes[index];
        public static string BestKeyAt(int index) => bestKeys[index];

        public static int IndexOf(string value)
        {
            if (string.IsNullOrEmpty(value)) return -1;
            string normalized = value.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");
            for (int i = 0; i < ids.Length; i++)
                if (string.Equals(ids[i], normalized, StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}
