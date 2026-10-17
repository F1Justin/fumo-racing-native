using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeTreeLodVerification
    {
        const float FieldOfView = 60f;
        const int Steps = 24;
        const float StepSeconds = 0.25f;

        [Serializable] sealed class Capture
        {
            public string file, transition, stage;
            public int frame, expectedLod;
            public float unscaledTime, secondsSinceCrossing, threshold, distance, estimatedScreenRelativeHeight;
            public Vector3 cameraPosition;
            public bool[] rendererVisible;
        }

        [Serializable] sealed class MaterialAudit
        {
            public string name, shader;
            public bool supported;
        }

        [Serializable] sealed class Report
        {
            public string version, platform, graphics, trackId, treeName, note;
            public Vector3 treeReferencePoint, roadAnchor;
            public float treeWorldSize, fieldOfView;
            public float[] screenRelativeThresholds;
            public int width, height, totalLodGroups, treeLodGroups, visualErrors;
            public int[] treeLodLevelCounts;
            public bool crossFade, animateCrossFading, sourceTopology, isolatedTree, passed;
            public string[] rendererTypes, unsupportedShaders;
            public MaterialAudit[] materials;
            public Capture[] captures;
        }

        public static IEnumerator Run(NativeRace race, string reportDirectory)
        {
            Directory.CreateDirectory(reportDirectory);
            var all = UnityEngine.Object.FindObjectsOfType<LODGroup>(true);
            Func<LODGroup, bool> isTree = race.trackId == "forest"
                ? new Func<LODGroup, bool>(group => group.name.StartsWith("tree_1", StringComparison.Ordinal))
                : race.trackId == "mountains"
                    ? new Func<LODGroup, bool>(group => group.name.StartsWith("AspenTree", StringComparison.Ordinal) ||
                        group.name.StartsWith("HillTree", StringComparison.Ordinal) || group.name == "Bushmesh")
                    : race.trackId == "wintermistylake"
                        ? new Func<LODGroup, bool>(group => group.name.StartsWith("Pine", StringComparison.Ordinal))
                        : new Func<LODGroup, bool>(group => false);
            var trees = all.Where(isTree).ToArray();
            var renderers = trees.SelectMany(group => group.GetLODs()).SelectMany(lod => lod.renderers)
                .Where(renderer => renderer).Distinct().ToArray();
            var materials = renderers.SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material).Distinct().ToArray();
            var report = new Report
            {
                version = Application.version, platform = SystemInfo.operatingSystem,
                graphics = SystemInfo.graphicsDeviceName, trackId = race.trackId,
                width = Screen.width, height = Screen.height, fieldOfView = FieldOfView,
                totalLodGroups = all.Length, treeLodGroups = trees.Length,
                treeLodLevelCounts = Enumerable.Range(1, Math.Max(1, trees.Select(group => group.GetLODs().Length).DefaultIfEmpty(0).Max()))
                    .Select(count => trees.Count(group => group.GetLODs().Length == count)).ToArray(),
                rendererTypes = renderers.Select(renderer => renderer.GetType().Name).Distinct().OrderBy(name => name).ToArray(),
                materials = materials.Select(material => new MaterialAudit
                {
                    name = material.name, shader = material.shader ? material.shader.name : "missing",
                    supported = material.shader && material.shader.isSupported
                }).OrderBy(material => material.name).ToArray(),
                unsupportedShaders = materials.Where(material => !material.shader || !material.shader.isSupported)
                    .Select(material => material.shader ? material.shader.name : "missing").Distinct().OrderBy(name => name).ToArray(),
                captures = new Capture[0],
                note = "Screen-height crossing times are estimates. Compare sequential PNGs for visible foliage and uninterrupted fades."
            };

            if (race.trackId == "purplefield")
            {
                var menuContent = race.menuPresentation && race.menuPresentation.content
                    ? race.menuPresentation.content.transform : null;
                var courseTrees = all.Where(group => (!menuContent || !group.transform.IsChildOf(menuContent)) &&
                    (group.name.StartsWith("tree_1", StringComparison.Ordinal) ||
                    group.name.StartsWith("AspenTree", StringComparison.Ordinal) ||
                    group.name.StartsWith("HillTree", StringComparison.Ordinal) || group.name == "Bushmesh" ||
                    group.name.StartsWith("Pine", StringComparison.Ordinal))).ToArray();
                report.treeLodGroups = courseTrees.Length;
                report.sourceTopology = courseTrees.Length == 0;
                report.passed = report.sourceTopology && race.VisualErrorCount == 0;
                report.note = "The original Purplefield scene contains no LODGroup trees; imported menu trees are excluded.";
                Finish();
                Application.Quit(report.passed ? 0 : 2);
                yield break;
            }

            var target = race.trackId == "forest" ? new Vector3(26.99543f, -0.3251629f, 241.44183f)
                : race.trackId == "mountains" ? new Vector3(-291.87878f, 35.21443f, 366.8091f)
                : new Vector3(142.53f, 18.68f, 215.57f);
            var anchor = race.trackId == "forest" ? new Vector3(-1.16f, 3.27f, 218.92f)
                : race.trackId == "mountains" ? new Vector3(-303.15f, 31.97f, 356.22f)
                : new Vector3(147.40001f, 19.7f, 234.6f);
            var tree = trees.OrderBy(group => Vector3.Distance(group.transform.position, target)).FirstOrDefault();
            if (!tree || Vector3.Distance(tree.transform.position, target) > 2f)
                throw new InvalidOperationException("Original tree fixture is missing on " + race.trackId);
            var lods = tree.GetLODs();
            if (lods.Length < 2 || lods.Any(lod => lod.renderers.Length == 0))
                throw new InvalidOperationException("Original tree has no near/far LOD pair: " + tree.name);
            var treeRenderers = lods.SelectMany(lod => lod.renderers).Where(renderer => renderer).Distinct().ToArray();
            if (UnityEngine.Object.FindObjectsOfType<Renderer>(true).Any(renderer => renderer.gameObject.layer == 30 &&
                !treeRenderers.Contains(renderer)))
                throw new InvalidOperationException("Tree LOD capture layer 30 is occupied by another renderer");
            var center = tree.transform.TransformPoint(tree.localReferencePoint);
            var direction = new Vector3(center.x - anchor.x, 0f, center.z - anchor.z).normalized;
            var scale = tree.transform.lossyScale;
            float worldSize = tree.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            float tangent = Mathf.Tan(FieldOfView * Mathf.Deg2Rad * .5f);
            var captures = new List<Capture>();
            report.treeName = tree.name;
            report.treeReferencePoint = center;
            report.roadAnchor = anchor;
            report.treeWorldSize = worldSize;
            report.screenRelativeThresholds = lods.Select(lod => lod.screenRelativeTransitionHeight).ToArray();
            report.crossFade = tree.fadeMode == LODFadeMode.CrossFade;
            report.animateCrossFading = tree.animateCrossFading;
            var sourceThresholds = race.trackId == "forest"
                ? new[] { .79121286f, .47693983f, .01836405f }
                : race.trackId == "mountains"
                    ? new[] { .83264846f, .5638316f, .15050307f, .04625898f }
                    : new[] { .89761937f, .19671014f, .036710683f };
            report.sourceTopology = lods.Length == sourceThresholds.Length &&
                lods.All(lod => lod.renderers.Length == 1) &&
                lods.Select((lod, index) => Mathf.Abs(lod.screenRelativeTransitionHeight - sourceThresholds[index]) < .0001f).All(equal => equal);

            var camera = race.raceCamera;
            var oldPosition = camera.transform.position;
            var oldRotation = camera.transform.rotation;
            var oldFov = camera.fieldOfView;
            var oldCullingMask = camera.cullingMask;
            var oldTimeScale = Time.timeScale;
            var oldTreeLayers = treeRenderers.Select(renderer => renderer.gameObject.layer).ToArray();
            var motors = race.racers.Where(motor => motor).ToArray();
            var motorStates = motors.Select(motor => motor.enabled).ToArray();
            try
            {
                race.SetVisualCapture("race", false);
                foreach (var motor in motors) motor.enabled = false;
                camera.fieldOfView = FieldOfView;
                for (int i = 0; i < treeRenderers.Length; i++) treeRenderers[i].gameObject.layer = 30;
                camera.cullingMask = 1 << 30;
                report.isolatedTree = true;
                report.note = "Isolated-tree camera sweep at original LOD thresholds; compare sequential PNGs for uninterrupted fades.";
                Time.timeScale = 1f;
                for (int index = lods.Length - 2; index >= 0; index--)
                    yield return Sweep(index);
                report.captures = captures.ToArray();
                report.visualErrors = race.VisualErrorCount;
                report.passed = report.crossFade && report.animateCrossFading && report.sourceTopology &&
                    report.isolatedTree &&
                    trees.All(group => group.fadeMode == LODFadeMode.CrossFade && group.animateCrossFading) &&
                    report.unsupportedShaders.Length == 0 && report.visualErrors == 0 &&
                    captures.Count == (lods.Length - 1) * 6;
                Finish();
            }
            finally
            {
                Time.timeScale = oldTimeScale;
                for (int i = 0; i < motors.Length; i++) motors[i].enabled = motorStates[i];
                for (int i = 0; i < treeRenderers.Length; i++) treeRenderers[i].gameObject.layer = oldTreeLayers[i];
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
                camera.cullingMask = oldCullingMask;
            }
            Application.Quit(report.passed ? 0 : 2);

            IEnumerator Sweep(int thresholdIndex)
            {
                string label = "lod" + (thresholdIndex + 1) + "-to" + thresholdIndex;
                float threshold = lods[thresholdIndex].screenRelativeTransitionHeight;
                float thresholdDistance = worldSize / (2f * tangent * threshold);
                float farDistance = thresholdDistance / .70f;
                float nearDistance = thresholdDistance / 1.30f;
                float crossingTime = -1f;
                Place(farDistance);
                yield return new WaitForSecondsRealtime(.8f);
                yield return CaptureFrame(label, "before", threshold, crossingTime);
                for (int step = 1; step <= Steps; step++)
                {
                    float distance = Mathf.Lerp(farDistance, nearDistance, step / (float)Steps);
                    Place(distance);
                    yield return new WaitForSecondsRealtime(StepSeconds);
                    if (crossingTime < 0f && distance <= thresholdDistance) crossingTime = Time.unscaledTime;
                    if (step == 14 || step == 16 || step == 18 || step == Steps)
                        yield return CaptureFrame(label, "step-" + step, threshold, crossingTime);
                }
                yield return new WaitForSecondsRealtime(.8f);
                yield return CaptureFrame(label, "settled", threshold, crossingTime);
            }

            void Place(float distance)
            {
                var position = center - direction * distance;
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(center - position));
            }

            IEnumerator CaptureFrame(string label, string stage, float threshold, float crossingTime)
            {
                yield return new WaitForEndOfFrame();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                string file = label + "-" + stage + ".png";
                File.WriteAllBytes(Path.Combine(reportDirectory, file), texture.EncodeToPNG());
                UnityEngine.Object.Destroy(texture);
                float distance = Vector3.Distance(camera.transform.position, center);
                float relativeHeight = worldSize / (2f * distance * tangent);
                int expected = -1;
                for (int i = 0; i < lods.Length; i++)
                    if (relativeHeight >= lods[i].screenRelativeTransitionHeight) { expected = i; break; }
                captures.Add(new Capture
                {
                    file = file, transition = label, stage = stage, frame = Time.frameCount,
                    unscaledTime = Time.unscaledTime,
                    secondsSinceCrossing = crossingTime < 0f ? -1f : Time.unscaledTime - crossingTime,
                    threshold = threshold, distance = distance,
                    estimatedScreenRelativeHeight = relativeHeight, expectedLod = expected,
                    cameraPosition = camera.transform.position,
                    rendererVisible = lods.Select(lod => lod.renderers.Any(renderer => renderer && renderer.isVisible)).ToArray()
                });
            }

            void Finish()
            {
                File.WriteAllText(Path.Combine(reportDirectory, "tree-lod-report.json"), JsonUtility.ToJson(report, true));
                Debug.Log("NATIVE_TREE_LOD_CAPTURE captures=" + report.captures.Length + " tree=" + report.treeName +
                    " groups=" + report.treeLodGroups + " passed=" + report.passed);
            }
        }
    }
}
