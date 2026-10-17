using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class WinterAudioRef
        {
            public string clipGUID, clipName, mixer;
            public float pitch, volume, spatialBlend, minDistance, maxDistance, doppler;
            public int priority, rolloff;
            public bool playOnAwake, loop;
        }
        [Serializable] sealed class WinterAwakeAudioRef
        {
            public string sourceId, path;
            public WinterAudioRef audio;
        }
        [Serializable] sealed class WinterCallRef
        {
            public string eventName, targetPath, targetType, method, sourceTargetId;
            public int order, mode, state;
            public bool argument;
            public WinterAudioRef audio;
        }
        [Serializable] sealed class WinterSnowmanRef
        {
            public string sourceId, path;
            public float speedRequired, respawnTime;
            public WinterCallRef[] calls;
        }
        [Serializable] sealed class WinterColorRef
        {
            public string identity, path;
            public Color color, color2, dirtColor;
            public float heat, dirt, wheelDirt, wheelHeat, dirtColorEnabled;
            public string[] targets;
        }
        [Serializable] sealed class WinterSource
        {
            public WinterSnowmanRef[] snowmen;
            public WinterAwakeAudioRef[] awakeDeathAudio;
            public WinterColorRef[] colors;
        }
        [Serializable] sealed class WinterCheck
        {
            public string id, evidenceKind, detail;
            public bool passed;
        }
        [Serializable] sealed class WinterAudioSample
        {
            public string id, path, clip, mixer;
            public bool enabled, active, playing, awake, emitterAwake, muted;
            public float pitch, volume, spatialBlend, listenerVolume, sourcePeak, listenerPeak;
            public NativeAudioObservation.Snapshot audioStart, audioEnd;
        }
        [Serializable] sealed class WinterTimelineSample
        {
            public string stage, evidenceKind;
            public float elapsed, global, realtime, gameTime, fixedDeltaTime;
            public bool enabled;
            public int started, finished;
        }
        [Serializable] sealed class WinterColorActivationSample
        {
            public string path;
            public bool activeSelfBefore, activeSelfAfter, activeSelfRestored;
            public int activationFrame, observationFrame, restorationFrame;
        }
        [Serializable] sealed class WinterRuntimeReport
        {
            public bool passed, accepted, ordinaryAudibleAccepted, originalMatchedVisualAccepted, fullGameplayAccepted;
            public string error, baselineSHA256, assemblySHA256, outputMode, fixtureType;
            public bool nonrendered, pendingrender;
            public NativeAudioObservation.Snapshot audioStart, audioEnd;
            public string[] commandLine;
            public string splashRawClipName = "Formal Invitation", splashImportedClipName = "Formal Invitation to the Race (Splash)";
            public float realtimeSeconds;
            public List<WinterCheck> checks = new List<WinterCheck>();
            public List<WinterAudioSample> audio = new List<WinterAudioSample>();
            public List<WinterTimelineSample> timeline = new List<WinterTimelineSample>();
            public List<WinterColorActivationSample> colorActivation = new List<WinterColorActivationSample>();
            public List<string> pending = new List<string>();
        }
        WinterRuntimeReport winterAudit;
        readonly List<Tuple<UnityEvent, UnityAction>> winterListeners = new List<Tuple<UnityEvent, UnityAction>>();
        static readonly BindingFlags WinterPrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        const string WinterBaselineHash = "cb57ed4597130002113e9f3b829f160ccda4f635a8f7141e18878a8a2b8df344";

        void WinterRequire(bool passed, string id, string kind, string detail = "")
        {
            winterAudit.checks.Add(new WinterCheck { id = id, passed = passed, evidenceKind = kind, detail = detail });
            Debug.Log("WINTER_RUNTIME " + (passed ? "PASS " : "FAIL ") + id + " " + kind);
        }
        static bool WinterNear(float a, float b, float tolerance = .00002f) => Mathf.Abs(a - b) <= tolerance;
        static bool WinterColorNear(Color a, Color b) => WinterNear(a.r, b.r) && WinterNear(a.g, b.g) && WinterNear(a.b, b.b) && WinterNear(a.a, b.a);
        static string WinterPath(Transform node)
        {
            string result = node.name;
            while (node.parent) { node = node.parent; result = node.name + "/" + result; }
            return result;
        }
        Transform WinterFind(string path)
        {
            var nodes = gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => WinterPath(t) == path).ToArray();
            if (nodes.Length != 1) throw new InvalidOperationException("Unique source hierarchy binding absent: " + path + " matches=" + nodes.Length);
            return nodes[0];
        }
        static string WinterHash(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        static object WinterField(object obj, string name)
        {
            for (Type type = obj.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, WinterPrivate); if (field != null) return field.GetValue(obj);
            }
            throw new MissingFieldException(obj.GetType().FullName, name);
        }
        static object WinterPersistentCall(UnityEvent value, int order)
        {
            var group = WinterField(value, "m_PersistentCalls");
            return ((IList)WinterField(group, "m_Calls"))[order];
        }
        static UnityEngine.Object WinterTarget(Transform node, string type)
        {
            if (type == "GameObject") return node.gameObject;
            if (type == "AudioSource") return node.GetComponent<AudioSource>();
            if (type == "ParticleSystem") return node.GetComponent<ParticleSystem>();
            throw new InvalidOperationException(type);
        }
        void WinterObserveEnd(UnityEvent value, UnityAction callback)
        {
            value.AddListener(callback); winterListeners.Add(Tuple.Create(value, callback));
        }
        sealed class WinterFinishObservation : IDisposable
        {
            readonly NativeRace race;
            readonly NativeRaceStateFlow state;
            int finishCount, cleanupCount;
            bool finishBeforeCleanup, zeroAtFinish, firstCleanupAfterFinish, zeroAtCleanup;
            public WinterFinishObservation(NativeRace race)
            {
                this.race = race; state = race.SourceRaceState;
                race.SourceRaceFinishObserved += OnFinish;
                state.HudCleanup += OnCleanup;
            }
            void OnFinish()
            {
                finishCount++;
                finishBeforeCleanup = cleanupCount == 0 && state.InRace;
                zeroAtFinish = Shader.GetGlobalFloat("_MeltingFactor") == 0;
            }
            void OnCleanup()
            {
                cleanupCount++;
                if (cleanupCount != 1) return;
                firstCleanupAfterFinish = finishCount == 1;
                zeroAtCleanup = Shader.GetGlobalFloat("_MeltingFactor") == 0;
            }
            public bool Passed => finishCount == 1 && cleanupCount > 0 && finishBeforeCleanup
                && zeroAtFinish && firstCleanupAfterFinish && zeroAtCleanup;
            public string Detail => "finish=" + finishCount + " HudCleanup=" + cleanupCount
                + " finishBeforeFirstHudCleanup=" + finishBeforeCleanup + " zeroAtFinish=" + zeroAtFinish
                + " firstHudCleanupAfterFinish=" + firstCleanupAfterFinish + " zeroAtHudCleanup=" + zeroAtCleanup;
            public void Dispose()
            {
                race.SourceRaceFinishObserved -= OnFinish;
                state.HudCleanup -= OnCleanup;
            }
        }
        static bool winterAuditRunning;
        internal IEnumerator CheckWinterRuntimeLive()
        {
            if (winterAuditRunning) yield break;
            winterAuditRunning = true;
            var root = new GameObject("Persistent Winter verification");
            DontDestroyOnLoad(root);
            root.AddComponent<NativeWinterAuditHost>().StartCoroutine(CheckWinterRuntimeManaged());
            yield break;
        }
        IEnumerator CheckWinterRuntimeManaged()
        {
            bool nonrendered = Environment.GetCommandLineArgs().Contains("--winter-nonrendered-diagnostic");
            string outputMode = Environment.GetCommandLineArgs().Contains("--winter-ordinary-audio-output")
                ? "winter-runtime-output-requested" : "winter-runtime-listener-isolated";
            winterAudit = new WinterRuntimeReport { commandLine = Environment.GetCommandLineArgs(), outputMode = outputMode,
                audioStart = NativeAudioObservation.Capture(outputMode),
                fixtureType = nonrendered ? "winter-nonrendered-diagnostic" : "winter-runtime-audit",
                nonrendered = nonrendered, pendingrender = nonrendered };
            if (nonrendered)
                winterAudit.pending.Add("Nonrendered diagnostic replaces this checker end-of-frame rendering wait with a next-frame yield; actual rendered pixels, original matched visual states, ordinary hearing and natural collision remain pending");
            float started = Time.realtimeSinceStartup;
            float oldListener = AudioListener.volume, oldScale = Time.timeScale;
            bool oldUiPlayback = NativeAudioRuntime.UiVerificationPlayback;
            bool oldListenerSilenced = NativeAudioRuntime.VerificationListenerSilenced;
            var stack = new Stack<IEnumerator>(); stack.Push(CheckWinterRuntimeCore());
            try
            {
                while (stack.Count > 0)
                {
                    bool next; object value;
                    try { next = stack.Peek().MoveNext(); value = next ? stack.Peek().Current : null; }
                    catch (Exception error) { winterAudit.error = error.ToString(); break; }
                    if (!next) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (value is IEnumerator child && !(value is CustomYieldInstruction)) { stack.Push(child); continue; }
                    yield return value;
                }
            }
            finally
            {
                foreach (var pending in stack) (pending as IDisposable)?.Dispose();
                foreach (var listener in winterListeners) listener.Item1.RemoveListener(listener.Item2);
                winterListeners.Clear();
                winterAudit.audioEnd = NativeAudioObservation.Capture(winterAudit.outputMode);
                NativeAudioRuntime.VerificationListenerSilenced = oldListenerSilenced;
                AudioListener.volume = oldListener; Time.timeScale = oldScale; NativeAudioRuntime.UiVerificationPlayback = oldUiPlayback;
                winterAudit.realtimeSeconds = Time.realtimeSinceStartup - started;
                winterAudit.passed = string.IsNullOrEmpty(winterAudit.error) && winterAudit.checks.Count > 0 && winterAudit.checks.All(c => c.passed);
                Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(Path.Combine(reportDirectory, "winter-runtime-report.json"), JsonUtility.ToJson(winterAudit, true));
            }
            Debug.Log("WINTER_RUNTIME_RESULT passed=" + winterAudit.passed + " accepted=false pending=" + winterAudit.pending.Count);
            Application.Quit(winterAudit.passed ? 0 : 2);
        }
        IEnumerator CheckWinterRuntimeCore()
        {
            string[] args = Environment.GetCommandLineArgs();
            Time.timeScale = 1;
            NativeAudioRuntime.VerificationListenerSilenced = !args.Contains("--winter-ordinary-audio-output");
            AudioListener.volume = args.Contains("--winter-ordinary-audio-output") ? 1 : 0;
            NativeAudioRuntime.UiVerificationPlayback = true;
            hud.SuppressPointerInput = true;
            WinterRequire(trackId == "wintermistylake", "winter.scene", "field", trackId);
            if (trackId != "wintermistylake") throw new InvalidOperationException("Run with --track wintermistylake");
            var baseline = NativeContent.Load<TextAsset>("NativeVerification/WinterRuntimeSource");
            if (!baseline || WinterHash(baseline.bytes) != WinterBaselineHash) throw new InvalidOperationException("Independent Winter source baseline missing or changed");
            winterAudit.baselineSHA256 = WinterBaselineHash;
            winterAudit.assemblySHA256 = WinterHash(File.ReadAllBytes(typeof(NativeRace).Assembly.Location));
            var source = JsonUtility.FromJson<WinterSource>(baseline.text);
            WinterRequire(source.snowmen.Length == 4 && source.snowmen.Sum(s => s.calls.Length) == 24 && source.colors.Length == 14 && source.colors.Sum(c => c.targets.Length) == 28
                && source.awakeDeathAudio != null && source.awakeDeathAudio.Length == 2,
                "winter.source-denominator", "source-field");
            WinterRequire(!NativeAudioRuntime.Muted, "audio.production-playback-mode", "field", "listenerVolume=" + AudioListener.volume);
            yield return CheckWinterSplash();
            // Observe the settings path after startup; disabled callback sources are a real failure.
            NativeUserSettings.ApplyScene(this);
            WinterRequire(args.Contains("--winter-ordinary-audio-output") || AudioListener.volume == 0,
                "audio.default-listener-remains-silent-after-settings", "listener-isolation-field");
            yield return null;
            if (winterAudit.nonrendered) yield return null;
            else yield return new WaitForEndOfFrame();
            yield return NativeSceneSettingsAudioVerification.RepeatApply(this,(passed,id,detail)=>WinterRequire(passed,id,"real-settings-scheduling",detail));
            yield return CheckWinterModeSounds();
            yield return CheckWinterStaticColors(source);
            yield return CheckWinterSnowmen(source);
            yield return CheckWinterLifecycle();
            CheckWinterInjectedTimeline();
            if (args.Contains("--winter-natural-timeline"))
            {
                winterAudit.pending.Add("Fresh-Awake full30plus160second timeline remains pending; this flag observes the current instance's remaining real-clock interval");
                yield return CheckWinterNaturalTimeline();
            }
            else winterAudit.pending.Add("Natural190second timeline observation requested with --winter-natural-timeline");
            if (args.Contains("--winter-natural-race-lifecycle")) yield return CheckWinterNaturalFinish();
            else winterAudit.pending.Add("Natural local race finish and finish-to-result lifecycle requested with --winter-natural-race-lifecycle");
            if (args.Contains("--winter-restart-lifecycle")) yield return CheckWinterProductionRestart();
            else winterAudit.pending.Add("Production scene-loading Restart requested with --winter-restart-lifecycle");
            winterAudit.pending.Add("Original matched visual states and ordinary listening acceptance require reviewer evidence");
            winterAudit.pending.Add("Persistent24 callback serialized order and end effects observed; per-original-call dynamic invocation trace remains open");
            winterAudit.pending.Add("Natural CharacterController collision and item TriggerHurt contact require separate real-physics observation");
            winterAudit.pending.Add("Production GP final scoreboard -> RaceFinish -> HudCleanup -> ceremony publisher retirement, next-real-fixed-tick global0 and ceremony-end Finish require a dedicated transition receipt; default/natural-TT/Restart paths do not execute this route");
            winterAudit.pending.Add("ApplicationQuit callback reflection probe is injected; real application shutdown reset requires shutdown evidence");
            winterAudit.pending.Add("CharacterCancel source producer is disabled original OnlineButton confirm6; enabled Native runtime action remains outside this slice");
        }
        IEnumerator CheckWinterStaticColors(WinterSource source)
        {
            var actual = gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NativeStaticKartColor>(true)).ToArray();
            WinterRequire(actual.Length == 14 && actual.Sum(c => c.targets.Length) == 28, "winter.color14-renderer28", "field");
            var tontos = WinterFind("Tontos").gameObject;
            bool originalActive = tontos.activeSelf;
            WinterRequire(!originalActive, "color.fixture-source-tontos-inactive", "source-inactive-field");
            var recordedMaterials = new Dictionary<Renderer, Material[]>();
            var ancestorStates = new Dictionary<GameObject, bool>();
            var boundNodes = actual.Select(c => c.transform).Concat(actual.SelectMany(c => c.targets).Where(r => r).Select(r => r.transform));
            foreach (var node in boundNodes)
            {
                if (node != tontos.transform && !node.IsChildOf(tontos.transform))
                    throw new InvalidOperationException("Static color fixture binding outside Tontos: " + WinterPath(node));
                for (var ancestor = node; ancestor; ancestor = ancestor.parent)
                {
                    ancestorStates[ancestor.gameObject] = ancestor.gameObject.activeSelf;
                    if (ancestor == tontos.transform) break;
                }
            }
            var orderedAncestors = ancestorStates.Keys.OrderBy(g => WinterPath(g.transform).Count(ch => ch == '/')).ThenBy(g => WinterPath(g.transform)).ToArray();
            var inactiveAncestors = orderedAncestors.Where(g => !ancestorStates[g]).ToArray();
            WinterRequire(inactiveAncestors.Select(g => WinterPath(g.transform)).OrderBy(path => path)
                .SequenceEqual(new[] { "Tontos", "Tontos/Racers (1)", "Tontos/Racers/Reisen" }.OrderBy(path => path)),
                "color.fixture3-source-inactive-ancestors", "source-inactive-field");
            var activationSamples = new Dictionary<GameObject, WinterColorActivationSample>();
            int activationFrame = Time.frameCount;
            try
            {
                foreach (var ancestor in inactiveAncestors)
                {
                    var sample = new WinterColorActivationSample { path = WinterPath(ancestor.transform),
                        activeSelfBefore = ancestorStates[ancestor], activationFrame = activationFrame };
                    activationSamples.Add(ancestor, sample);
                    winterAudit.colorActivation.Add(sample);
                    ancestor.SetActive(true);
                    sample.activeSelfAfter = ancestor.activeSelf;
                }
                yield return null;
                foreach (var sample in activationSamples.Values) sample.observationFrame = Time.frameCount;
                WinterRequire(Time.frameCount > activationFrame, "color.fixture-next-real-frame", "injected-activation-real-Update");
                WinterRequire(actual.All(c => c.isActiveAndEnabled), "color.fixture14-active-updaters", "injected-activation-field");
                foreach (var row in source.colors)
                {
                    var node = WinterFind(row.path); var updater = node.GetComponent<NativeStaticKartColor>();
                    WinterRequire(updater && updater.sourceIdentity == row.identity && updater.targets.Length == row.targets.Length, "color.identity." + row.identity, "field");
                    if (!updater) continue;
                    WinterRequire(WinterColorNear(updater.color, row.color) && WinterColorNear(updater.color2, row.color2) && WinterColorNear(updater.dirtColor, row.dirtColor)
                        && WinterNear(updater.heat,row.heat) && WinterNear(updater.dirt,row.dirt) && WinterNear(updater.wheelDirt,row.wheelDirt)
                        && WinterNear(updater.wheelHeat,row.wheelHeat) && WinterNear(updater.dirtColorEnabled,row.dirtColorEnabled), "color.fields." + row.identity, "field");
                    for (int i = 0; i < row.targets.Length; i++)
                    {
                        var target = updater.targets[i];
                        WinterRequire(target && target.transform == WinterFind(row.targets[i]), "color.target." + row.identity + "." + i, "field");
                        if (!target) continue;
                        recordedMaterials[target] = target.sharedMaterials; var block = new MaterialPropertyBlock(); target.GetPropertyBlock(block);
                        WinterRequire(WinterColorNear(block.GetColor("_Color"),row.color) && WinterColorNear(block.GetColor("_Color2"),row.color2)
                            && WinterColorNear(block.GetColor("_DirtColor"),row.dirtColor) && WinterNear(block.GetFloat("_SpeedIntensity"), row.heat * 5)
                            && WinterNear(block.GetFloat("_DirtAmount"),row.dirt) && WinterNear(block.GetFloat("_WheelDirtAmount"),row.wheelDirt)
                            && WinterNear(block.GetFloat("_WheelHeatAmount"),row.wheelHeat) && WinterNear(block.GetFloat("_DirtColorEnabled"),row.dirtColorEnabled),
                            "color.actual-property-block." + row.identity + "." + i, "injected-activation-real-Update");
                    }
                }
                foreach (var row in source.colors)
                {
                    var updater = WinterFind(row.path).GetComponent<NativeStaticKartColor>();
                    if (!updater) continue;
                    updater.Apply();
                    for (int i = 0; i < row.targets.Length; i++)
                    {
                        var target = updater.targets[i];
                        if (!target) continue;
                        var materials = recordedMaterials[target];
                        WinterRequire(materials.SequenceEqual(target.sharedMaterials), "color.shared-material-stable." + row.identity + "." + i, "injected-method");
                    }
                }
            }
            finally
            {
                foreach (var ancestor in orderedAncestors.Reverse())
                {
                    if (ancestor.activeSelf != ancestorStates[ancestor]) ancestor.SetActive(ancestorStates[ancestor]);
                    if (activationSamples.TryGetValue(ancestor, out var sample))
                    {
                        sample.activeSelfRestored = ancestor.activeSelf;
                        sample.restorationFrame = Time.frameCount;
                    }
                }
            }
            WinterRequire(tontos.activeSelf == originalActive, "color.fixture-tontos-active-restored", "restored-source-field");
            WinterRequire(ancestorStates.All(state => state.Key.activeSelf == state.Value), "color.fixture-all-ancestor-states-restored", "restored-source-field");
        }
        IEnumerator CheckWinterSnowmen(WinterSource source)
        {
            var snowmen = new List<NativeSnowmanDestructable>();
            var refs = new Dictionary<NativeSnowmanDestructable, WinterSnowmanRef>();
            var postDestruction = new Dictionary<NativeSnowmanDestructable,int>();
            var postRespawn = new Dictionary<NativeSnowmanDestructable,int>();
            foreach (var row in source.snowmen)
            {
                var snowman = WinterFind(row.path).GetComponent<NativeSnowmanDestructable>();
                WinterRequire(snowman && snowman.sourceComponentId.Contains("-" + row.sourceId + "-") && snowman.IsAlive && WinterNear(snowman.speedRequired,row.speedRequired) && WinterNear(snowman.respawnTime,row.respawnTime), "snowman.identity." + row.sourceId,"field");
                if (!snowman) continue;
                snowmen.Add(snowman); refs[snowman]=row; postDestruction[snowman]=0; postRespawn[snowman]=0;
                var awakeDeath = source.awakeDeathAudio.SingleOrDefault(a => a.path.StartsWith(row.path + "/", StringComparison.Ordinal));
                if (awakeDeath != null)
                {
                    var audio = WinterFind(awakeDeath.path).GetComponent<AudioSource>();
                    WinterRequire(audio && !audio.gameObject.activeInHierarchy && !audio.isPlaying,
                        "audio.awake-death-dormant-before-damage." + awakeDeath.sourceId, "real-pre-damage-state");
                }
                foreach (string name in new[] { "onDestructed", "onRespawn" })
                {
                    var value = name == "onDestructed" ? snowman.onDestructed : snowman.onRespawn;
                    var rows = row.calls.Where(c=>c.eventName==name).OrderBy(c=>c.order).ToArray();
                    WinterRequire(value.GetPersistentEventCount()==rows.Length,"snowman.callback-count."+row.sourceId+"."+name,"field");
                    foreach (var call in rows)
                    {
                        if (call.order>=value.GetPersistentEventCount()) continue;
                        var expected=WinterTarget(WinterFind(call.targetPath),call.targetType);
                        var persistent=WinterPersistentCall(value,call.order);
                        bool matches=value.GetPersistentTarget(call.order)==expected && value.GetPersistentMethodName(call.order)==call.method
                            && (int)value.GetPersistentListenerState(call.order)==call.state && Convert.ToInt32(WinterField(persistent,"m_Mode"))==call.mode
                            && (bool)WinterField(WinterField(persistent,"m_Arguments"),"m_BoolArgument")==call.argument;
                        WinterRequire(matches,"snowman.callback."+row.sourceId+"."+name+"."+call.order,"ordered-serialized-field",call.targetPath);
                        if (expected is AudioSource audio) CheckWinterAudioFields(audio,call.audio,"snowman."+row.sourceId+"."+name);
                    }
                }
                WinterObserveEnd(snowman.onDestructed,()=>{postDestruction[snowman]++;WinterRequire(!snowman.IsAlive,"snowman.dead-before-event."+row.sourceId,"injected-damage-real-UnityEvent");CheckWinterEventEffects(row,"onDestructed");if(postDestruction[snowman]==1 && awakeDeath!=null)CheckSnowAwake(awakeDeath);});
                WinterObserveEnd(snowman.onRespawn,()=>{postRespawn[snowman]++;WinterRequire(snowman.IsAlive,"snowman.alive-before-event."+row.sourceId,"real-Invoke-callback");CheckWinterEventEffects(row,"onRespawn");});
            }
            WinterRequire(snowmen.Count==4,"snowman.production-instance4","field");
            var deathBefore=snowmen.ToDictionary(s=>s,s=>s.DestructionCount); var respawnBefore=snowmen.ToDictionary(s=>s,s=>s.RespawnCount);
            foreach(var snowman in snowmen) WinterRequire(snowman.TakeDamage(19f)==0,"snowman.float-damage-zero."+refs[snowman].sourceId,"injected-damage");
            yield return new WaitForSeconds(2f);
            foreach(var snowman in snowmen) WinterRequire(snowman.TakeDamage()==0 && !snowman.IsAlive,"snowman.repeat-damage."+refs[snowman].sourceId,"injected-damage");
            float repeatedAt=Time.time, repeatedReal=Time.realtimeSinceStartup;
            while(Time.time<repeatedAt+9.8f)yield return null;
            foreach(var snowman in snowmen) WinterRequire(!snowman.IsAlive && snowman.RespawnCount==respawnBefore[snowman],"snowman.old-deadline-cancelled."+refs[snowman].sourceId,"real-scaled-clock");
            while(Time.time<repeatedAt+10.2f)yield return null;
            foreach(var snowman in snowmen)WinterRequire(snowman.IsAlive && snowman.RespawnCount==respawnBefore[snowman]+1 && snowman.DestructionCount==deathBefore[snowman]+2
                && postDestruction[snowman]==2 && postRespawn[snowman]==1,"snowman.single-repeated-respawn."+refs[snowman].sourceId,"real-scaled-clock");
            WinterRequire(Time.realtimeSinceStartup-repeatedReal>=9.9f,"snowman.real-ten-second-observation","real-clock");
        }
        void CheckWinterEventEffects(WinterSnowmanRef row,string eventName)
        {
            foreach(var call in row.calls.Where(c=>c.eventName==eventName))
            {
                var target=WinterTarget(WinterFind(call.targetPath),call.targetType);
                if(target is GameObject node)WinterRequire(node.activeSelf==call.argument,"snowman.effect."+row.sourceId+"."+eventName+"."+call.order,"actual-UnityEvent-end-state");
                if(target is AudioSource audio){WinterRequire(audio.enabled && audio.gameObject.activeInHierarchy && audio.isPlaying,"snowman.audio-trigger."+row.sourceId+"."+eventName,"actual-source-Play-state");CheckWinterAudioFields(audio,call.audio,"snowman-callback."+row.sourceId+"."+eventName);}
                if(target is ParticleSystem particles)WinterRequire(particles.isPlaying,"snowman.particle."+row.sourceId+"."+eventName,"actual-UnityEvent-end-state");
            }
        }
        void CheckSnowAwake(WinterAwakeAudioRef original)
        {
            var audio = WinterFind(original.path).GetComponent<AudioSource>();
            WinterRequire(audio && audio.enabled && audio.gameObject.activeInHierarchy && audio.isPlaying,
                "audio.awake-death-onenable." + original.sourceId, "actual-FX-activation-source-Play-state");
            if (audio) CheckWinterAudioFields(audio, original.audio, "awake-death." + original.sourceId, true);
        }
        void CheckWinterAudioFields(AudioSource audio, WinterAudioRef expected, string id, bool emitterOwnsAwake = false)
        {
            var emitters=audio.GetComponents<NativeAudioEmitter>(); int bindings=0;bool originalAwake=false;
            foreach(var emitter in emitters)for(int i=0;i<(emitter.sources?.Length??0);i++)if(emitter.sources[i]==audio){bindings++;originalAwake|=emitter.originalPlayOnAwake!=null && i<emitter.originalPlayOnAwake.Length && emitter.originalPlayOnAwake[i];}
            bool valid=(!audio.gameObject.activeInHierarchy || audio.enabled) && audio.clip && audio.clip.name==expected.clipName && audio.outputAudioMixerGroup && audio.outputAudioMixerGroup.name==expected.mixer
                && audio.playOnAwake==(emitterOwnsAwake ? false : expected.playOnAwake) && bindings==1 && originalAwake==expected.playOnAwake && audio.loop==expected.loop
                && WinterNear(audio.pitch,expected.pitch) && WinterNear(audio.volume,expected.volume) && WinterNear(audio.spatialBlend,expected.spatialBlend)
                && WinterNear(audio.minDistance,expected.minDistance) && WinterNear(audio.maxDistance,expected.maxDistance) && WinterNear(audio.dopplerLevel,expected.doppler)
                && audio.priority==expected.priority && (int)audio.rolloffMode==expected.rolloff;
            WinterRequire(valid,"audio.actual-source-and-emitter."+id,"field",WinterPath(audio.transform));
            winterAudit.audio.Add(new WinterAudioSample{id=id,path=WinterPath(audio.transform),clip=audio.clip?audio.clip.name:"",mixer=audio.outputAudioMixerGroup?audio.outputAudioMixerGroup.name:"",enabled=audio.enabled,active=audio.gameObject.activeInHierarchy,playing=audio.isPlaying,awake=audio.playOnAwake,emitterAwake=originalAwake,pitch=audio.pitch,volume=audio.volume,spatialBlend=audio.spatialBlend,listenerVolume=AudioListener.volume,muted=NativeAudioRuntime.Muted,audioStart=NativeAudioObservation.Capture(winterAudit.outputMode)});
        }
        NativeShaderGlobalsOverTime WinterTimeline()
        {
            var timelines=gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<NativeShaderGlobalsOverTime>(true)).ToArray();
            if(timelines.Length!=1)throw new InvalidOperationException("Original Winter timeline denominator1 missing");
            return timelines[0];
        }
        void WinterRecordTimeline(NativeShaderGlobalsOverTime timeline,string stage,string kind)
        {
            winterAudit.timeline.Add(new WinterTimelineSample{stage=stage,evidenceKind=kind,elapsed=timeline.Elapsed,global=Shader.GetGlobalFloat("_MeltingFactor"),enabled=timeline.enabled,realtime=Time.realtimeSinceStartup,gameTime=Time.time,fixedDeltaTime=Time.fixedDeltaTime,started=OriginalRaceStartedCount,finished=OriginalRaceFinishedCount});
        }
        IEnumerator CheckWinterLifecycle()
        {
            var timeline=WinterTimeline();var manager=GetComponent<NativeShaderGlobalsManager>();
            WinterRequire(manager && timeline.shaderKey=="_MeltingFactor" && timeline.delay==30 && timeline.targetTime==160,"melting.source-fields-and-manager","field");
            if(!manager)yield break;
            float age=timeline.Elapsed;bool enabledBefore=timeline.enabled;int started=OriginalRaceStartedCount,finished=OriginalRaceFinishedCount;
            int resetsBeforeBegin=manager.ResetCount;
            Shader.SetGlobalFloat("_MeltingFactor",.63f);Begin();
            WinterRequire(OriginalRaceStartedCount==started+1 && manager.ResetCount==resetsBeforeBegin+1 && OriginalCircuitPrepared && manager.LastResetReason=="RaceStarted" && Shader.GetGlobalFloat("_MeltingFactor")==0
                && timeline.Elapsed==age && timeline.enabled==enabledBefore,"melting.injected-Begin-start-reset-only-global","injected-setup-production-event");
            WinterRecordTimeline(timeline,"Begin","injected-setup-production-event");
            float timeout=Time.realtimeSinceStartup+5;
            while(phase==Phase.Countdown && Time.realtimeSinceStartup<timeout)yield return null;
            WinterRequire(phase==Phase.Racing && OriginalRaceStartedCount==started+1,"melting.countdown-no-second-raceStarted","real-countdown");
            age=timeline.Elapsed;int resets=manager.ResetCount;
            Pause();float pauseAge=timeline.Elapsed;
            yield return new WaitForSecondsRealtime(.25f);
            WinterRequire(timeline.Elapsed==pauseAge && manager.ResetCount==resets,"melting.pause-holds-fixed-scheduler","real-pause-clock");
            Resume();
            bool resumed = phase == Phase.Racing && Time.timeScale == 1;
            WinterRequire(resumed,"melting.resume-restores-race-clock","production-resume-clock");
            if (!resumed) throw new InvalidOperationException("Winter race clock did not resume.");
            yield return new WaitForFixedUpdate();yield return null;
            WinterRequire(timeline.Elapsed>age || !timeline.enabled,"melting.resume-advances-fixed-scheduler","real-fixed-clock");
            age=timeline.Elapsed;enabledBefore=timeline.enabled;finished=OriginalRaceFinishedCount;started=OriginalRaceStartedCount;
            resetsBeforeBegin=manager.ResetCount;
            Shader.SetGlobalFloat("_MeltingFactor",.71f);Begin();
            WinterRequire(OriginalRaceFinishedCount==finished && OriginalRaceStartedCount==started+1 && manager.ResetCount==resetsBeforeBegin+1 && OriginalCircuitPrepared
                && Shader.GetGlobalFloat("_MeltingFactor")==0 && timeline.Elapsed==age && timeline.enabled==enabledBefore,"melting.injected-repeated-Begin-same-timeline","injected-setup-production-event");
            WinterRecordTimeline(timeline,"repeated-Begin","injected-setup-production-event");
            timeout=Time.realtimeSinceStartup+5;
            while(phase==Phase.Countdown && Time.realtimeSinceStartup<timeout)yield return null;
            WinterRequire(phase==Phase.Racing,"melting.Quit-enters-real-Racing","real-countdown");
            Pause();
            WinterRequire(phase==Phase.Paused && Time.timeScale==0,"melting.Quit-enters-source-Pause","production-lifecycle");
            finished=OriginalRaceFinishedCount;age=timeline.Elapsed;Shader.SetGlobalFloat("_MeltingFactor",.82f);
            int resetsBeforeQuit=manager.ResetCount;
            using(var observed=new WinterFinishObservation(this))
            {
                ExecuteMenuAction(NativeMenuAction.MainMenu);
                WinterRequire(observed.Passed && manager.ResetCount==resetsBeforeQuit+1,"melting.Quit-source-event-before-cleanup","production-StateFlow-and-HudCleanup-events",observed.Detail);
            }
            WinterRequire(OriginalRaceFinishedCount==finished+1 && !OriginalCircuitPrepared && manager.LastResetReason=="RaceFinish"
                && Shader.GetGlobalFloat("_MeltingFactor")==0 && timeline.Elapsed==age && timeline.enabled==enabledBefore,"melting.real-paused-Quit-finish-reset-only-global","production-lifecycle");
            WinterRecordTimeline(timeline,"paused-Quit","production-lifecycle");
            resets=manager.ResetCount;age=timeline.Elapsed;Shader.SetGlobalFloat("_MeltingFactor",.92f);
            typeof(NativeShaderGlobalsManager).GetMethod("OnApplicationQuit",WinterPrivate).Invoke(manager,null);
            WinterRequire(manager.ResetCount==resets+1 && manager.LastResetReason=="ApplicationQuit" && Shader.GetGlobalFloat("_MeltingFactor")==0 && timeline.Elapsed==age,"melting.quit-handler-injected","injected-lifecycle-handler");
        }
        void CheckWinterInjectedTimeline()
        {
            var timeline=WinterTimeline();var method=typeof(NativeShaderGlobalsOverTime).GetMethod("FixedUpdate",WinterPrivate);
            var field=typeof(NativeShaderGlobalsOverTime).GetField("elapsed",WinterPrivate);
            if(method==null || field==null)throw new InvalidOperationException("Original timeline private scheduler method/field changed");
            float oldAge=timeline.Elapsed,oldGlobal=Shader.GetGlobalFloat(timeline.shaderKey),oldFixed=Time.fixedDeltaTime;bool oldEnabled=timeline.enabled;
            try
            {
                timeline.enabled=false;Time.fixedDeltaTime=.02f;
                foreach(var pair in new[]{Tuple.Create(-.04f,0f),Tuple.Create(0f,.02f/160f),Tuple.Create(79.98f,.5f),Tuple.Create(159.99f,1f)})
                {
                    field.SetValue(timeline,pair.Item1);timeline.enabled=true;method.Invoke(timeline,null);
                    WinterRequire(WinterNear(Shader.GetGlobalFloat(timeline.shaderKey),pair.Item2,.00003f),"melting.injected-fixed-boundary."+pair.Item1,"injected-fixed-tick");
                    if(pair.Item2==1)WinterRequire(timeline.Elapsed==160 && !timeline.enabled,"melting.terminal-clamp-disable","injected-fixed-tick");
                    WinterRecordTimeline(timeline,"injected:"+pair.Item1,"injected-fixed-tick");
                }
                field.SetValue(timeline,-30f);timeline.enabled=true;
                for(int i=0;i<9502;i++)method.Invoke(timeline,null);
                WinterRequire(timeline.Elapsed==160 && !timeline.enabled && Shader.GetGlobalFloat(timeline.shaderKey)==1,"melting.30-plus160-serialized-formula","injected9502fixedticks");
                Shader.SetGlobalFloat(timeline.shaderKey,.2f);
                typeof(NativeShaderGlobalsManager).GetMethod("OnApplicationQuit",WinterPrivate).Invoke(GetComponent<NativeShaderGlobalsManager>(),null);
                WinterRequire(timeline.Elapsed==160 && !timeline.enabled && Shader.GetGlobalFloat(timeline.shaderKey)==0,"melting.reset-preserves-terminal-disabled-timeline","injected-global-reset");
            }
            finally{field.SetValue(timeline,oldAge);timeline.enabled=oldEnabled;Time.fixedDeltaTime=oldFixed;Shader.SetGlobalFloat(timeline.shaderKey,oldGlobal);}
        }
        IEnumerator CheckWinterNaturalTimeline()
        {
            var timeline=WinterTimeline();float beganReal=Time.realtimeSinceStartup;
            WinterRecordTimeline(timeline,"natural-start","real-fixed-clock");
            float lastAge=timeline.Elapsed;bool sampledDelay=false,sampledMiddle=false;int realAdvances=0;
            WinterRequire(timeline.enabled && timeline.Elapsed < 160,"melting.natural-start-has-remaining-scheduler","real-fixed-clock","startAge="+timeline.Elapsed);
            if (!timeline.enabled || timeline.Elapsed >= 160) yield break;
            while(timeline.enabled && Time.realtimeSinceStartup-beganReal<205)
            {
                yield return new WaitForFixedUpdate();yield return null;
                float current=timeline.Elapsed;
                WinterRequire(current>=lastAge,"melting.natural-monotonic","real-fixed-clock");
                if(current>lastAge)realAdvances++;
                if(!sampledDelay && lastAge<0 && current>=0){sampledDelay=true;WinterRecordTimeline(timeline,"natural-delay-boundary","real-fixed-clock");}
                if(!sampledMiddle && lastAge<80 && current>=80){sampledMiddle=true;WinterRecordTimeline(timeline,"natural-midpoint","real-fixed-clock");}
                lastAge=current;
                if(Time.frameCount%600==0)Debug.Log("WINTER_NATURAL_TIMELINE elapsed="+current+" global="+Shader.GetGlobalFloat(timeline.shaderKey));
            }
            WinterRecordTimeline(timeline,"natural-terminal","real-fixed-clock");
            WinterRequire(realAdvances>0,"melting.natural-observed-real-advancement","real-fixed-clock","realAdvances="+realAdvances);
            WinterRequire(!timeline.enabled && timeline.Elapsed==160 && Shader.GetGlobalFloat(timeline.shaderKey)==1,"melting.natural-terminal-publisher","real-fixed-clock");
            int before=OriginalRaceStartedCount;Begin();
            WinterRequire(OriginalRaceStartedCount==before+1 && !timeline.enabled && timeline.Elapsed==160 && Shader.GetGlobalFloat(timeline.shaderKey)==0,"melting.injected-terminal-Begin-preserves-instance","injected-setup-production-event");
            yield return new WaitForSeconds(.1f);
            WinterRequire(Shader.GetGlobalFloat(timeline.shaderKey)==0,"melting.disabled-timeline-does-not-republish","real-fixed-clock");
            ResetRace(false);
        }
        IEnumerator CheckWinterNaturalFinish()
        {
            selectedMode=NativeSessionMode.TimeTrials;autopilot=true;Begin();
            var timeline=WinterTimeline();int finished=OriginalRaceFinishedCount;float deadline=Time.realtimeSinceStartup+420;
            while(phase!=Phase.Finished && Time.realtimeSinceStartup<deadline)yield return null;
            WinterRequire(phase==Phase.Finished,"winter.natural-local-finish","natural-assisted-driving");
            WinterRequire(OriginalRaceFinishedCount==finished && OriginalCircuitPrepared,"melting.local-finish-preserves-source-session","natural-assisted-driving");
            WinterRecordTimeline(timeline,"natural-local-finish","natural-assisted-driving");
            float resultDeadline=Time.realtimeSinceStartup+10;
            while(!SourceRaceState.ResultsActive && Time.realtimeSinceStartup<resultDeadline)yield return null;
            WinterRequire(SourceRaceState.ResultsActive && SourceRaceState.Humans[0].CardActive,"winter.natural-result-card-active","natural-results-lifecycle");
            var manager=GetComponent<NativeShaderGlobalsManager>();int resetsBeforeFinish=manager.ResetCount;
            using(var observed=new WinterFinishObservation(this))
            {
                SourceRaceState.OkStarted(SourceRaceState.Humans[0].InputUserIndex);
                WinterRequire(observed.Passed && manager.ResetCount==resetsBeforeFinish+1 && OriginalRaceFinishedCount==finished+1 && !OriginalCircuitPrepared,
                    "melting.result-submit-source-FinishGame-before-cleanup","injected-result-input-production-StateFlow-and-HudCleanup-events",observed.Detail);
            }
            autopilot=false;
        }
        IEnumerator CheckWinterProductionRestart()
        {
            selectedMode=NativeSessionMode.TimeTrials;selectedTrackId=trackId;Begin();
            float racingDeadline=Time.realtimeSinceStartup+5;
            while(phase==Phase.Countdown && Time.realtimeSinceStartup<racingDeadline)yield return null;
            WinterRequire(phase==Phase.Racing,"melting.production-Restart-enters-Racing","real-countdown");
            Pause();
            WinterRequire(phase==Phase.Paused && Time.timeScale==0,"melting.production-Restart-enters-source-Pause","production-lifecycle");
            var oldRace=this;var oldTimeline=WinterTimeline();int oldTimelineId=oldTimeline.GetInstanceID();
            int oldStarted=OriginalRaceStartedCount,oldFinished=OriginalRaceFinishedCount;
            bool loaded=false;float loadedAt=0;
            var oldManager=GetComponent<NativeShaderGlobalsManager>();int oldResets=oldManager.ResetCount;
            var observed=new WinterFinishObservation(this);
            UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene,UnityEngine.SceneManagement.LoadSceneMode> observe=(scene,mode)=>
            { if(scene.name==SceneForTrack("wintermistylake")){loaded=true;loadedAt=Time.realtimeSinceStartup;} };
            UnityEngine.SceneManagement.SceneManager.sceneLoaded+=observe;
            try
            {
                ExecuteMenuAction(NativeMenuAction.Restart);
                WinterRequire(observed.Passed && oldManager.ResetCount==oldResets+1 && OriginalRaceFinishedCount==oldFinished+1,"melting.production-Restart-source-Finish-before-cleanup","production-StateFlow-and-HudCleanup-events",observed.Detail);
                float deadline=Time.realtimeSinceStartup+20;NativeRace replacement=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    replacement=FindObjectsOfType<NativeRace>().SingleOrDefault(r=>r!=oldRace && r.trackId=="wintermistylake");
                    if(loaded && replacement && replacement.OriginalRaceStartedCount>0)break;
                    yield return null;
                }
                WinterRequire(loaded && replacement && !oldRace && !oldTimeline,"melting.production-Restart-Single-replaces-scene","real-sceneLoaded");
                if(!replacement)yield break;
                var timeline=replacement.WinterTimeline();
                WinterRequire(timeline.GetInstanceID()!=oldTimelineId && timeline.enabled && timeline.Elapsed>=-30 && timeline.Elapsed<=-28,
                    "melting.production-Restart-new-Awake-delay","real-sceneLoaded-real-fixed-clock","newAge="+timeline.Elapsed+" secondsSinceSceneLoaded="+(Time.realtimeSinceStartup-loadedAt));
                WinterRequire(replacement.OriginalRaceStartedCount==1 && replacement.OriginalCircuitPrepared && replacement.StartupSplash==null,
                    "melting.production-Restart-new-setup-once","production-lifecycle","oldStarted="+oldStarted);
                winterAudit.timeline.Add(new WinterTimelineSample{stage="production-Restart-new-scene",evidenceKind="real-sceneLoaded",elapsed=timeline.Elapsed,global=Shader.GetGlobalFloat("_MeltingFactor"),enabled=timeline.enabled,realtime=Time.realtimeSinceStartup,gameTime=Time.time,fixedDeltaTime=Time.fixedDeltaTime,started=replacement.OriginalRaceStartedCount,finished=replacement.OriginalRaceFinishedCount});
                yield return NativeSceneSettingsAudioVerification.AfterNormalBegin(replacement,(passed,id,detail)=>
                    WinterRequire(passed,id,id.Contains("continuity") || id.Contains("awake-probe") || id.Contains("unowned") || id.Contains("owned-enabled") ? "real-settings-scheduling" : "injected-damage-real-clock",detail));
            }
            finally{UnityEngine.SceneManagement.SceneManager.sceneLoaded-=observe;observed.Dispose();}
        }
        IEnumerator CheckWinterModeSounds()
        {
            int calls=0;AudioSource actualSource=null;AudioClip actualClip=null;
            Action<AudioSource,AudioClip> observe=(s,c)=>{calls++;actualSource=s;actualClip=c;};
            NativeAudioRuntime.UiOneShotPlayed+=observe;
            try
            {
                foreach(var action in new[]{NativeMenuAction.TimeTrials,NativeMenuAction.GrandPrix,NativeMenuAction.QuickRace,NativeMenuAction.CustomRace})
                {
                    var page=action==NativeMenuAction.TimeTrials?NativeMenuPage.Main:NativeMenuPage.RaceModes;
                    hud.SetMenuPage(page);
                    hud.SelectMenuIndex(action==NativeMenuAction.TimeTrials?1:action==NativeMenuAction.GrandPrix?0:action==NativeMenuAction.QuickRace?1:2);
                    while(hud.MenuTransitionActive)yield return null;
                    int before=calls;var result=hud.ConfirmMenu(page);ExecuteMenuAction(result);
                    WinterRequire(result==action && calls==before+1 && actualClip && actualClip.name=="GameModeSelect","audio.mode-actual-confirm."+action,"injected-menu-input-production-producer");
                    WinterRequire(actualSource && actualSource.enabled && actualSource.isPlaying && actualSource.outputAudioMixerGroup && actualSource.outputAudioMixerGroup.name=="UI"
                        && WinterNear(actualSource.pitch,1) && WinterNear(actualSource.volume,.899999976f) && actualSource.spatialBlend==0,"audio.mode-source-parameters."+action,"actual-PlayOneShot-field");
                    if(actualSource)yield return WinterSampleOutput(actualSource,"mode."+action);
                }
                hud.SetMenuPage(NativeMenuPage.Main);hud.SelectMenuIndex(0);while(hud.MenuTransitionActive)yield return null;
                int prior=calls;var open=hud.ConfirmMenu(NativeMenuPage.Main);ExecuteMenuAction(open);
                WinterRequire(open==NativeMenuAction.OpenRaceModes && calls==prior+1 && actualClip==hud.menuConfirmSound,"audio.Race-entry-confirm1","injected-menu-input-production-producer");
                hud.SetMenuPage(NativeMenuPage.Main);while(hud.MenuTransitionActive)yield return null;
                prior=calls;
                var navigation=(NativeMenuNavigation)typeof(NativeRaceHud).GetProperty("Navigation",WinterPrivate).GetValue(hud);
                WinterRequire(navigation.IsDisabled(2) && !navigation.Select(2) && calls==prior,"audio.Online-confirm6-disabled","disabled-production-input");
                hud.SetMenuPage(NativeMenuPage.Main);
            }
            finally{NativeAudioRuntime.UiOneShotPlayed-=observe;}
        }
        IEnumerator WinterSampleOutput(AudioSource source,string id)
        {
            var sample=new WinterAudioSample{id=id,path=WinterPath(source.transform),clip=source.clip?source.clip.name:"oneShot",mixer=source.outputAudioMixerGroup?source.outputAudioMixerGroup.name:"",enabled=source.enabled,active=source.gameObject.activeInHierarchy,playing=source.isPlaying,pitch=source.pitch,volume=source.volume,spatialBlend=source.spatialBlend,listenerVolume=AudioListener.volume,muted=NativeAudioRuntime.Muted,audioStart=NativeAudioObservation.Capture(winterAudit.outputMode)};
            var buffer=new float[256];
            for(int i=0;i<8;i++){yield return new WaitForSecondsRealtime(.01f);source.GetOutputData(buffer,0);foreach(float f in buffer)sample.sourcePeak=Mathf.Max(sample.sourcePeak,Mathf.Abs(f));AudioListener.GetOutputData(buffer,0);foreach(float f in buffer)sample.listenerPeak=Mathf.Max(sample.listenerPeak,Mathf.Abs(f));}
            winterAudit.audio.Add(sample);
            sample.audioEnd = NativeAudioObservation.Capture(winterAudit.outputMode);
            WinterRequire(sample.sourcePeak>0,"audio.source-DSP."+id,"actual-source-DSP");
            if(sample.listenerVolume>0)WinterRequire(sample.listenerPeak>0,"audio.listener-DSP."+id,"ordinary-volume-listener-DSP");
        }
        IEnumerator CheckWinterSplash()
        {
            var splash=StartupSplash;
            WinterRequire(splash && splash.IsRunning && splash.PlayCount==1,"audio.splash-startup-actual-producer","production-startup");
            if(!splash)yield break;
            var source=splash.source;
            WinterRequire(source && source.enabled && source.isPlaying && !source.playOnAwake && !source.loop && source.clip && source.clip.name==winterAudit.splashImportedClipName
                && source.outputAudioMixerGroup && source.outputAudioMixerGroup.name=="Music" && source.pitch==1 && source.volume==1 && source.spatialBlend==0
                && splash.duration==10,"audio.splash-actual-source-parameters","actual-Play-field");
            AudioContextTick();
            WinterRequire(NativeAudioRuntime.CurrentMusic=="" && splash.IsRunning,"audio.splash-excludes-menu-music","production-AudioContextTick");
            if(source)yield return WinterSampleOutput(source,"splash");
            float startReal=Time.realtimeSinceStartup;float began=splash.Elapsed;
            while(splash.IsRunning && splash.Elapsed<9.8f)yield return null;
            WinterRequire(splash.IsRunning && splash.CompletionCount==0,"audio.splash-before10sec","real-scaled-clock");
            while(splash.IsRunning && Time.realtimeSinceStartup-startReal<12)yield return null;
            WinterRequire(!splash.IsRunning && splash.CompletionCount==1 && splash.PlayCount==1 && !source.isPlaying && hud.MenuPage==NativeMenuPage.Main
                && Time.realtimeSinceStartup-startReal+began>=9.9f,"audio.splash-real10sec-end-main","real-scaled-clock");
            AudioContextTick();
            WinterRequire(NativeAudioRuntime.CurrentMusic=="Menu","audio.splash-complete-main-music","production-AudioContextTick");
        }
    }
    public sealed class NativeWinterAuditHost : MonoBehaviour { }
}
