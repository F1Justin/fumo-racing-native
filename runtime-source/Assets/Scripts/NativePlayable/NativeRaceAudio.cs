using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        string nativeAudioContext;
        float nativeAudioResultStarted;
        public void AudioInitialize(string[] args, bool verification)
        {
            if (Array.IndexOf(args ?? Array.Empty<string>(), "--winter-runtime-audit") >= 0
                || Array.IndexOf(args ?? Array.Empty<string>(), "--winter-snowman-contact-self-test") >= 0)
                NativeAudioRuntime.VerificationListenerSilenced = Array.IndexOf(args ?? Array.Empty<string>(), "--winter-ordinary-audio-output") < 0;
            NativeAudioRuntime.Initialize(args, verification);
            if (musicSource) { musicSource.Stop(); musicSource.enabled = false; }
            if (menuPresentation) menuPresentation.soundtrack = null;
            nativeAudioContext = null;
        }
        public void AudioContextTick()
        {
            if (StartupSplashActive) return;
            if (raceConfig != null && racers != null)
                for (int i = 0; i < racers.Length && i < raceConfig.roster.Count; i++)
                    NativeVehicleAudio.Bind(racers[i], raceConfig.roster[i].characterId, raceConfig.roster[i].vehicleId);
            if (hud.MenuPage.ToString() == "MusicRoom") { NativeAudioRuntime.PauseMenuForMusicRoom(); return; }
            bool pause = phase == Phase.Paused;
            NativeAudioRuntime.SetPaused(pause);
            if (pause) return;
            string context = ceremony ? "GrandPrixCeremony" : phase == Phase.Menu ?
                hud.MenuPage == NativeMenuPage.Tracks ? "TrackSelect" : hud.MenuPage == NativeMenuPage.Characters ? "CharSelect" :
                hud.MenuPage == NativeMenuPage.Vehicles || hud.MenuPage == NativeMenuPage.Custom ? "CustomSelect" : "Menu" :
                phase == Phase.Finished ? "Results" : phase == Phase.Countdown ? "Countdown" :
                trackId == "forest" ? "Forest" : trackId == "mountains" ? "Mountains" : trackId == "purplefield" ? "Purplefield" : "WistyLake";
            if (context != nativeAudioContext)
            {
                nativeAudioContext = context;
                if (context == "Countdown") NativeAudioRuntime.StopMusic();
                else if (context == "Results") { nativeAudioResultStarted = Time.time; NativeAudioRuntime.EndCurrentMusic(); }
                else if (context == "TrackSelect" || context == "CharSelect" || context == "CustomSelect")
                    NativeAudioRuntime.PlaySelectionLayer(context == "TrackSelect" ? 0 : context == "CharSelect" ? 1 : 2);
                else NativeAudioRuntime.PlayMusic(context);
            }
            int audioRacer=Net&&Net.InRace?Net.LocalRacerIndex:0;
            var playerInventory = racers != null && audioRacer>=0 && audioRacer<racers.Length ? racers[audioRacer].GetComponent<NativeKartItemInventory>() : null;
            NativeAudioRuntime.PlayFullPower(phase == Phase.Racing && playerInventory && playerInventory.Effects && playerInventory.Effects.IsActive("FullPower"));
            if (context == "Results" && Time.time - nativeAudioResultStarted >= 4.75f) NativeAudioRuntime.PlayMusic("Results");
        }
    }
}
