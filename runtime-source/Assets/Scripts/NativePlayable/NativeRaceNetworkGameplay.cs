using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        readonly Dictionary<int, GameObject> networkActors = new Dictionary<int, GameObject>();
        string networkOwnerTelemetryReason;
        internal PlayerInput NetworkActualOwner()
        {
            var service=NativeLocalSelectionOwners.Instance;
            if(!service||!Net||Net.StableLocalOwnerId==0)return ObserveNetworkOwner(null,"stable-owner-unavailable");
            var owner=service.FindOwner(Net.StableLocalOwnerId);
            if(!owner||!owner.user.valid)return ObserveNetworkOwner(null,"owner-user-unavailable");
            if(owner.currentControlScheme!=Net.StableLocalOwnerScheme)return ObserveNetworkOwner(null,"scheme-changed");
            var paired=owner.user.pairedDevices;
            if(paired.Count==0)return ObserveNetworkOwner(null,"paired-devices-empty");
            if(paired.Any(device=>!device.enabled))return ObserveNetworkOwner(null,"paired-device-disabled");
            if(!paired.Select(device=>device.deviceId).OrderBy(id=>id).SequenceEqual(Net.StableLocalOwnerDevices.OrderBy(id=>id)))return ObserveNetworkOwner(null,"paired-devices-changed");
            return ObserveNetworkOwner(owner,"valid");
        }
        PlayerInput ObserveNetworkOwner(PlayerInput owner,string reason)
        {
            if (networkOwnerTelemetryReason != reason)
            {
                networkOwnerTelemetryReason = reason;
                NativeTelemetry.SetContext("ownerStatus", reason);
                NativeTelemetry.Event("network.owner.changed", "observed", detail: reason);
            }
            return owner;
        }
        bool AdoptNetworkLocalOwner(InputDevice entryDevice=null)
        {
            var service=NativeLocalSelectionOwners.Instance ?? NativeLocalSelectionOwners.Create();
            var owner=NetworkActualOwner();
            if(entryDevice!=null&&owner&&!service.Paired(owner,entryDevice))owner=null;
            if(!owner)
            {
                var device=entryDevice;
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
                if(device==null&&Net.StableLocalOwnerId==0&&!Net.DiagnosticOwnerCreated&&service.Players.Length==0
                    &&Environment.GetCommandLineArgs().Contains("--net-owner-diagnostic"))
                {
                    Net.DiagnosticOwnerCreated=true;
                    InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                    if(Mouse.current!=null && !Mouse.current.enabled)InputSystem.EnableDevice(Mouse.current);
                    device=Environment.GetCommandLineArgs().Contains("--net-parity-diagnostic") && !Environment.GetCommandLineArgs().Contains("--net-parity-keyboard")
                        ? (InputDevice)InputSystem.AddDevice<Gamepad>() : InputSystem.AddDevice<Keyboard>();service.MarkDiagnosticOwner(true);
                    Debug.Log("NATIVE_NETWORK_OWNER NativeInjected physicalHumanAccepted=false");
                }
#endif
                if(device==null||!device.enabled)return false;
                owner=service.Players.FirstOrDefault(p=>p&&p.user.valid&&service.Paired(p,device));
                if(!owner)owner=service.JoinConfirmedNetworkOwner(device);
                if(!owner||!owner.user.valid||!service.Paired(owner,device))return false;
                Net.StableLocalOwnerId=owner.user.id;
                Net.StableLocalOwnerDevices=owner.user.pairedDevices.Select(paired=>paired.deviceId).ToArray();
                Net.StableLocalOwnerScheme=owner.currentControlScheme;
            }
            service.End();
            NativeInputBindings.SetActualLocalOwners(new[]{owner,null,null,null});
            return true;
        }
        float networkSnapshotAt;
        NativeNetworkSnapshot networkTarget;
        NativeRaceNetwork Net => NativeRaceNetwork.Instance;
        internal int NetworkVisualActorCount => networkActors.Count;
        internal int NetworkCameraSeat => cameraRig ? Array.IndexOf(racers, cameraRig.target) : -1;
        internal bool NetworkReceiverReady { get; private set; }
        internal void NetworkInitialize() { NetworkReceiverReady=true; if (Net) { Net.Bind(this); if (Net.Open && !Net.InRace) NetworkOpenMenu(); } }
        internal void NetworkOpenMenu(InputDevice entryDevice=null)
        {
            AdoptNetworkLocalOwner(entryDevice);
            DisposeLocalRaceViews(false);
            ApplyNetworkPresentationClock();
            Time.timeScale = 1;
            if (menuPresentation) menuPresentation.SetVisible(true);
            hud.SetMenuPage(NativeMenuPage.Online);
            heldMenuDirection = 0; mainQuitSource?.Release(); PublishMainQuitSource();
        }
        void ApplyNetworkPresentationClock()
        {if(Net && Net.Open && Application.platform==RuntimePlatform.OSXPlayer)QualitySettings.vSyncCount=0;}
        internal void NetworkCloseMenu()
        {
            NetworkFinishSourceCircuit();
            NativeUserSettings.Apply(raceCamera);
            if(NativeLocalSelectionOwners.Instance) NativeInputBindings.SetActualLocalOwners(NativeLocalSelectionOwners.Instance.Players);
            hud.SetMenuPage(NativeMenuPage.Main);
            hud.SelectMenuIndex(2);
            heldMenuDirection = 0; mainQuitSource?.Release(); PublishMainQuitSource();
        }
        internal bool NetworkChangeTrack(string track) => ChangeTrack(track, false);
        internal void NetworkLobby()
        {
            NetworkFinishSourceCircuit();
            foreach (var actor in networkActors.Values) if (actor) Destroy(actor);
            networkActors.Clear();
            if (progress == null) return;
            ResetRace(false);
            cameraRig = NativeKartCamera.Attach(raceCamera, racers[0]);
            FinishCamera = NativeKartFinishCamera.Attach(cameraRig, finishCameraController);
            cameraRig.SnapToTarget();
            if (itemHud) itemHud.inventory = racers[0].GetComponent<NativeKartItemInventory>();
            foreach (var racer in racers)
            {
                racer.enabled = true;
                racer.GetComponent<NativeKartItemInventory>().enabled = true;
                racer.GetComponent<NativeKartHit>().working = true;
                var effects = racer.GetComponent<NativeKartItemEffects>(); if (effects) effects.enabled = true;
            }
            if (itemWorld) itemWorld.enabled = true;
            var ufo = GetComponent<NativeUfoManager>(); if (ufo) ufo.enabled = true;
            foreach (var box in FindObjectsOfType<NativeKartItemBox>(true)) box.enabled = true;
            networkTarget = null;
            if (Net && Net.Open) { hud.SetMenuPage(NativeMenuPage.Online); hud.ClearMenuTransition(); }
        }
        internal void NetworkPrepare(NativeNetworkConfig config)
        {
            if(!AdoptNetworkLocalOwner())throw new InvalidOperationException("Network round requires a confirmed actual owner");
            DisposeLocalRaceViews(false);
            ApplyNetworkPresentationClock();
            sessionId = null;
            selectedMode = config.mode;
            var roster = new List<NativeSessionSeat>();
            int count = Mathf.Clamp(config.racers, 1, 100);
            for (int i = 0; i < count; i++)
            {
                var choice = config.selections.FirstOrDefault(s => s.seat == config.PeerSeat(i));
                bool human = Net.Participants.Any(p => p.seat == Net.PeerSeat(i));
                int character = choice == null ? i % vehicleRoster.CharacterCount : vehicleRoster.CharacterIndexOf(choice.character);
                int vehicle = choice == null ? i % vehicleRoster.VehicleCount : vehicleRoster.VehicleIndexOf(choice.vehicle);
                character = Mathf.Clamp(character, 0, vehicleRoster.CharacterCount - 1); vehicle = Mathf.Clamp(vehicle, 0, vehicleRoster.VehicleCount - 1);
                roster.Add(new NativeSessionSeat { seatId = "seat-" + i, isHuman = human,
                    characterId = vehicleRoster.CharacterIdAt(character), vehicleId = vehicleRoster.VehicleIdAt(vehicle),
                    skinIndex = vehicleRoster.ClampSkin(character, choice == null ? 0 : choice.skin) });
            }
            var rules = new NativeSessionConfig { racerCount = count, mode = config.mode, modeId = NativeModeCatalog.Get(config.mode).Id, customRules = (config.customRules ?? new NativeCustomRaceRules()).Copy(), laps = Mathf.Clamp(config.laps, 1, 100), roster = roster,
                trackIds = new List<string> { trackId }, itemType = !config.items ? "none" : config.itemType == 1 ? "ufo" : "normal" };
            rules.sourceStorageOrder=Enumerable.Range(0,count).ToList();
            PrepareModeRoster(rules);
            ConfigureItemInventories(); ResetRace(true, false);
            networkObservedMinimapFrame=-1;networkObservedMinimapOrder=null;networkObservedModuleDrawFrame=-1;
            NetworkPrepareSourceCircuit();
            phase = Phase.Countdown; countdown = 3;
            for (int i = 0; i < racers.Length; i++)
            {
                var peer = Net.Peers.FirstOrDefault(p => !p.spectator && p.seat == Net.PeerSeat(i));
                if (peer != null) racerNames[i] = peer.name + " / " + vehicleRoster.CharacterNameAt(vehicleRoster.CharacterIndexOf(roster[i].characterId));
                racers[i].GetComponent<NativeKartItemInventory>().Configure(this, itemWorld, roster[i].characterId, peer == null && Net.IsHost);
                racers[i].GetComponent<NativeKartHit>().working = Net.IsHost;
                var effects = racers[i].GetComponent<NativeKartItemEffects>(); if (effects) effects.enabled = Net.IsHost;
            }
            itemWorld.enabled = Net.IsHost;
            foreach (var box in FindObjectsOfType<NativeKartItemBox>(true)) { box.enabled = Net.IsHost; if (!config.items) box.gameObject.SetActive(false); }
            var ufo = GetComponent<NativeUfoManager>(); if (ufo) ufo.enabled = Net.IsHost;
            if (Net.requestedSpectator) NetworkSpectatorFocus(0);
            if (Net.LocalRacerIndex >= 0 && Net.LocalRacerIndex < racers.Length && !Net.requestedSpectator)
            {
                cameraRig = NativeKartCamera.Attach(raceCamera, racers[Net.LocalRacerIndex]);
                FinishCamera = NativeKartFinishCamera.Attach(cameraRig, finishCameraController);
                cameraRig.SnapToTarget();
                if (itemHud) itemHud.inventory = racers[Net.LocalRacerIndex].GetComponent<NativeKartItemInventory>();
                BindNaturalInsanityConsumer();
                hud.SetPlayerCharacterIcon(vehicleRoster.CharacterIconAt(vehicleRoster.CharacterIndexOf(roster[Net.LocalRacerIndex].characterId)));
                hud.DrawOriginalVehicleModuleBar(racers[Net.LocalRacerIndex], raceCamera, true, false);
            }
        }
        internal void NetworkDrawHud()
        {
            // Online page visibility must settle before the shared race prefab draws.
            Net.RenderNetworkUi();
            var state = Net && Net.Latest != null && Net.Latest.karts != null
                ? Net.Latest.karts.FirstOrDefault(k => k.seat == Net.LocalRacerIndex) : null;
            if (!Net || !Net.InRace || Net.Loading || state == null)
            {
                hud.SetVisible(false);
                if (itemHud) itemHud.SetVisible(false);
                return;
            }
            if (Net.requestedSpectator || Net.OnlineRaceMenuVisible)
            {
                hud.SetVisible(false);
                if (itemHud) itemHud.SetVisible(false);
                return;
            }
            hud.SetMenuPage(NativeMenuPage.None);
            if (itemHud) itemHud.SetVisible(!state.finished);
            float width = hud.BeginFrame(true);
            try
            {
                ConfigureRaceHud(Net.LocalRacerIndex, state.finished, state.lap + 1, Net.Config.laps);
                var lapClock = state.sourceLapClock;
                hud.RaceLapSeconds = lapClock != null && lapClock.sourceLapTicks != null
                    ? lapClock.sourceLapTicks.Take(Mathf.Clamp(lapClock.sourceLapIndex,0,lapClock.sourceLapTicks.Length)).Select(ticks=>TimeSpan.FromTicks(ticks).TotalSeconds).ToList() : new List<double>();
                hud.CurrentLap = Mathf.Min(Net.Config.laps, state.lap + 1);
                hud.RaceLapCount = Net.Config.laps;
                hud.CurrentLapSeconds = lapClock != null && lapClock.sourceLapTicks != null && lapClock.sourceLapTicks.Length > 0
                    ? TimeSpan.FromTicks(lapClock.sourceLapTicks[Mathf.Clamp(lapClock.sourceLapIndex, 0, lapClock.sourceLapTicks.Length - 1)]).TotalSeconds : 0;
                hud.DrawOriginalVehicleModuleBar(racers[Net.LocalRacerIndex],raceCamera,true,!state.finished);
                ObserveNetworkModuleHudDraw();
                var displayRacers=new[]{racers[Net.LocalRacerIndex]}.Concat(racers.Where((r,i)=>i!=Net.LocalRacerIndex)).ToArray();
                hud.Race(width, state.finished ? state.finish : Net.Latest.elapsed, state.place, state.speed, route, displayRacers);
                ObserveNetworkHudConsumerOrder(displayRacers);
                if (Net.Latest.phase == "Countdown")
                    hud.Countdown(Mathf.CeilToInt(Net.Latest.countdown).ToString());
                else if(Net.Latest.phase=="Racing" && Net.Latest.elapsed<1.5f) hud.Countdown("START");
            }
            finally { hud.EndFrame(); }
        }
        void ConfigureRaceHud(int racer, bool finished, int lap, int laps)
        {
            hud.RaceFinished=finished;
            hud.ShowRaceLapTimes=selectedMode==NativeSessionMode.TimeTrials;
            hud.ShowRaceRank=selectedMode!=NativeSessionMode.TimeTrials;
            hud.OriginalBaseTopSpeed=racers[racer].OriginalHudBaseTopSpeed;
            hud.CurrentLap=lap;hud.RaceLapCount=laps;
        }
        internal void NetworkCountdown()
        {
            if(!networkSourcePrepared||networkCountdownBegun)return;
            networkCountdownBegun=true;
            countdownDeadline = Time.timeSinceLevelLoadAsDouble + 3;
            countdown = 3; phase = Phase.Countdown; countdownAudioSecond=-1;
        }
        internal bool NetworkUpdate()
        {
            if (!Net || !Net.Open) return false;
            if(Net.onlineUiFixture)return true;
            ApplyNetworkPresentationClock();
            NetworkRefreshSourceOwner();
            SynchronizeRaceLocalOwners();
            if(sourceHumanInput)sourceHumanInput.AllowMenuInput=()=>!Net||!Net.Open;
            AudioContextTick();
            TickSelectionPreview();
            if (!Net.InRace || Net.Loading) return true;
            Time.timeScale = NativeCameraLab.WorldPaused ? 0 : 1;
            if (NativeCameraLab.WorldPaused) return true;
            if(NetworkActualOwner()) ApplyCameraInput(NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.LookBehind), NativeInputBindings.GetButtonDown(NativeInputBindings.Action.ChangeView));
            if (phase == Phase.Countdown)
            {
                countdown = (float)(countdownDeadline - Time.timeSinceLevelLoadAsDouble);
                NetworkCountdownAudio();
                if (Net.IsHost && countdown <= 0)
                { phase = Phase.Racing; NetworkStartSourceRace(countdownDeadline); foreach (var racer in racers) racer.driving = true; }
                return true;
            }
            NetworkTickSourceRace();
            if (Net.IsHost && (phase == Phase.Racing || phase == Phase.Finished))
            {
                elapsed = (float)raceClock.Elapsed;
                for (int i = 0; i < racers.Length; i++)
                {
                    NativeNetworkInput input; bool human = Net.Participants.Any(p => p.seat == Net.PeerSeat(i));
                    Net.Inputs.TryGetValue(Net.PeerSeat(i), out input);
                    var motor = racers[i]; var driver = motor.GetComponent<NativeKartOriginalAI>();
                    if(Net.retiredSeats.Contains(i)){driver.active=false;motor.driving=false;continue;}
                    driver.active = !progress[i].Finished && (!human || (input != null && input.autopilot));
                    motor.driving = !progress[i].Finished;
                    if (human && input != null && !driver.active) NetworkInput(i, input, true);
                    if (input != null && input.autopilot && Net.Config.items && motor.GetComponent<NativeKartItemInventory>().ItemDecided)
                        motor.GetComponent<NativeKartItemInventory>().RequestUse();
                    if (progress[i].Finished || Net.retiredSeats.Contains(i)) continue;
                    TickRacerEmergencyRecovery(i,!human && driver.active);
                }
            }
            else if (!Net.IsHost && phase == Phase.Racing)
            {
                NativeNetworkInput input;
                if (!Net.requestedSpectator && Net.LocalRacerIndex>=0 && Net.LocalRacerIndex<racers.Length && Net.Inputs.TryGetValue(Net.LocalSeat, out input))
                {
                    var driver = racers[Net.LocalRacerIndex].GetComponent<NativeKartOriginalAI>(); driver.active = input.autopilot;
                    if (!input.autopilot) NetworkInput(Net.LocalRacerIndex, input, false);
                }
            }
            return true;
        }
        void NetworkInput(int index, NativeNetworkInput input, bool authority)
        {
            var motor = racers[index];
            motor.throttle = input.throttle; motor.steering = input.steering; motor.braking = input.brake;
            motor.drifting = input.throttle >= .4f && input.brake; motor.jump |= input.jump; motor.jumpHeld = input.jumpHeld;
            motor.WheelieInputVertical(input.vertical);
            if (authority && input.use) motor.GetComponent<NativeKartItemInventory>().RequestUse();
            if (authority && input.recover) Recover(index, true, "network-input");
            if (authority) Net.TelemetryAppliedInput(Net.PeerSeat(index), index, input, motor);
            input.jump = input.use = input.recover = false;
        }
        internal bool NetworkLateUpdate()
        {
            if (!Net || !Net.Open) { SetBroadcastView(false); return false; }
            if (Net.onlineUiFixture) { StepNetworkBroadcast(Time.unscaledDeltaTime); return true; }
            if (!Net.InRace || Net.Loading || !cameraRig) { SetBroadcastView(false); return true; }
            if (Net.IsHost) { UpdateProgress(); NetworkTickSourceRace(); Net.Latest = NetworkSnapshot(); }
            foreach (var racer in racers) racer.PrepareRenderPose(true);
            if (!Net.IsHost && networkTarget != null)
            {
                float prediction = Mathf.Clamp(Time.realtimeSinceStartup - networkSnapshotAt, 0f, .1f);
                foreach (var state in networkTarget.karts)
                {
                    if (state.seat == Net.LocalRacerIndex) continue;
                    var motor = racers[state.seat];
                    motor.ApplyNetworkPose(state.position + state.velocity * prediction, state.rotation, state.velocity, state.speed, state.distance, false);
                }
            }
            var own = Net.Latest == null ? null : Net.Latest.karts.FirstOrDefault(k => k.seat == Net.LocalRacerIndex);
            if(!Net.requestedSpectator && own!=null && own.finished && FinishCamera && !FinishCamera.Ended)FinishCamera.OnEndedRace();
            if (!StepNetworkBroadcast(Time.unscaledDeltaTime))
                cameraRig.Step(Time.deltaTime, !Net.requestedSpectator && own != null && own.finished);
            return true;
        }
        internal NativeNetworkSnapshot NetworkSnapshot()
        {
            var states = new NativeNetworkKart[racers.Length];
            for (int i = 0; i < racers.Length; i++)
            {
                var motor = racers[i]; var inventory = motor.GetComponent<NativeKartItemInventory>(); var hit = motor.GetComponent<NativeKartHit>();
                states[i] = new NativeNetworkKart { sourceAudio=NetworkReadSourceAudio(i),seat = i, position = motor.transform.position, rotation = motor.transform.rotation,
                    recoveryRequests=motor.GetComponent<NativeKartRecovery>().State.Requests,
                    velocity = motor.DriveVelocity, speed = motor.Speed, distance = motor.Distance, lap = progress[i].CompletedLaps,
                    checkpoint = progress[i].Passed, finished = progress[i].Finished || Net.retiredSeats.Contains(i), retired = Net.retiredSeats.Contains(i), finish = finishTimes[i], place = Net.retiredSeats.Contains(i) ? 0 : RankOf(motor),
                    sourceLapClock = sourceLapTimers!=null&&i<sourceLapTimers.Length&&raceConfig.roster[i].isHuman?sourceLapTimers[i].ExportNetworkClock():null,
                    inventory = inventory.State, frozen = hit.Frozen, invulnerable = hit.Invulnerable, damageVisible = hit.State.DamageVisible,
                    damage = hit.State.AcceptedDamage, effects = inventory.Effects.ActiveCount, effectIds = inventory.Effects.NetworkEffectIds, reversed = motor.ControlsReversed,
                    topSpeed = motor.stats._topSpeed, topSpeedAdd = motor.stats._topSpeedAdd, acceleration = motor.stats._acceleration, grassSpeed = motor.stats._topSpeedGrass };
            }
            var catalog = GetComponent<NativeItemCatalog>();
            var actors = FindObjectsOfType<NativeItemActor>().Where(a => !a.Finished).Select(a => new NativeNetworkActor { id = a.GetInstanceID(),
                owner = Array.IndexOf(racers, a.Owner), item = a.ItemId, visual = NetworkPrefabIndex(catalog.Find(a.ItemId), a.NetworkVisualPrefab),
                mode = (int)a.Mode, position = a.transform.position, rotation = a.transform.rotation, carried = a.Carried }).ToList();
            foreach (var effect in itemWorld.NetworkVisualEffects)
            {
                if (!effect.Key || !effect.Value) continue;
                var entry = catalog.entries.FirstOrDefault(e => NetworkPrefabIndex(e, effect.Value) >= 0);
                if (entry == null) continue;
                actors.Add(new NativeNetworkActor { id = effect.Key.GetInstanceID(), owner = -1, item = entry.id,
                    visual = NetworkPrefabIndex(entry, effect.Value), mode = (int)NativeItemActorMode.Effect,
                    position = effect.Key.transform.position, rotation = effect.Key.transform.rotation });
            }
            return NetworkMeasureAudioPacket(new NativeNetworkSnapshot { sourceClockAvailable=networkSourceStarted,sourceClockStopped=networkSourceClockStopped,sourceClockElapsed=sourceGameTime,phase = phase.ToString(), elapsed = elapsed, countdown = countdown, karts = states, actors = actors.ToArray() });
        }
        static GameObject NetworkPrefab(NativeItemCatalog.Entry entry, int index)
        {
            if (entry == null) return null;
            if (index >= 100) return entry.shieldPrefabs != null && index - 100 < entry.shieldPrefabs.Length ? entry.shieldPrefabs[index - 100] : null;
            switch (index)
            {
                case 0: return entry.throwPrefab;
                case 1: return entry.carryPrefab;
                case 2: return entry.itemToDropPrefab;
                case 3: return entry.frogPrefab;
                case 4: return entry.meleePrefab;
                case 5: return entry.effectPrefab;
                case 6: return entry.affectedEffectPrefab;
                case 7: return entry.projectile == null ? null : entry.projectile.explosionPrefab;
                case 8: return entry.carryProjectile == null ? null : entry.carryProjectile.explosionPrefab;
                case 9: return entry.dropProjectile == null ? null : entry.dropProjectile.explosionPrefab;
                case 10: return entry.frog == null ? null : entry.frog.explosionPrefab;
                case 11: return entry.dropFrog == null ? null : entry.dropFrog.explosionPrefab;
                case 12: return entry.shieldPrefab;
                default: return null;
            }
        }
        static int NetworkPrefabIndex(NativeItemCatalog.Entry entry, GameObject prefab)
        {
            if (entry == null || !prefab) return -1;
            for (int i = 0; i <= 12; i++) if (NetworkPrefab(entry, i) == prefab) return i;
            for (int i = 0; entry.shieldPrefabs != null && i < entry.shieldPrefabs.Length; i++)
                if (entry.shieldPrefabs[i] == prefab) return 100 + i;
            return -1;
        }
        internal void NetworkApply(NativeNetworkSnapshot snapshot)
        {
            if (snapshot.karts == null || snapshot.karts.Length != racers.Length) return;
            networkTarget = snapshot; networkSnapshotAt = Time.realtimeSinceStartup;
            elapsed = snapshot.elapsed; countdown = snapshot.countdown;
            phase = snapshot.phase == "Countdown" ? Phase.Countdown : snapshot.karts.All(k => k.finished) ? Phase.Finished : Phase.Racing;
            if(phase!=Phase.Countdown)
            {
                NetworkStartSourceRace(Time.timeSinceLevelLoadAsDouble-snapshot.elapsed);
                if(phase==Phase.Finished)
                {
                    if(snapshot.sourceClockAvailable)raceClock.StopAtElapsed(snapshot.sourceClockElapsed);
                    else if(raceClock.Running)raceClock.StopAtElapsed(snapshot.elapsed);
                }
                else raceClock.StartAt(Time.timeSinceLevelLoadAsDouble-snapshot.elapsed);
            }
            foreach (var state in snapshot.karts)
            {
                if (state.seat < 0 || state.seat >= racers.Length) continue;
                var motor = racers[state.seat];
                NetworkConsumeSourceAudio(state,motor);
                motor.GetComponent<NativeKartRecovery>().ApplyNetworkRecovery(state.recoveryRequests);
                if (state.seat == Net.LocalRacerIndex || Vector3.Distance(motor.transform.position, state.position) > 10f)
                    motor.ApplyNetworkPose(state.position, state.rotation, state.velocity, state.speed, state.distance, state.seat != Net.LocalRacerIndex);
                motor.driving = !Net.requestedSpectator && state.seat == Net.LocalRacerIndex && phase == Phase.Racing && !state.finished;
                motor.GetComponent<NativeKartItemInventory>().ApplyNetworkInventory(state.inventory, state.frozen);
                motor.GetComponent<NativeKartHit>().ApplyNetworkHit(state.frozen, state.invulnerable, state.damageVisible, state.damage);
                motor.stats._topSpeed = state.topSpeed; motor.stats._topSpeedAdd = state.topSpeedAdd; motor.stats._acceleration = state.acceleration; motor.stats._topSpeedGrass = state.grassSpeed;
                if (state.reversed && !motor.ControlsReversed) motor.AddControlReverse(); else if (!state.reversed) motor.ClearControlReverse();
                motor.GetComponent<NativeKartItemEffects>().ApplyNetworkEffects(state.effectIds, GetComponent<NativeItemCatalog>());
                if (!Net.requestedSpectator && state.seat == Net.LocalRacerIndex && state.finished && FinishCamera && !FinishCamera.Ended) FinishCamera.OnEndedRace();
                finishTimes[state.seat] = state.finish; finishSeconds[state.seat] = state.finish; finishPlaces[state.seat] = state.place;
                if (state.seat != Net.LocalRacerIndex) motor.GetComponent<NativeKartOriginalAI>().active = false;
            }
            NetworkTickSourceRace(snapshot);
            ApplyNetworkActors(snapshot.actors ?? new NativeNetworkActor[0]);
        }
        void ApplyNetworkActors(NativeNetworkActor[] actors)
        {
            var active = new HashSet<int>(actors.Select(a => a.id));
            foreach (int id in networkActors.Keys.Where(id => !active.Contains(id)).ToArray()) { if (networkActors[id]) Destroy(networkActors[id]); networkActors.Remove(id); }
            foreach (var actor in actors)
            {
                GameObject visual;
                if (!networkActors.TryGetValue(actor.id, out visual) || !visual)
                {
                    var entry = GetComponent<NativeItemCatalog>().Find(actor.item);
                    if (entry == null) continue;
                    GameObject prefab = NetworkPrefab(entry, actor.visual);
                    if (!prefab) continue;
                    visual = Instantiate(prefab); visual.name = "Network visual " + actor.item;
                    foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
                    foreach (var collider in visual.GetComponentsInChildren<Collider>()) collider.enabled = false;
                    foreach (var controller in visual.GetComponentsInChildren<CharacterController>()) controller.enabled = false;
                    networkActors[actor.id] = visual;
                }
                visual.transform.SetPositionAndRotation(actor.position, actor.rotation);
            }
        }
        internal void NetworkGive(string id)
        {
            for (int i = 0; i < racers.Length; i++)
            {
                var inventory = racers[i].GetComponent<NativeKartItemInventory>();
                inventory.TryGive(id, false); inventory.RequestUse();
            }
        }
    }
}
