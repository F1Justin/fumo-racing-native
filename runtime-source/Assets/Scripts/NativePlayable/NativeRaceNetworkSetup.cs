using System;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        bool onlineCustomDropdown;
        int onlineCustomRow, onlineCustomOption;
        int MinimumSetupRacers => Mathf.Max(1, Participants.Length);
        bool IsSetupChoice => setupPage && (onlineChoice == OnlineChoice.RaceModes || onlineChoice == OnlineChoice.Cups || onlineChoice == OnlineChoice.Tracks || onlineChoice == OnlineChoice.Custom);
        internal int LocalRacerIndex => RacerIndex(LocalSeat);
        internal int RacerIndex(int peerSeat)
        {
            var seats = (InRace ? roundConfig : null)?.humanSeats ?? Config.humanSeats;
            return seats == null || seats.Length == 0 ? peerSeat : Array.IndexOf(seats, peerSeat);
        }
        internal int PeerSeat(int racerIndex) => (InRace ? roundConfig ?? Config : Config).PeerSeat(racerIndex);
        void NormalizeSetupCount() => Config.racers = Mathf.Clamp(Config.racers, MinimumSetupRacers, Config.mode == NativeSessionMode.CustomRace ? 100 : 8);
        void PrepareRoundConfig()
        {
            NormalizeSetupCount();
            Config.humanSeats = Participants.OrderBy(peer => peer.seat).Select(peer => peer.seat).ToArray();
            if (Config.mode == NativeSessionMode.GrandPrix) Config.track = NativeModeCatalog.CupTrackIds[Mathf.Clamp(Config.gpStation, 0, NativeModeCatalog.CupTrackIds.Length - 1)];
        }
        void HandleOnlineSetupAction(NativeMenuAction action)
        {
            if (!IsHost || action == NativeMenuAction.None) return;
            if (action == NativeMenuAction.GrandPrix || action == NativeMenuAction.QuickRace || action == NativeMenuAction.CustomRace)
            {
                Config.mode = action == NativeMenuAction.GrandPrix ? NativeSessionMode.GrandPrix : action == NativeMenuAction.CustomRace ? NativeSessionMode.CustomRace : NativeSessionMode.QuickRace;
                var definition = NativeModeCatalog.Get(Config.mode);
                Config.gpStation = 0; Config.laps = definition.DefaultLaps; Config.racers = Mathf.Max(MinimumSetupRacers, definition.DefaultRacerCount);
                Config.itemType = 0; Config.items = true;
                OnlineHud.SelectionRecordMode = Config.mode;
                BroadcastConfig(); SetOnlineChoice(OnlineChoice.Cups);
            }
            else if (action == NativeMenuAction.ConfirmCup)
            {
                if (Config.mode == NativeSessionMode.GrandPrix) { Config.track = NativeModeCatalog.CupTrackIds[Mathf.Clamp(Config.gpStation, 0, NativeModeCatalog.CupTrackIds.Length - 1)]; StartRound(); }
                else SetOnlineChoice(OnlineChoice.Tracks);
            }
            else if (action == NativeMenuAction.ConfirmCircuit)
            {
                Config.track = NativeTrackCatalog.IdAt(OnlineHud.SelectedMenuIndex); BroadcastConfig(); Send("candidate", null);
                if (Config.mode == NativeSessionMode.CustomRace) SetOnlineChoice(OnlineChoice.Custom); else StartRound();
            }
            else if (action == NativeMenuAction.StartRace) StartRound();
            else if (action == NativeMenuAction.CustomItems || action == NativeMenuAction.CustomRespawn)
            {
                if (!onlineCustomDropdown) { onlineCustomRow = action == NativeMenuAction.CustomItems ? 3 : 4; onlineCustomOption = OnlineCustomValue(onlineCustomRow); onlineCustomDropdown = true; OnlineHud.PlayCustomizationSound(1); }
            }
            else if (action == NativeMenuAction.BackToMain || action == NativeMenuAction.BackToCharacters || action == NativeMenuAction.BackToCup) CancelOnlineSetup();
        }
        void CancelOnlineSetup()
        {
            if (onlineCustomDropdown) { CloseOnlineCustomDropdown(); return; }
            Send("candidate", null);
            if (onlineChoice == OnlineChoice.RaceModes) ReturnFromRaceSetup();
            else if (onlineChoice == OnlineChoice.Custom) SetOnlineChoice(OnlineChoice.Tracks);
            else if (onlineChoice == OnlineChoice.Tracks) SetOnlineChoice(OnlineChoice.Cups);
            else SetOnlineChoice(OnlineChoice.RaceModes);
        }
        void CloseOnlineCustomDropdown() { onlineCustomDropdown = false; OnlineHud.CloseCustomDropdown(); }
        int OnlineCustomValue(int row) => row == 1 ? Config.laps : row == 2 ? Config.racers : row == 3 ? Config.itemType : (Config.customRules ?? (Config.customRules = new NativeCustomRaceRules())).Value(row);
        void SetOnlineCustomValue(int row, int value)
        {
            if (!IsHost) return;
            if (row == 1) Config.laps = Mathf.Clamp(value, 1, 100);
            else if (row == 2) Config.racers = Mathf.Clamp(value, MinimumSetupRacers, 100);
            else if (row == 3) { Config.itemType = Mathf.Clamp(value, 0, 2); Config.items = Config.itemType != 2; }
            else if (row > 3) (Config.customRules ?? (Config.customRules = new NativeCustomRaceRules())).Set(row, value);
            BroadcastConfig();
        }
        NativeMenuAction RenderOnlineCustom(float width)
        {
            NormalizeSetupCount();
            return OnlineHud.CustomModeMenu(width, OnlineCustomValue, SetOnlineCustomValue, onlineCustomDropdown, onlineCustomOption,
                option => { SetOnlineCustomValue(onlineCustomRow, option); CloseOnlineCustomDropdown(); }, CloseOnlineCustomDropdown,
                option => onlineCustomOption = option, row => row == 2 ? MinimumSetupRacers : NativeCustomRaceRules.Minimum(row));
        }
        void NavigateOnlineCustom(Vector2 direction)
        {
            int delta = Mathf.Abs(direction.y) >= Mathf.Abs(direction.x) ? direction.y < 0 ? 1 : -1 : direction.x > 0 ? 1 : -1;
            if (onlineCustomDropdown) onlineCustomOption = Mathf.Clamp(onlineCustomOption + delta, 0, onlineCustomRow == 3 ? 2 : 1);
            else if (Mathf.Abs(direction.y) >= Mathf.Abs(direction.x)) OnlineHud.NavigateMenu(NativeMenuPage.Custom, delta);
            else SetOnlineCustomValue(OnlineHud.SelectedMenuIndex, OnlineCustomValue(OnlineHud.SelectedMenuIndex) + delta);
        }
    }
    public static class NativeNetworkGrid
    {
        // AI slots have no room identity.
        public static int PeerSeat(this NativeNetworkConfig config, int racerIndex)
            => config.humanSeats == null || config.humanSeats.Length == 0 ? racerIndex
                : racerIndex >= 0 && racerIndex < config.humanSeats.Length ? config.humanSeats[racerIndex] : -1;
    }
}
