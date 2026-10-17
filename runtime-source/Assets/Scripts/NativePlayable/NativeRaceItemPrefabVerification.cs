using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class ItemPrefabScope
        {
            public string name, evidence, error;
            public bool completed, passed, accepted;
            public int checks, failures, pending;
            public int coroutineSteps;
            public string lastYield;
            public float lastScaledTime, lastRealTime, lastTimeScale;
        }
        [Serializable] sealed class ItemPrefabPlayerReport
        {
            public bool passed, accepted, fullGameplayAcceptance, originalRuntimeAccepted, naturalHumanInsanityAccepted, naturalCpuInsanityAccepted, ordinaryAudibleAccepted;
            public string error, assemblySHA256, baselineSHA256;
            public int sourceRows, prefabBindings, failures, pending;
            public float listenerVolume, timeScale, realSeconds, scaledSeconds;
            public string scope = "Original53typed records; production prefab resource fields and injected production method/lifecycle probes. Natural gameplay, original pair and ordinary audible output remain open.";
            public List<ItemPrefabScope> scopes = new List<ItemPrefabScope>();
        }
        static Transform ItemPrefabNode(Transform root, int[] path)
        {
            foreach (int index in path ?? Array.Empty<int>())
            {
                if (!root || index < 0 || index >= root.childCount) return null;
                root = root.GetChild(index);
            }
            return root;
        }
        static bool itemPrefabVerificationRunning;
        NativeItemPrefabRuntimeVerification itemPrefabWaitHost;
        internal IEnumerator CheckItemPrefabBehaviorsLive()
        {
            if (itemPrefabVerificationRunning) yield break;
            itemPrefabVerificationRunning = true;
            var node = new GameObject("Persistent item prefab verification");
            DontDestroyOnLoad(node);
            var host = node.AddComponent<NativeItemPrefabRuntimeVerification>();
            itemPrefabWaitHost = host;
            host.StartCoroutine(CheckItemPrefabManaged());
            yield break;
        }
        IEnumerator CheckItemPrefabManaged()
        {
            var report = new ItemPrefabPlayerReport();
            var stack = new Stack<IEnumerator>(); stack.Push(CheckItemPrefabCore(report));
            while (stack.Count > 0)
            {
                bool next; object value;
                try { next = stack.Peek().MoveNext(); value = next ? stack.Peek().Current : null; }
                catch (Exception error) { report.error = error.ToString(); report.passed = false; break; }
                if (!next) { DisposeItemPrefabRoutine(stack.Pop(), error => report.error = error.ToString()); continue; }
                if (value is IEnumerator child && !(value is CustomYieldInstruction)) { stack.Push(child); continue; }
                if (value is WaitForEndOfFrame)
                {
                    stack.Push(WaitItemPrefabEndOfFrame(null));
                    continue;
                }
                yield return value;
            }
            while (stack.Count > 0) DisposeItemPrefabRoutine(stack.Pop(), error => report.error = error.ToString());
            if (!string.IsNullOrEmpty(report.error)) report.passed = false;
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory, "item-prefab-behaviors-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("ITEM_PREFAB_BEHAVIORS_RESULT passed=" + report.passed + " pending=" + report.pending);
            Application.Quit(report.passed ? 0 : 2);
        }
        IEnumerator CheckItemPrefabCore(ItemPrefabPlayerReport report)
        {
            float oldListener = AudioListener.volume, oldTimeScale = Time.timeScale;
            bool oldLoading = NativeItemDestructionContext.IsSceneLoading;
            bool oldSilenced = NativeAudioRuntime.VerificationListenerSilenced;
            float realStart = Time.realtimeSinceStartup, scaledStart = Time.time;
            var jobs = new List<ItemPrefabScope>();
            var probes = new List<NativeItemPrefabRuntimeVerification>();
            NativeAudioRuntime.VerificationListenerSilenced = true;
            NativeAudioRuntime.ApplyVolumes(); Time.timeScale = 1;
            report.listenerVolume = AudioListener.volume; report.timeScale = Time.timeScale;
            try
            {
                Directory.CreateDirectory(reportDirectory);
                if (Time.captureDeltaTime != 0 || Time.timeScale != 1 || AudioListener.volume != 0 || NativeAudioRuntime.Muted)
                    throw new InvalidOperationException("Clock/listener boundary differs from silent real-time production audio probe");
                var data = NativeContent.Load<NativeItemPrefabVerificationData>(NativeItemPrefabVerificationData.ResourcePath);
                if (!data || data.source == null || data.source.rows == null || data.source.rows.Length != NativeItemPrefabVerificationData.SourceRecordCount)
                    throw new InvalidOperationException("Independent53record source data resource missing or denominator differs");
                if (data.bindings == null || data.bindings.Length != data.source.rows.Select(r => r.sourcePath).Distinct().Count()
                    || data.bindings.Any(b => b == null || !b.runtimePrefab) || data.bindings.Select(b => b.sourcePath).Distinct().Count() != data.bindings.Length)
                    throw new InvalidOperationException("Source path production prefab bindings incomplete or duplicate");
                if (!data.sourceBaseline || ItemPrefabBytesSha256(data.sourceBaseline.bytes) != data.baselineSHA256
                    || data.baselineSHA256 != "8ddca8725fbb908de7f8aa1aef89b5ca4e0df4d2d099f2c40c4cfc6fb2722de5"
                    || JsonUtility.ToJson(JsonUtility.FromJson<NativeItemPrefabRuntimeVerification.Reference>(data.sourceBaseline.text)) != JsonUtility.ToJson(data.source))
                    throw new InvalidOperationException("Typed source records differ from independent original53record baseline");
                report.sourceRows = data.source.rows.Length; report.prefabBindings = data.bindings.Length;
                report.baselineSHA256 = data.baselineSHA256;
                report.assemblySHA256 = ItemPrefabFileSha256(typeof(NativeRace).Assembly.Location);
                var resources = new ItemPrefabScope { name = "resources", evidence = "item-prefab-resource-fields.json" }; report.scopes.Add(resources);
                var resourceProbe = gameObject.AddComponent<NativeItemPrefabRuntimeVerification>(); probes.Add(resourceProbe);
                resourceProbe.VerifyResourceBindings(data.source, data.bindings, Path.Combine(reportDirectory, resources.evidence));
                ReadItemPrefabScope(resources);
                VerifyItemPrefabExactResources(data, report);
                foreach (var binding in data.bindings)
                {
                    var probe = gameObject.AddComponent<NativeItemPrefabRuntimeVerification>(); probes.Add(probe);
                    var scope = new ItemPrefabScope { name = "lifecycle:" + binding.sourcePath, evidence = "item-prefab-lifecycle-" + jobs.Count + ".json" };
                    report.scopes.Add(scope); jobs.Add(scope);
                    StartCoroutine(RunItemPrefabScope(probe.VerifyLifecycle(data.source, binding, Path.Combine(reportDirectory, scope.evidence), 10.25f), scope));
                }
                float lifetimeBatchStarted = Time.realtimeSinceStartup, nextReceipt = lifetimeBatchStarted;
                while (jobs.Any(s => !s.completed))
                {
                    if (Time.realtimeSinceStartup >= nextReceipt)
                    {
                        report.realSeconds = Time.realtimeSinceStartup - realStart; report.scaledSeconds = Time.time - scaledStart;
                        File.WriteAllText(Path.Combine(reportDirectory, "item-prefab-progress.json"), JsonUtility.ToJson(report, true));
                        nextReceipt = Time.realtimeSinceStartup + 1f;
                    }
                    if (Time.timeScale != 1 || Time.captureDeltaTime != 0)
                        throw new InvalidOperationException("Lifetime observation physical/scaled clock changed");
                    if (Time.realtimeSinceStartup - lifetimeBatchStarted > 15f)
                        {
                        foreach (var scope in jobs.Where(s => !s.completed))
                        {
                            scope.pending++; scope.error = "Lifetime deadline: " + scope.lastYield + "; render observation incomplete";
                        }
                        throw new TimeoutException("Lifetime batch exceeded source10sec+5sec; progress receipt identifies incomplete coroutine step/clock");
                    }
                    yield return null;
                }
                foreach (var binding in data.bindings)
                {
                    var rows = data.source.rows.Where(r => r.sourcePath == binding.sourcePath).ToArray();
                    if (rows.Any(r => r.sourceType == "QualityActivator"))
                        yield return ItemPrefabProbe(data, binding, "quality", report, probes);
                    if (rows.Any(r => r.sourceType == "FXOnDestroy"))
                        yield return ItemPrefabProbe(data, binding, "destruction", report, probes);
                }
                var ice = data.bindings.FirstOrDefault(b => data.source.rows.Any(r => r.sourcePath == b.sourcePath && r.sourceType == "AudioSource"));
                var recovery = racers != null && racers.Length > 0 ? racers[0].GetComponent<NativeKartRecovery>() : null;
                if (ice != null && recovery)
                {
                    var probe = gameObject.AddComponent<NativeItemPrefabRuntimeVerification>(); probes.Add(probe);
                    var scope = new ItemPrefabScope { name = "recovery", evidence = "item-prefab-recovery.json" }; report.scopes.Add(scope);
                    yield return RunItemPrefabScope(probe.VerifyFrozenRequest(data.source, recovery, ice.runtimePrefab, Path.Combine(reportDirectory, scope.evidence)), scope);
                }
                else report.scopes.Add(new ItemPrefabScope { name = "recovery", completed = true, pending = 1, error = "Production recovery or independently bound IceCubeFX missing" });
                foreach (var binding in data.bindings.Where(b => b.runtimePrefab.GetComponentInChildren<NativeInsanityHudSource>(true)))
                {
                    var instance = Instantiate(binding.runtimePrefab); var hudSource = instance.GetComponentInChildren<NativeInsanityHudSource>(true);
                    var parent = new GameObject("Injected player HUD root");
                    var probe = gameObject.AddComponent<NativeItemPrefabRuntimeVerification>(); probes.Add(probe);
                    var scope = new ItemPrefabScope { name = "insanity-hud-probe", evidence = "item-prefab-insanity-hud.json" }; report.scopes.Add(scope);
                    yield return RunItemPrefabScope(probe.VerifyInsanityHud(data.source, hudSource, parent.transform, Path.Combine(reportDirectory, scope.evidence)), scope);
                    NativeItemDestructionContext.IsSceneLoading = true; Destroy(instance); Destroy(parent); yield return null; NativeItemDestructionContext.IsSceneLoading = oldLoading;
                }
                if (!report.scopes.Any(s => s.name == "insanity-hud-probe"))
                    report.scopes.Add(new ItemPrefabScope { name = "insanity-hud-probe", completed = true, pending = 1, error = "Production NativeInsanityHudSource binding missing" });
                yield return VerifyItemPrefabWorld(data, report);
                yield return VerifyItemPrefabSceneLoading(data, report);
                report.scopes.Add(new ItemPrefabScope { name = "natural-insanity-human-and-cpu", completed = true, pending = 2, error = "Injected HUD probe supplies no natural human or CPU consumer acceptance" });
            }
            finally
            {
                foreach (var probe in probes) if (probe) Destroy(probe);
                NativeAudioRuntime.VerificationListenerSilenced = oldSilenced;
                AudioListener.volume = oldListener; Time.timeScale = oldTimeScale; NativeItemDestructionContext.IsSceneLoading = oldLoading;
                report.realSeconds = Time.realtimeSinceStartup - realStart; report.scaledSeconds = Time.time - scaledStart;
                report.failures = report.scopes.Sum(s => s.failures); report.pending = report.scopes.Sum(s => s.pending);
                report.passed = string.IsNullOrEmpty(report.error) && report.scopes.Count > 0 && report.failures == 0 && report.scopes.All(s => s.completed);
                File.WriteAllText(Path.Combine(reportDirectory, "item-prefab-behaviors-report.json"), JsonUtility.ToJson(report, true));
            }
        }
        static string ItemPrefabFileSha256(string path)
        {
            return ItemPrefabBytesSha256(File.ReadAllBytes(path));
        }
        static string ItemPrefabBytesSha256(byte[] bytes)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        IEnumerator VerifyItemPrefabWorld(NativeItemPrefabVerificationData data, ItemPrefabPlayerReport report)
        {
            var scope = new ItemPrefabScope { name = "world-production-SpawnEffect", completed = false, evidence = "item-prefab-world.json" }; report.scopes.Add(scope);
            var source = data.source.rows.First(r => r.sourceType == "DisapearOverTime" && r.sourceTarget.Length == 0 && r.duration == 10f);
            var binding = data.bindings.Single(b => b.sourcePath == source.sourcePath);
            var before = FindObjectsOfType<NativeItemPrefabLifetime>(true).Select(c => c.GetInstanceID()).ToHashSet();
            float realStarted = Time.realtimeSinceStartup;
            itemWorld.SpawnEffect(binding.runtimePrefab, new Vector3(17, 3, -9), Quaternion.identity, .1f);
            yield return new WaitForEndOfFrame();
            var spawned = FindObjectsOfType<NativeItemPrefabLifetime>(true).Where(c => !before.Contains(c.GetInstanceID()) && c.sourceComponentId == source.expectedIdentity).ToArray();
            scope.checks++;
            if (spawned.Length != 1 || !spawned[0].StartScheduled) { scope.failures++; scope.error = "Production SpawnEffect source10sec lifetime not scheduled exactly once"; }
            else
            {
                var lifetime = spawned[0]; float boundary = lifetime.StartedAt + source.duration;
                while (Time.time < lifetime.StartedAt + .3f) yield return null;
                scope.checks++; if (!lifetime) { scope.failures++; scope.error = "Production world fallback .1sec prematurely destroyed source10sec prefab"; }
                while (Time.time < boundary - .05f) yield return null;
                scope.checks++; if (!lifetime) { scope.failures++; scope.error = "Source10sec prefab absent before deadline"; }
                while (Time.time < boundary + .1f) yield return null;
                scope.checks++; if (lifetime) { scope.failures++; scope.error = "Source10sec prefab survived source deadline"; }
                scope.checks++; if (Time.realtimeSinceStartup - realStarted < 9.9f) { scope.failures++; scope.error = "Real10second observation shortened"; }
            }
            scope.pending = 1; scope.error = scope.error ?? "Injected production SpawnEffect path verified; natural item effect caller remains open";
            scope.completed = true; scope.passed = scope.failures == 0;
            File.WriteAllText(Path.Combine(reportDirectory, scope.evidence), JsonUtility.ToJson(scope, true));
        }
        IEnumerator VerifyItemPrefabSceneLoading(NativeItemPrefabVerificationData data, ItemPrefabPlayerReport report)
        {
            var scope = new ItemPrefabScope { name = "scene-loading-production-ChangeTrack", evidence = "item-prefab-scene-loading.json" }; report.scopes.Add(scope);
            var binding = data.bindings.First(b => data.source.rows.Any(r => r.sourcePath == b.sourcePath && r.sourceType == "FXOnDestroy"));
            NativeItemDestructionContext.IsSceneLoading = false;
            var instance = Instantiate(binding.runtimePrefab); instance.SetActive(true);
            yield return new WaitForEndOfFrame();
            var destruction = instance.GetComponentsInChildren<NativeItemDestruction>(true);
            bool sceneObserved = false, loadingAtSceneCallback = false;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> observe = (scene, mode) => { sceneObserved = true; loadingAtSceneCallback = NativeItemDestructionContext.IsSceneLoading; };
            SceneManager.sceneLoaded += observe;
            try
            {
                bool requested = ChangeTrack("forest", false);
                scope.checks++; if (!requested) { scope.failures++; scope.error = "Actual production ChangeTrack rejected exported forest scene"; }
                for (int frame = 0; frame < 120 && !sceneObserved; frame++) yield return null;
                scope.checks++; if (!sceneObserved || !loadingAtSceneCallback) { scope.failures++; scope.error = "Real sceneLoaded callback did not preserve production loading gate"; }
                yield return null; yield return null;
                scope.checks++; if (AudioListener.volume != 0 || NativeAudioRuntime.Muted || Time.timeScale != 1)
                { scope.failures++; scope.error = "Scene initialization changed silent listener or production audio/scaled-time state"; }
                scope.checks++; if (NativeItemDestructionContext.IsSceneLoading) { scope.failures++; scope.error = "Loading bridge gate was not released after actual loaded nextframe"; }
                scope.checks++; if (destruction.Length == 0 || destruction.Any(c => c.SpawnCount != 0)) { scope.failures++; scope.error = "Actual scene teardown emitted destruction FX"; }
            }
            finally { SceneManager.sceneLoaded -= observe; }
            scope.completed = true; scope.passed = scope.failures == 0;
            File.WriteAllText(Path.Combine(reportDirectory, scope.evidence), JsonUtility.ToJson(scope, true));
        }
        IEnumerator ItemPrefabProbe(NativeItemPrefabVerificationData data, NativeItemPrefabRuntimeVerification.Binding binding, string kind, ItemPrefabPlayerReport report, List<NativeItemPrefabRuntimeVerification> probes)
        {
            var probe = gameObject.AddComponent<NativeItemPrefabRuntimeVerification>(); probes.Add(probe);
            var scope = new ItemPrefabScope { name = kind + ":" + binding.sourcePath, evidence = "item-prefab-" + kind + "-" + report.scopes.Count + ".json" }; report.scopes.Add(scope);
            var routine = kind == "quality" ? probe.VerifyQualityValues(data.source, binding, Path.Combine(reportDirectory, scope.evidence)) : probe.VerifyDestruction(data.source, binding, Path.Combine(reportDirectory, scope.evidence));
            yield return RunItemPrefabScope(routine, scope);
        }
        IEnumerator RunItemPrefabScope(IEnumerator routine, ItemPrefabScope scope)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            while (stack.Count > 0)
            {
                bool next; object value;
                try { next = stack.Peek().MoveNext(); value = next ? stack.Peek().Current : null; }
                catch (Exception error) { scope.error = error.ToString(); scope.failures++; break; }
                if (!next) { DisposeItemPrefabRoutine(stack.Pop(), error => { scope.error = error.ToString(); scope.failures++; }); continue; }
                if (value is IEnumerator child && !(value is CustomYieldInstruction)) { stack.Push(child); continue; }
                scope.coroutineSteps++;
                scope.lastYield = value == null ? "next-frame" : value.GetType().FullName;
                scope.lastScaledTime = Time.time; scope.lastRealTime = Time.realtimeSinceStartup; scope.lastTimeScale = Time.timeScale;
                if (value is WaitForEndOfFrame)
                {
                    var endOfFrame = WaitItemPrefabEndOfFrame(scope);
                    while (true)
                    {
                        bool waiting;
                        try { waiting = endOfFrame.MoveNext(); }
                        catch (Exception error) { scope.error = error.ToString(); break; }
                        if (!waiting) break;
                        yield return null;
                    }
                    DisposeItemPrefabRoutine(endOfFrame, error => scope.error = error.ToString());
                    if (!string.IsNullOrEmpty(scope.error)) break;
                }
                else yield return value;
            }
            while (stack.Count > 0) DisposeItemPrefabRoutine(stack.Pop(), error => { scope.error = error.ToString(); scope.failures++; });
            if (string.IsNullOrEmpty(scope.error)) ReadItemPrefabScope(scope);
            scope.completed = true;
        }
        IEnumerator WaitItemPrefabEndOfFrame(ItemPrefabScope scope)
        {
            bool resumed = false;
            Coroutine wait = itemPrefabWaitHost.StartCoroutine(ItemPrefabEndOfFrameSignal(() => resumed = true));
            float started = Time.realtimeSinceStartup;
            try
            {
                while (!resumed)
                {
                    if (Time.realtimeSinceStartup - started > 5f)
                    {
                        const string detail = "Render stall: actual WaitForEndOfFrame did not resume within 5 real seconds";
                        if (scope != null) { scope.pending++; scope.error = detail; }
                        throw new TimeoutException(detail);
                    }
                    yield return null;
                }
            }
            finally { if (!resumed && itemPrefabWaitHost) itemPrefabWaitHost.StopCoroutine(wait); }
        }
        static IEnumerator ItemPrefabEndOfFrameSignal(Action resumed)
        {
            yield return new WaitForEndOfFrame();
            resumed();
        }
        static void DisposeItemPrefabRoutine(IEnumerator routine, Action<Exception> failed)
        {
            try { (routine as IDisposable)?.Dispose(); }
            catch (Exception error) { failed(error); }
        }
        void ReadItemPrefabScope(ItemPrefabScope scope)
        {
            var helper = JsonUtility.FromJson<NativeItemPrefabRuntimeVerification.Report>(File.ReadAllText(Path.Combine(reportDirectory, scope.evidence)));
            scope.checks = helper.checks; scope.failures = helper.failures; scope.pending = helper.pending;
            scope.passed = helper.failures == 0; scope.completed = true; scope.accepted = false;
        }
        void VerifyItemPrefabExactResources(NativeItemPrefabVerificationData data, ItemPrefabPlayerReport report)
        {
            var scope = new ItemPrefabScope { name = "original-resource-object-identities", completed = true }; report.scopes.Add(scope);
            foreach (var row in data.source.rows.Where(r => r.sourceType == "FXOnDestroy" || r.sourceType == "AudioSource"))
            {
                var expected = (data.sourceResources ?? Array.Empty<NativeItemPrefabVerificationData.SourceResource>()).Where(r => r.expectedIdentity == row.expectedIdentity).ToArray();
                var binding = data.bindings.Single(b => b.sourcePath == row.sourcePath); var node = ItemPrefabNode(binding.runtimePrefab.transform, row.sourceTarget);
                bool valid = expected.Length == 1 && node;
                if (valid && row.sourceType == "FXOnDestroy")
                {
                    var component = node.GetComponents<NativeItemDestruction>().SingleOrDefault(c => c.sourceComponentId == row.expectedIdentity);
                    valid = component && expected[0].effectPrefab && component.effectPrefab == expected[0].effectPrefab;
                }
                if (valid && row.sourceType == "AudioSource") valid = node.GetComponent<AudioSource>() && expected[0].audioClip && node.GetComponent<AudioSource>().clip == expected[0].audioClip;
                scope.checks++; if (!valid) { scope.failures++; scope.error = "Exact original exported resource object mismatch:" + row.expectedIdentity; }
            }
            scope.passed = scope.failures == 0;
        }
    }
}
