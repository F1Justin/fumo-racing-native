using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        Dictionary<string, RectTransform> sourceWheelTransforms;
        bool sourceTransformOnlyGraph, sourceMainTweenStarted, sourceMainTweenRunning;
        float sourceMainTweenElapsed, sourceMainTweenDuration, sourceMainTweenStartCounter;
        Vector3 sourceMainTweenStartEuler, sourceMainTweenDelta;
        public string SourceMenu2WriterModel => "native-update-then-scaled-tween; original-order-unobserved";
        public int SourceMenu2ForwardCopyCount { get; private set; }
        public Quaternion SourceMenu2LastCopyMainWorld { get; private set; }
        public Quaternion SourceMenu2LastCopyRaceWorld { get; private set; }
        public bool SourceMenu2TransformGraphPresent => sourceWheelTransforms != null;
        public bool SourceMenu2ScaledMainTweenRunning => sourceMainTweenRunning;
        const string MainSourceWheel = "menu2/Canvas/MainMenu/WheelRoot/Wheel";
        const string ExtrasSourceWheel = "menu2/Canvas/ExtrasMenu/WheelRoot/Wheel";
        const string OptionsSourceWheel = "menu2/Canvas/OptionsMenu/WheelRoot/Wheel";
        const string RaceSourceWheel = "menu2/Canvas/RaceMenu/Wheel";
        bool sourceClockGraphRequested;
        public bool SourceMenu2RenderedConsumersObserved => authoredUi != null && sourceWheelTransforms != null;
        public bool SourceMenu2RenderedConsumersQuaternionMatch => SourceMenu2RenderedConsumersObserved
            && authoredUi.SourceMenu2RenderedWheelQuaternionsMatch(sourceWheelTransforms);
        void BindNormalSourceWheelGraph()
        {
            if (sourceWheelTransforms != null && new[] { MainSourceWheel, ExtrasSourceWheel, OptionsSourceWheel, RaceSourceWheel }
                .Any(path => !sourceWheelTransforms[path])) DestroySourceClockGraph();
            if (menu2SourceScenePresent && sourceWheelTransforms == null && authoredUi != null)
            {
                if (authoredUi.PrefabAuthored) BindMenu2SourceWheelTransforms(authoredUi.MenuTransformGraph(), false);
                else InitializeMenu2SourceTransformGraphForCheck();
            }
        }
        void PrepareMenu2SourceRenderedConsumers()
        {
            BindNormalSourceWheelGraph();
            if (authoredUi != null && sourceWheelTransforms != null) authoredUi.ApplySourceMenu2WheelQuaternions(sourceWheelTransforms);
        }
        float RenderSourceWheelAngle(string path, float fallback) => sourceWheelTransforms == null
            ? fallback : sourceWheelTransforms[path].localEulerAngles.z;
        void BindMenu2SourceWheelTransforms(Dictionary<string, RectTransform> graph, bool transformOnly)
        {
            sourceWheelTransforms = graph;
            sourceTransformOnlyGraph = transformOnly;
            ResetBoundSourceWheelRotations();
            string[] paths = { MainSourceWheel, ExtrasSourceWheel, OptionsSourceWheel, RaceSourceWheel };
            float[] angles = { sourceMainWheelAngle, sourceExtrasWheelAngle, sourceOptionsWheelAngle, sourceRaceWheelAngle };
            for (int i = 0; i < paths.Length; i++)
                sourceWheelTransforms[paths[i]].Rotate(0, 0, angles[i] - sourceWheelTransforms[paths[i]].localEulerAngles.z);
        }
        public RectTransform SourceMenu2Transform(string path) => sourceWheelTransforms[path];
        public void InitializeMenu2SourceTransformGraphForCheck()
        {
            sourceClockGraphRequested = true;
            if (sourceWheelTransforms != null) return;
            var graph = new Dictionary<string, RectTransform>();
            var ids = new Dictionary<long, RectTransform>();
            foreach (var node in authoredUiData.nodes.Where(n => n.scene == "menu2").OrderBy(n => n.path.Count(c => c == '/')).ThenBy(n => n.order))
            {
                var target = new GameObject(node.path, typeof(RectTransform)).GetComponent<RectTransform>();
                if (ids.TryGetValue(node.parent, out var parent)) target.SetParent(parent, false);
                target.anchorMin = node.anchorMin; target.anchorMax = node.anchorMax; target.pivot = node.pivot;
                target.anchoredPosition = node.position; target.sizeDelta = node.size;
                target.localRotation = node.rotation; target.localScale = node.scale;
                target.gameObject.SetActive(node.active);
                ids[node.id] = target; graph["menu2/" + node.path] = target;
            }
            BindMenu2SourceWheelTransforms(graph, true);
        }
        void DestroySourceClockGraph()
        {
            sourceMainTweenRunning = false;
            if (sourceWheelTransforms == null) return;
            var roots = sourceWheelTransforms.Values.Where(t => t && t.parent == null).Select(t => t.gameObject).ToArray();
            sourceWheelTransforms = null;
            if (sourceTransformOnlyGraph) foreach (var root in roots) Object.Destroy(root);
        }
        void BeginSourceWheelTransformGeneration()
        {
            sourceMainTweenStarted = false; sourceMainTweenRunning = false;
            SourceMenu2ForwardCopyCount = 0;
            DestroySourceClockGraph();
            if (sourceClockGraphRequested)
            {
                if (authoredUi != null && authoredUi.PrefabAuthored) BindNormalSourceWheelGraph();
                else InitializeMenu2SourceTransformGraphForCheck();
            }
        }
        void ResetBoundSourceWheelRotations()
        {
            if (sourceWheelTransforms == null) return;
            foreach (var path in new[] { MainSourceWheel, ExtrasSourceWheel, OptionsSourceWheel, RaceSourceWheel })
                sourceWheelTransforms[path].localRotation = authoredUi != null && authoredUi.PrefabAuthored
                    ? authoredUi.Definition(path).rotation : authoredUiData.nodes.Single(n => n.scene + "/" + n.path == path).rotation;
        }
        void WriteSourceWheelActivity(bool mainParent, bool mainLeaf, bool extrasParent, bool optionsParent, bool raceParent, bool raceLeaf)
        {
            if (!sourceTransformOnlyGraph) return;
            sourceWheelTransforms["menu2/Canvas/MainMenu"].gameObject.SetActive(mainParent);
            sourceWheelTransforms[MainSourceWheel].gameObject.SetActive(mainLeaf);
            sourceWheelTransforms["menu2/Canvas/ExtrasMenu"].gameObject.SetActive(extrasParent);
            sourceWheelTransforms["menu2/Canvas/OptionsMenu"].gameObject.SetActive(optionsParent);
            sourceWheelTransforms["menu2/Canvas/RaceMenu"].gameObject.SetActive(raceParent);
            sourceWheelTransforms[RaceSourceWheel].gameObject.SetActive(raceLeaf);
        }
        void RotateSourceWheel(string path, ref float angle, float speed, float deltaTime)
        {
            angle += speed * deltaTime;
            if (sourceWheelTransforms != null) sourceWheelTransforms[path].Rotate(new Vector3(0, 0, speed) * deltaTime);
        }
        bool CopySourceWheelWorldRotation(string source, string target, ref float targetAngle)
        {
            if (sourceWheelTransforms == null) return false;
            var sourceTransform = sourceWheelTransforms[source];
            var targetTransform = sourceWheelTransforms[target];
            targetTransform.rotation = sourceTransform.rotation;
            targetAngle = targetTransform.localEulerAngles.z;
            if (target == RaceSourceWheel)
            {
                SourceMenu2ForwardCopyCount++;
                SourceMenu2LastCopyMainWorld = sourceTransform.rotation;
                SourceMenu2LastCopyRaceWorld = targetTransform.rotation;
            }
            return true;
        }
        static float SourceFastEulerDelta(float start, float end)
        {
            if (end >= 360) end %= 360;
            float delta = end - start;
            if (delta > 180) delta -= 360;
            else if (delta < -180) delta += 360;
            return delta;
        }
        void TickScaledSourceMainTween(float deltaTime)
        {
            if (transitionPending && (transitionFrom == NativeMenuPage.RaceModes || transitionTo == NativeMenuPage.RaceModes)
                && !sourceMainTweenStarted && Time.unscaledTime - transitionStarted >= 1f / 3f && sourceWheelTransforms != null)
            {
                sourceMainTweenStarted = true; sourceMainTweenRunning = true; sourceMainTweenElapsed = 0; sourceMainTweenDuration = 1f / 3f;
                sourceMainTweenStartEuler = sourceWheelTransforms[MainSourceWheel].localEulerAngles;
                sourceMainTweenStartCounter = sourceMainWheelAngle;
                float targetZ = sourceMainTweenStartEuler.z + (transitionTo == NativeMenuPage.RaceModes ? 1440 : -1440);
                sourceMainTweenDelta = new Vector3(SourceFastEulerDelta(sourceMainTweenStartEuler.x, 0),
                    SourceFastEulerDelta(sourceMainTweenStartEuler.y, 0), SourceFastEulerDelta(sourceMainTweenStartEuler.z, targetZ));
            }
            if (!sourceMainTweenRunning || sourceWheelTransforms == null) return;
            sourceMainTweenElapsed += deltaTime;
            float t = Mathf.Clamp01(sourceMainTweenElapsed / sourceMainTweenDuration), eased = 1 - (1 - t) * (1 - t);
            sourceWheelTransforms[MainSourceWheel].localRotation = Quaternion.Euler(sourceMainTweenStartEuler + sourceMainTweenDelta * eased);
            sourceMainWheelAngle = sourceMainTweenStartCounter + sourceMainTweenDelta.z * eased;
            if (t >= 1) sourceMainTweenRunning = false;
        }
        public void SyncSourceMainWheelAngleFromTransform()
        {
            if (sourceWheelTransforms != null && !sourceMainTweenRunning)
                sourceMainWheelAngle += Mathf.DeltaAngle(sourceMainWheelAngle, sourceWheelTransforms[MainSourceWheel].localEulerAngles.z);
        }
        void ResetSourceMainTween()
        {
            sourceMainTweenStarted = false;
        }
    }
    public sealed partial class NativeAuthoredUi
    {
        static readonly string[] SourceWheelConsumerPaths = {
            "menu2/Canvas/MainMenu/WheelRoot/Wheel", "menu2/Canvas/ExtrasMenu/WheelRoot/Wheel",
            "menu2/Canvas/OptionsMenu/WheelRoot/Wheel", "menu2/Canvas/RaceMenu/Wheel" };
        public void ApplySourceMenu2WheelQuaternions(Dictionary<string, RectTransform> source)
        {
            foreach (var path in SourceWheelConsumerPaths) transforms[path].localRotation = source[path].localRotation;
        }
        public bool SourceMenu2RenderedWheelQuaternionsMatch(Dictionary<string, RectTransform> source)
        {
            foreach (var path in SourceWheelConsumerPaths)
                if (!transforms[path] || Quaternion.Angle(transforms[path].localRotation, source[path].localRotation) > .001f) return false;
            return true;
        }
    }
}
