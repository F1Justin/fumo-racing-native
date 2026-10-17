using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeItemPrefabRuntimeVerification : MonoBehaviour
    {
        [Serializable] public sealed class Reference
        {
            public string gitHash;
            public Row[] rows;
        }
        [Serializable] public sealed class Row
        {
            public string sourcePath, sourcePrefabName, sourceType, nativeType, expectedIdentity, sourceSiblingPath;
            public int[] sourceTarget;
            public bool sourceEnabled, overrideLocalPosition, playOnAwake;
            public Vector3 rotation;
            public float duration, bobAmplitude, bobRate, pitch, volume, minDistance, maxDistance, doppler, pan, spatialBlend;
            public int priority, rolloffMode;
            public string audioClipName, audioClipGuid, optionKey;
            public QualityAction[] qualityActions;
            public AudioCurve panLevelCustomCurve;
        }
        [Serializable] public sealed class AudioCurve
        {
            public int serializedVersion, preInfinity, postInfinity, rotationOrder;
            public AudioCurveKey[] keys;
        }
        [Serializable] public sealed class AudioCurveKey
        {
            public int serializedVersion, tangentMode, weightedMode;
            public float time, value, inSlope, outSlope, inWeight, outWeight;
        }
        [Serializable] public sealed class QualityAction { public QualityCall[] onTrue, onFalse; }
        [Serializable] public sealed class QualityCall
        {
            public int[] targetPath;
            public string method;
            public int mode, state;
            public bool argument;
        }
        [Serializable] public sealed class Binding
        {
            public string sourcePath;
            public GameObject runtimePrefab;
        }
        [Serializable] public sealed class Check
        {
            public string obligation, kind, detail, status;
            public bool passed, accepted;
        }
        [Serializable] public sealed class Report
        {
            public string sourceGitHash, phase;
            public bool passed, accepted, fullGameplayAcceptance;
            public int sourceRows, checks, failures, pending;
            public Check[] rows;
        }
        readonly List<Check> checks = new List<Check>();
        void CheckValue(bool pass, string id, string kind, string detail)
        {
            checks.Add(new Check { passed = pass, obligation = id, kind = kind, detail = detail, status = pass ? "passed" : "failed" });
        }
        void Pending(string id, string detail)
        {
            checks.Add(new Check { obligation = id, kind = "pending", detail = detail, status = "pending" });
        }
        sealed class LifetimeObservation
        {
            public NativeItemPrefabLifetime component;
            public Row source;
            public float boundary;
            public string boundaryOwner;
            public bool beforeSeen, done;
        }
        static Transform Node(Transform root, int[] path)
        {
            if (path == null) return root;
            foreach (int index in path)
            {
                if (!root || index < 0 || index >= root.childCount) return null;
                root = root.GetChild(index);
            }
            return root;
        }
        static object Value(Component component, string name)
        {
            if (ReferenceEquals(component, null)) return null;
            var type = component.GetType();
            return type.GetField(name)?.GetValue(component) ?? type.GetProperty(name)?.GetValue(component);
        }
        static Component ComponentAt(Transform target, Row row)
        {
            if (!target) return null;
            return target.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().Name == row.nativeType
                && (row.sourceType == "AudioSource" || (string)Value(c, "sourceComponentId") == row.expectedIdentity));
        }
        public void VerifyResourceBindings(Reference source, Binding[] bindings, string reportPath)
        {
            checks.Clear();
            CheckValue(source != null && source.rows != null && source.rows.Length > 0, "source-denominator", "source-reference", "requested reference contains source rows");
            foreach (var row in source.rows)
            {
                var matches = bindings.Where(b => b.sourcePath == row.sourcePath).ToArray();
                string id = row.expectedIdentity;
                CheckValue(matches.Length == 1 && matches[0].runtimePrefab, id, "resource-binding", "one supplied production prefab for exact source path");
                if (matches.Length != 1 || !matches[0].runtimePrefab) continue;
                var node = Node(matches[0].runtimePrefab.transform, row.sourceTarget);
                var component = ComponentAt(node, row);
                CheckValue(component, id, "resource-binding", "component at original sibling-index path with exact asset identity");
                if (!component) continue;
                var behavior = component as Behaviour;
                CheckValue(!behavior || behavior.enabled == row.sourceEnabled, id, "source-field", "original enabled flag");
                if (row.sourceType == "RotateOverTime" || row.sourceType == "LoadingAnim")
                {
                    CheckValue(Value(component, "rotationPerSecond") is Vector3 vector && vector == row.rotation, id, "source-field", "original rotation vector / LoadingAnim up axis");
                    CheckValue((bool)Value(component, "overrideLocalPosition") == row.overrideLocalPosition, id, "source-field", "LoadingAnim absolute local-position mode");
                    if (row.overrideLocalPosition)
                        CheckValue(Mathf.Approximately((float)Value(component, "bobAmplitude"), row.bobAmplitude)
                            && Mathf.Approximately((float)Value(component, "bobRate"), row.bobRate), id, "source-field", "original bob amplitude/rate");
                }
                else if (row.sourceType == "DisapearOverTime")
                    CheckValue(Mathf.Approximately((float)Value(component, "duration"), row.duration), id, "source-field", "original duration, independent raw-YAML expected");
                else if (row.sourceType == "FXOnDestroy")
                    CheckValue(Value(component, "effectPrefab") is GameObject fx && fx, id, "resource-binding", "non-null exported destruction FX; GUID/pixel binding requires Editor field audit");
                else if (component is NativeItemPrefabQuality quality)
                {
                    CheckValue(quality._optionKey == row.optionKey && quality._actions != null && row.qualityActions != null
                        && quality._actions.Length == row.qualityActions.Length, id, "source-field", "original quality option key and action count");
                    if (quality._actions != null && row.qualityActions != null)
                        for (int i = 0; i < Mathf.Min(quality._actions.Length, row.qualityActions.Length); i++)
                        {
                            CheckQualityEvent(quality._actions[i].onTrue, row.qualityActions[i].onTrue, matches[0].runtimePrefab.transform, id);
                            CheckQualityEvent(quality._actions[i].onFalse, row.qualityActions[i].onFalse, matches[0].runtimePrefab.transform, id);
                        }
                }
                else if (component is AudioSource audio)
                {
                    CheckValue(audio.clip && audio.clip.name == row.audioClipName && Mathf.Approximately(audio.pitch, row.pitch)
                        && Mathf.Approximately(audio.volume, row.volume), id, "source-field", "source clip name, pitch and volume; exact clip GUID requires Editor audit");
                    CheckValue(audio.priority == row.priority, id, "source-field", "priority expected=" + row.priority + " actual=" + audio.priority);
                    CheckValue(Mathf.Approximately(audio.minDistance, row.minDistance), id, "source-field", "minDistance expected=" + row.minDistance + " actual=" + audio.minDistance);
                    CheckValue(Mathf.Approximately(audio.maxDistance, row.maxDistance), id, "source-field", "maxDistance expected=" + row.maxDistance + " actual=" + audio.maxDistance);
                    CheckValue(Mathf.Approximately(audio.dopplerLevel, row.doppler), id, "source-field", "dopplerLevel expected=" + row.doppler + " actual=" + audio.dopplerLevel);
                    CheckValue(Mathf.Approximately(audio.panStereo, row.pan), id, "source-field", "panStereo rawExpectedPan2D=" + row.pan + " actual=" + audio.panStereo);
                    CheckValue(Mathf.Approximately(audio.spatialBlend, row.spatialBlend), id, "source-field", "spatialBlend originalCurveAndApiExpected=" + row.spatialBlend + " actual=" + audio.spatialBlend);
                    var spatialCurve = audio.GetCustomCurve(AudioSourceCurveType.SpatialBlend);
                    var expectedKeys = row.panLevelCustomCurve == null ? null : row.panLevelCustomCurve.keys;
                    CheckValue(spatialCurve != null && expectedKeys != null && spatialCurve.keys.Length == expectedKeys.Length
                        && Enumerable.Range(0, expectedKeys.Length).All(i => Mathf.Approximately(spatialCurve.keys[i].time, expectedKeys[i].time)
                            && Mathf.Approximately(spatialCurve.keys[i].value, expectedKeys[i].value)
                            && Mathf.Approximately(spatialCurve.keys[i].inTangent, expectedKeys[i].inSlope)
                            && Mathf.Approximately(spatialCurve.keys[i].outTangent, expectedKeys[i].outSlope)),
                        id, "source-field", "spatialBlend original serialized curve keys expected=" + (expectedKeys == null ? "missing" : string.Join(",", expectedKeys.Select(k => k.time + ":" + k.value)))
                            + " actual=" + (spatialCurve == null ? "missing" : string.Join(",", spatialCurve.keys.Select(k => k.time + ":" + k.value))));
                    CheckValue((int)audio.rolloffMode == row.rolloffMode, id, "source-field", "rolloffMode expected=" + row.rolloffMode + " actual=" + (int)audio.rolloffMode);
                    bool awakeHook = node.GetComponents<NativeAudioEmitter>().Any(e => e.sources != null && e.originalPlayOnAwake != null
                        && Enumerable.Range(0, e.sources.Length).Any(i => i < e.originalPlayOnAwake.Length && e.sources[i] == audio && e.originalPlayOnAwake[i] == row.playOnAwake));
                    CheckValue(!audio.playOnAwake && awakeHook, id, "resource-binding", "source awake obligation delegated once to emitter");
                }
            }
            Write(source, "resource-fields; no live trigger or output acceptance", reportPath);
        }
        public IEnumerator VerifyLifecycle(Reference source, Binding binding, string reportPath, float durationLimit = 8f)
        {
            checks.Clear();
            CheckValue(source != null && source.rows != null && source.rows.Length > 0, "source-denominator", "source-reference", "requested reference contains source rows");
            var relevant = source.rows.Where(r => r.sourcePath == binding.sourcePath).ToArray();
            CheckValue(relevant.Length > 0, binding.sourcePath, "source-denominator", "requested prefab has relevant source rows");
            if (!binding.runtimePrefab) { CheckValue(false, binding.sourcePath, "lifecycle", "missing bound production prefab"); Write(source, "injected production-prefab lifecycle", reportPath); yield break; }
            var parent = new GameObject("Independent prefab runtime trial");
            parent.SetActive(false);
            var instance = Instantiate(binding.runtimePrefab, parent.transform, false);
            instance.SetActive(true);
            var lifetimes = instance.GetComponentsInChildren<NativeItemPrefabLifetime>(true);
            CheckValue(lifetimes.All(c => !c.StartScheduled), binding.sourcePath, "lifecycle", "inactive parent defers Start scheduling");
            parent.SetActive(true);
            yield return new WaitForEndOfFrame();
            foreach (var c in lifetimes)
                CheckValue(!c.enabled || !c.gameObject.activeInHierarchy || c.StartScheduled, c.sourceComponentId, "lifecycle", "first active frame schedules original Start destruction");
            var motions = instance.GetComponentsInChildren<NativeItemPrefabMotion>(true);
            var rotations = motions.Select(c => c.transform.localRotation).ToArray();
            var ticks = motions.Select(c => c.UpdateTicks).ToArray();
            yield return new WaitForEndOfFrame();
            for (int i = 0; i < motions.Length; i++)
            {
                var c = motions[i];
                if (!c || !c.enabled || !c.gameObject.activeInHierarchy) { Pending(c.sourceComponentId, "disabled/inactive/destroyed motion requires separate active-path observation"); continue; }
                var row = relevant.FirstOrDefault(r => r.expectedIdentity == c.sourceComponentId);
                if (row == null) { CheckValue(false, c.sourceComponentId, "lifecycle", "source row missing"); continue; }
                CheckValue(c.UpdateTicks - ticks[i] == 1 && Quaternion.Angle(c.transform.localRotation,
                    rotations[i] * Quaternion.Euler(row.rotation * c.LastDeltaTime)) < .02f,
                    row.expectedIdentity, "lifecycle", "one actual Update matches source self-space rotation and scaled delta");
                if (row.overrideLocalPosition)
                    CheckValue(Vector3.Distance(c.transform.localPosition, new Vector3(0,
                        Mathf.Sin(Time.time * row.bobRate) * row.bobAmplitude + row.bobAmplitude * .5f, 0)) < .0001f,
                        row.expectedIdentity, "lifecycle", "source absolute sine bob uses scaled Time.time");
            }
            var observations = new List<LifetimeObservation>();
            foreach (var c in lifetimes)
            {
                var row = relevant.FirstOrDefault(r => r.expectedIdentity == c.sourceComponentId);
                if (row == null) { CheckValue(false, c.sourceComponentId, "lifecycle", "missing independent lifetime source row"); continue; }
                if (!c.StartScheduled) { Pending(row.expectedIdentity, "disabled/inactive lifetime did not Start; active-path sample still required"); continue; }
                if (row.duration <= 0 || row.duration > durationLimit) { Pending(row.expectedIdentity, "nonpositive/over-limit duration requires independent bounded instance trial"); continue; }
                float boundary = c.StartedAt + row.duration;
                string boundaryOwner = row.expectedIdentity;
                foreach (var ancestor in lifetimes)
                {
                    var ancestorRow = relevant.FirstOrDefault(r => r.expectedIdentity == ancestor.sourceComponentId);
                    if (ancestorRow == null || !ancestor.StartScheduled || ancestorRow.duration <= 0) continue;
                    var path = row.sourceTarget ?? Array.Empty<int>();
                    var parentPath = ancestorRow.sourceTarget ?? Array.Empty<int>();
                    bool contains = parentPath.Length <= path.Length && Enumerable.Range(0, parentPath.Length).All(i => parentPath[i] == path[i]);
                    float ancestorBoundary = ancestor.StartedAt + ancestorRow.duration;
                    if (contains && ancestorBoundary < boundary) { boundary = ancestorBoundary; boundaryOwner = ancestorRow.expectedIdentity; }
                }
                observations.Add(new LifetimeObservation { component = c, source = row, boundary = boundary, boundaryOwner = boundaryOwner });
            }
            while (observations.Any(o => !o.done))
            {
                foreach (var observation in observations.Where(o => !o.done))
                {
                    if (Time.time < observation.boundary - .05f)
                    {
                        CheckValue(observation.component, observation.source.expectedIdentity, "lifecycle", "alive sampled before source duration while all node deadlines monitored together");
                        observation.beforeSeen = true;
                    }
                    if (Time.time < observation.boundary + .1f) continue;
                    if (!observation.beforeSeen) Pending(observation.source.expectedIdentity, "source before-boundary observation missed; duration/frame budget needs separate instance");
                    CheckValue(!observation.component, observation.source.expectedIdentity, "lifecycle", "destroyed after effective original ancestor deadline " + observation.boundaryOwner);
                    observation.done = true;
                }
                if (observations.Any(o => !o.done)) yield return null;
            }
            if (parent) Destroy(parent);
            Write(source, "injected production-prefab lifecycle; natural world/RequestFrozen separate", reportPath);
        }
        public IEnumerator VerifyFrozenRequest(Reference source, NativeKartRecovery recovery, GameObject icePrefab, string reportPath)
        {
            checks.Clear();
            CheckValue(source != null && source.rows != null && source.rows.Length > 0, "source-denominator", "source-reference", "requested reference contains source rows");
            var before = FindObjectsOfType<AudioSource>(true).Select(a => a.GetInstanceID()).ToHashSet();
            bool requested = recovery && recovery.RequestFrozen(icePrefab);
            CheckValue(requested, "frozen-fall", "real-method-trigger", "production RequestFrozen invocation, injected call");
            yield return null;
            var emitted = FindObjectsOfType<AudioSource>(true).Where(a => !before.Contains(a.GetInstanceID()) && a.clip && a.clip.name == "freezebomb").ToArray();
            CheckValue(emitted.Length == 1 && emitted[0].isPlaying && Mathf.Approximately(emitted[0].pitch, 1.4f),
                "frozen-fall", "ordinary-audio-trigger", "one newly instantiated freezebomb source is playing at source pitch");
            CheckValue(recovery && recovery.FrozenFall, "frozen-fall", "real-method-trigger", "production frozen state set");
            foreach (var audio in emitted)
            {
                var emitter = audio.GetComponent<NativeAudioEmitter>();
                CheckValue(audio.outputAudioMixerGroup && audio.outputAudioMixerGroup.name == "SFX" && !audio.playOnAwake
                    && emitter && emitter.originalPlayOnAwake.Any(v => v), "frozen-fall", "resource-binding", "ordinary trigger SFX route and emitter original awake flag");
            }
            Pending("frozen-fall.listener-output", "isPlaying proves Unity source playback; listener/DSP ordinary output sample remains separate");
            if (recovery) typeof(NativeKartRecovery).GetMethod("ClearFrozenFall", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(recovery, null);
            yield return new WaitForEndOfFrame();
            CheckValue(recovery && !recovery.FrozenFall && emitted.All(a => !a), "frozen-fall", "cleanup", "production ClearFrozenFall destroys audio instance and clears frozen motor state");
            Write(source, "injected RequestFrozen trigger; source/listener output and natural fall remain open", reportPath);
        }
        void CheckQualityEvent(UnityEngine.Events.UnityEvent runtime, QualityCall[] expected, Transform root, string id)
        {
            expected = expected ?? Array.Empty<QualityCall>();
            CheckValue(runtime.GetPersistentEventCount() == expected.Length, id, "quality-callback", "original callback count/order");
            for (int i = 0; i < Mathf.Min(runtime.GetPersistentEventCount(), expected.Length); i++)
            {
                var target = Node(root, expected[i].targetPath);
                CheckValue(target && runtime.GetPersistentTarget(i) == target.gameObject && runtime.GetPersistentMethodName(i) == expected[i].method
                    && (int)runtime.GetPersistentListenerState(i) == expected[i].state && expected[i].mode == 6,
                    id, "quality-callback", "source SetActive target sibling path, bool mode and RuntimeOnly state");
            }
        }
        public IEnumerator VerifyQualityValues(Reference source, Binding binding, string reportPath)
        {
            checks.Clear();
            var rows = source.rows.Where(r => r.sourcePath == binding.sourcePath && r.sourceType == "QualityActivator").ToArray();
            CheckValue(rows.Length > 0, binding.sourcePath, "source-denominator", "requested production prefab has source quality rows");
            var clone = Instantiate(binding.runtimePrefab);
            clone.SetActive(true);
            yield return new WaitForEndOfFrame();
            foreach (var row in rows)
            {
                var node = Node(clone.transform, row.sourceTarget);
                var quality = ComponentAt(node, row) as NativeItemPrefabQuality;
                if (!quality) { CheckValue(false, row.expectedIdentity, "quality-callback", "source quality component missing"); continue; }
                for (int selected = 0; selected < row.qualityActions.Length; selected++)
                {
                    var expectedStates = clone.GetComponentsInChildren<Transform>(true)
                        .ToDictionary(nodeState => nodeState, nodeState => nodeState.gameObject.activeSelf);
                    foreach (var action in row.qualityActions)
                        foreach (var call in action.onFalse ?? Array.Empty<QualityCall>()) expectedStates[Node(clone.transform, call.targetPath)] = call.argument;
                    foreach (var call in row.qualityActions[selected].onTrue ?? Array.Empty<QualityCall>()) expectedStates[Node(clone.transform, call.targetPath)] = call.argument;
                    foreach (var action in quality._actions) action.onFalse.Invoke();
                    quality._actions[selected].onTrue.Invoke();
                    CheckValue(expectedStates.All(p => p.Key && p.Key.gameObject.activeSelf == p.Value),
                        row.expectedIdentity, "quality-callback", "independent source command fold matches quality value " + selected + "; user settings untouched");
                }
                quality.Refresh();
                CheckValue(quality.SelectedIndex == NativeUserSettings.GetInt(row.optionKey), row.expectedIdentity,
                    "quality-selector", "production Refresh reads current source option key");
            }
            bool loading = NativeItemDestructionContext.IsSceneLoading;
            NativeItemDestructionContext.IsSceneLoading = true;
            Destroy(clone);
            yield return new WaitForEndOfFrame();
            NativeItemDestructionContext.IsSceneLoading = loading;
            Pending("quality-settings-changed", "all callback values checked independently; natural Changed event and global maxParticles exclusion await integrated Player");
            Write(source, "injected production-prefab quality callbacks and current Refresh; natural settings change separate", reportPath);
        }
        public IEnumerator VerifyDestruction(Reference source, Binding binding, string reportPath)
        {
            checks.Clear();
            CheckValue(source != null && source.rows != null && source.rows.Length > 0, "source-denominator", "source-reference", "requested reference contains source rows");
            bool previousLoading = NativeItemDestructionContext.IsSceneLoading;
            try
            {
                foreach (string scenario in new[] { "normal", "disabled", "loading", "quit" })
                {
                    NativeItemDestructionContext.IsSceneLoading = false;
                    var trial = Instantiate(binding.runtimePrefab, new Vector3(17, 3, -9), Quaternion.Euler(12, 31, 7));
                    trial.SetActive(true);
                    yield return new WaitForEndOfFrame();
                    var components = trial.GetComponentsInChildren<NativeItemDestruction>(true);
                    CheckValue(components.Length > 0, binding.sourcePath, "destruction", "production prefab contains source destruction hooks");
                    var positions = components.Select(c => c.transform.position).ToArray();
                    var rotations = components.Select(c => c.transform.rotation).ToArray();
                    var prefabScales = components.Select(c => c.effectPrefab ? c.effectPrefab.transform.localScale : Vector3.zero).ToArray();
                    foreach (var c in components)
                    {
                        if (scenario == "disabled") c.enabled = false;
                        if (scenario == "quit") typeof(NativeItemDestruction).GetMethod("OnApplicationQuit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, null);
                    }
                    NativeItemDestructionContext.IsSceneLoading = scenario == "loading";
                    Destroy(trial);
                    Destroy(trial);
                    yield return new WaitForEndOfFrame();
                    for (int i = 0; i < components.Length; i++)
                    {
                        var c = components[i];
                        int expected = scenario == "normal" || scenario == "disabled" ? 1 : 0;
                        CheckValue(c.SpawnCount == expected, c.sourceComponentId, "destruction", scenario + ": one OnDestroy callback despite duplicate Destroy, source loading/quit gates");
                        if (expected == 1 && c.LastSpawn)
                        {
                            CheckValue(c.LastSpawn.transform.parent == null && Vector3.Distance(c.LastSpawn.transform.localScale, prefabScales[i]) < .0001f && Vector3.Distance(c.LastSpawn.transform.position, positions[i]) < .0001f
                                && Quaternion.Angle(c.LastSpawn.transform.rotation, rotations[i]) < .001f,
                                c.sourceComponentId, "destruction", "world position/rotation and source FX prefab scale preserved, no parent assigned");
                            NativeItemDestructionContext.IsSceneLoading = true;
                            Destroy(c.LastSpawn);
                        }
                    }
                    yield return new WaitForEndOfFrame();
                }
            }
            finally { NativeItemDestructionContext.IsSceneLoading = previousLoading; }
            Write(source, "injected production-prefab destruction; natural scene-loading predicate wiring separate", reportPath);
        }
        public IEnumerator VerifyInsanityHud(Reference source, NativeInsanityHudSource binding, Transform playerHudRoot, string reportPath)
        {
            checks.Clear();
            CheckValue(source != null && source.rows != null && source.rows.Length > 0, "source-denominator", "source-reference", "requested reference contains source rows");
            int before = binding.SpawnCount;
            var instance = binding.Spawn(playerHudRoot);
            CheckValue(instance && instance.transform.parent == playerHudRoot && binding.SpawnCount == before + 1,
                binding.sourceComponentId, "insanity-hud", "one original UI clone attached to supplied player HUD-relative parent");
            yield return new WaitForEndOfFrame();
            var lifetimes = instance.GetComponentsInChildren<NativeItemPrefabLifetime>(true);
            CheckValue(lifetimes.Length == 1 && Mathf.Approximately(lifetimes[0].duration, 6.5f) && lifetimes[0].StartScheduled,
                binding.sourceComponentId, "insanity-hud", "exactly one source 6.5-second lifecycle hook");
            if (instance) Destroy(instance);
            Write(source, "injected source HUD spawn; natural reverse-stack/profile/Animator visuals separate", reportPath);
        }
        void Write(Reference source, string phase, string path)
        {
            var report = new Report { sourceGitHash = source.gitHash, phase = phase, sourceRows = source.rows.Length,
                checks = checks.Count, passed = checks.Count > 0 && checks.All(c => c.status == "passed"), failures = checks.Count(c => c.status == "failed"), pending = checks.Count(c => c.status == "pending"), rows = checks.ToArray(), accepted = false, fullGameplayAcceptance = false };
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }
    }
}
