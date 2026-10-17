using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        public TextAsset localViewsSource;
        NativeLocalViewsSource localViewReference;
        sealed class LocalRaceView
        {
            public int slot, racer;
            public NativeKartMotor motor;
            public Transform racerTransform;
            public uint ownerId;
            public Camera camera;
            public NativeKartCamera rig;
            public NativeKartFinishCamera finish;
            public NativeRaceHud hud;
            public NativeKartItemHud item;
            public NativeKartMotor[] markerOrder;
            public float wrongWayTime;
        }
        readonly List<LocalRaceView> localRaceViews = new List<LocalRaceView>();
        Rect localWholeCameraRect;
        public static Rect OriginalLocalViewport(int count, int index)
        {
            if (count < 1 || count > 4 || index < 0 || index >= count) throw new ArgumentOutOfRangeException();
            if (count == 1) return new Rect(0, 0, 1, 1);
            if (count == 2) return new Rect(0, index * .5f, 1, .5f);
            if (count == 3 && index == 2) return new Rect(0, 0, 1, .5f);
            return new Rect((index % 2) * .5f, index < 2 ? .5f : 0, .5f, .5f);
        }
        bool LocalRaceViewsRequested => !(Net && Net.Open) && sourceRaceState != null && racers != null && progress != null
            && !captureDetail && !ceremony && (phase == Phase.Countdown || phase == Phase.Racing || phase == Phase.Paused)
            && (localRaceViews.Count > 0 || ActualLocalViewCount() > 1);
        int ActualLocalViewCount()
        {
            int count = 0;
            for (int slot = 0; slot < sourceRaceState.Humans.Length; slot++)
                if (ActualViewHuman(slot, sourceRaceState.Humans[slot])) count++;
            return count;
        }
        bool ActualViewHuman(int slot, NativeRaceStateFlow.HumanSlot human)
        {
            var owner = SourceActualLocalOwner(slot);
            return owner && owner.user.valid && human.Joined && human.RacerValid && human.RacerIndex >= 0
                && human.RacerIndex < racers.Length && human.InputUserIndex == owner.user.index;
        }
        static NativeRaceHud LocalHudResources(NativeRaceHud source)
        {
            var result = new NativeRaceHud();
            foreach (var field in typeof(NativeRaceHud).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.IsNotSerialized || field.IsInitOnly) continue;
                var type = field.FieldType;
                if (typeof(UnityEngine.Object).IsAssignableFrom(type) || type.IsValueType || type == typeof(string) || type.IsArray)
                    field.SetValue(result, field.GetValue(source));
            }
            result.SuppressPointerInput = true;
            return result;
        }
        void EnsureLocalRaceViews()
        {
            if (!LocalRaceViewsRequested) { DisposeLocalRaceViews(); return; }
            if (localViewReference == null)
            {
                if (!localViewsSource) throw new InvalidOperationException("Independent original local views source resource missing");
                localViewReference = JsonUtility.FromJson<NativeLocalViewsSource>(localViewsSource.text);
                if (localViewReference.schemaVersion != 1 || localViewReference.viewports.Length != 10) throw new InvalidOperationException("Original local view denominator differs");
            }
            // Source race ownership and viewport assignments stay frozen until race cleanup.
            if (localRaceViews.Count > 0) return;
            var humanSlots = new List<int>(4);
            for (int slot = 0; slot < sourceRaceState.Humans.Length; slot++)
                if (ActualViewHuman(slot, sourceRaceState.Humans[slot])) humanSlots.Add(slot);
            DisposeLocalRaceViews();
            if (humanSlots.Count <= 1) return;
            localWholeCameraRect = raceCamera.rect;
            for (int index = 0; index < humanSlots.Count; index++)
            {
                int racer = sourceRaceState.Humans[humanSlots[index]].RacerIndex;
                Camera camera;
                NativeKartCamera rig;
                NativeKartFinishCamera finish;
                if (index == 0) { camera = raceCamera; rig = cameraRig; finish = FinishCamera; rig.target = racers[racer]; rig.SnapToTarget(); }
                else
                {
                    var node = Instantiate(raceCamera.gameObject); node.name = "Original local PlayerCamera " + humanSlots[index];
                    foreach (var listener in node.GetComponents<AudioListener>()) listener.enabled = false;
                    camera = node.GetComponent<Camera>(); camera.tag = "Untagged";
                    rig = NativeKartCamera.Attach(camera, racers[racer]);
                    finish = NativeKartFinishCamera.Attach(rig, finishCameraController);
                    rig.SnapToTarget();
                }
                camera.depth = localViewReference.cameraDepth; camera.targetTexture = null; camera.targetDisplay = localViewReference.cameraTargetDisplay;
                camera.rect = localViewReference.viewports.Single(row => row.count == humanSlots.Count && row.index == index).Value;
                var localHud = LocalHudResources(hud);
                localHud.BindLocalRaceView(this, racer, camera);
                NativeKartItemHud localItem = index == 0 ? itemHud : itemHud ? Instantiate(itemHud.gameObject).GetComponent<NativeKartItemHud>() : null;
                if (localItem) { localItem.inventory = racers[racer].GetComponent<NativeKartItemInventory>(); localItem.BindLocalCamera(camera); localItem.ResetState(); }
                var ordered = new NativeKartMotor[racers.Length];
                ordered[0] = racers[racer];
                int orderedIndex = 1;
                for (int actor = 0; actor < racers.Length; actor++) if (actor != racer) ordered[orderedIndex++] = racers[actor];
                localRaceViews.Add(new LocalRaceView { slot = humanSlots[index], racer = racer, motor = racers[racer], racerTransform = racers[racer].transform, ownerId = SourceActualLocalOwner(humanSlots[index]).user.id, camera = camera, rig = rig, finish = finish, hud = localHud, item = localItem, markerOrder = ordered });
            }
            var insanity = itemWorld.GetComponent<NativeInsanityHudRouter>();
            if (!insanity) insanity = itemWorld.gameObject.AddComponent<NativeInsanityHudRouter>();
            insanity.Bind(localRaceViews.Select(view => view.motor).ToArray(), localRaceViews.Select(view => view.hud.PrepareInsanityPlayerHudCanvas()).ToArray());
            hud.SetVisible(false);
        }
        bool LocalPrimaryCameraEnded()
        { return localRaceViews.Count > 0 ? progress[localRaceViews[0].racer].Finished : phase == Phase.Finished; }
        void StepLocalRaceViews()
        {
            EnsureLocalRaceViews();
            foreach (var view in localRaceViews)
            {
                if (!progress[view.racer].Finished) view.camera.fieldOfView = NativeUserSettings.GetInt("fov");
                if (progress[view.racer].Finished && !view.finish.Ended) view.finish.OnEndedRace();
                var owner = SourceActualLocalOwner(view.slot);
                if (owner && owner.user.valid && owner.user.id == view.ownerId && SourceHumanDrivingEnabled(view.slot) && phase != Phase.Paused)
                {
                    view.rig.SetLookBehind(NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.LookBehind, view.slot));
                    if (view.rig != cameraRig && NativeInputBindings.GetButtonDown(NativeInputBindings.Action.ChangeView, view.slot)) view.rig.ToggleView();
                }
                else view.rig.SetLookBehind(false);
                if (view.rig != cameraRig && phase != Phase.Paused) view.rig.Step(Time.deltaTime, progress[view.racer].Finished);
                int trigger = progress[view.racer].CurrentTrigger;
                if (trigger >= gates.Length) trigger = 0;
                if (trigger >= 0 && gates.Length > 0 && Vector3.Dot(view.racerTransform.forward, gates[trigger].forward) < -.2f) view.wrongWayTime += Time.deltaTime;
                else view.wrongWayTime = 0;
                view.hud.OriginalWrongWay = view.wrongWayTime >= .5f;
            }
        }
        void NotifyLocalViewLap(int racer)
        { foreach (var view in localRaceViews) if (view.racer == racer) view.hud.NotifyOriginalLap(); }
        bool DrawLocalRaceViews()
        {
            EnsureLocalRaceViews();
            if (localRaceViews.Count == 0) return false;
            if (Event.current.type == EventType.Repaint) localViewsOnGuiFrame = Time.frameCount;
            bool overlays = SettingsOpen;
            foreach (var view in localRaceViews)
            {
                view.hud.SetVisible(!overlays);
                view.hud.DrawOriginalVehicleModuleBar(view.motor, view.camera, true, !overlays && !progress[view.racer].Finished);
                if (view.item) { view.item.SetVisible(!overlays); if (progress[view.racer].Finished && !view.item.LocalRaceFinished) view.item.FreezeForLocalFinisher(); }
                if (overlays) continue;
                float width = view.hud.BeginFrame();
                try
                {
                    view.hud.RaceFinished = progress[view.racer].Finished;
                    view.hud.ShowRaceLapTimes = selectedMode == NativeSessionMode.TimeTrials;
                    view.hud.ShowRaceRank = selectedMode != NativeSessionMode.TimeTrials;
                    view.hud.RaceLapCount = RaceLaps;
                    view.hud.RaceLapSeconds = lapSeconds[view.racer];
                    view.hud.CurrentLapSeconds = sourceLapTimers != null ? sourceLapTimers[view.racer].LapSeconds().ElementAtOrDefault(progress[view.racer].CompletedLaps) : PreciseElapsed - lapTimestamps[view.racer];
                    view.hud.OriginalBaseTopSpeed = view.motor.OriginalHudBaseTopSpeed;
                    view.hud.CurrentLap = progress[view.racer].CompletedLaps + 1;
                    view.hud.Race(width, sourceLapTimers != null ? SourceTotalSeconds(view.racer) : PreciseElapsed, RankOf(view.motor), view.motor.Speed, route, view.markerOrder);
                }
                finally { view.hud.EndFrame(); }
            }
            float globalWidth = hud.BeginFrame();
            try
            {
                if (phase == Phase.Countdown || (phase == Phase.Racing && PreciseElapsed < 1.5))
                    hud.Countdown(phase == Phase.Countdown ? Mathf.CeilToInt(countdown).ToString() : "START");
                if (!overlays && (phase == Phase.Countdown || phase == Phase.Racing))
                    hud.DrawSharedOriginalMinimap(racers);
                if (phase == Phase.Paused || overlays)
                    if (!DrawSettings(globalWidth)) ExecuteMenuAction(hud.PauseMenu(globalWidth));
            }
            finally { hud.EndFrame(); }
            return true;
        }
        void DisposeLocalRaceViews(bool restoreNaturalConsumer = true)
        {
            if (localRaceViews.Count == 0) return;
            foreach (var view in localRaceViews)
            {
                view.hud.DisposeLocalRaceView();
                view.hud.DrawOriginalVehicleModuleBar(racers != null && view.racer < racers.Length ? racers[view.racer] : null, view.camera, true, false);
                if (view.item && view.item != itemHud) Destroy(view.item.gameObject);
                if (view.rig && view.rig != cameraRig) Destroy(view.rig.gameObject);
            }
            localRaceViews.Clear();
            if (itemHud) itemHud.BindWholeScreen();
            int primary = SourcePrimaryRacerIndex;
            if (itemWorld)
            {
                var router = itemWorld.GetComponent<NativeInsanityHudRouter>();
                if (restoreNaturalConsumer && SourceRacerObjectValid(primary) && hud != null && hud.authoredUiData)
                {
                    if (!router) router = itemWorld.gameObject.AddComponent<NativeInsanityHudRouter>();
                    router.Bind(new[]{racers[primary]},new[]{hud.PrepareInsanityPlayerHudCanvas()});
                }
                else if (router) router.Clear();
            }
            if (raceCamera) raceCamera.rect = localWholeCameraRect;
            if (cameraRig && racers != null && primary >= 0 && primary < racers.Length) cameraRig.target = racers[primary];
        }
    }
}
