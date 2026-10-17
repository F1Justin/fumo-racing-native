using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        [Serializable] sealed class OriginalImageFields
        {
            public string key, spriteGuid;
            public long nodeId, sourceComponentId, spriteId;
            public int imageType, fillMethod, fillOrigin;
            public float fillAmount, pixelsPerUnitMultiplier;
            public bool imageEnabled, sourceActive, maskable, preserveAspect, fillClockwise, useSpriteMesh;
        }
        [Serializable] sealed class OriginalImageFieldSource
        {
            public int sourceImageCount, inactiveSourceImageCount, disabledSourceImageCount, emptySpriteImageCount;
            public OriginalImageFields[] rows;
        }
        [Serializable] sealed class OriginalImageFieldObservation
        {
            public string key;
            public OriginalImageFields source, initialized, current;
            public bool observed, initializationMatchesSource, currentActive, currentEnabled;
            public int currentSpriteInstanceId;
        }
        [Serializable] sealed class OriginalImageFieldReport
        {
            public bool passed, visualParityAccepted;
            public int sourceImageCount, observedImageCount, inactiveSourceImageCount, disabledSourceImageCount, emptySpriteImageCount;
            public string evidenceScope = "Immutable constructor observations for every authored Image, including inactive, disabled, empty Sprite and mask Images. Later dynamic fillAmount changes remain visible in current snapshots and do not rewrite initialization evidence.";
            public string[] missingKeys, unexpectedKeys, checks;
            public OriginalImageFieldObservation[] images;
        }
        OriginalImageFieldSource originalImageFieldSource;
        readonly Dictionary<string, OriginalImageFields> originalImageInitialized = new Dictionary<string, OriginalImageFields>();

        static OriginalImageFields ReadOriginalImageFields(string key, Image image) => new OriginalImageFields
        {
            key = key, imageType = (int)image.type, imageEnabled = image.enabled,
            sourceActive = image.gameObject.activeSelf, maskable = image.maskable, preserveAspect = image.preserveAspect,
            fillMethod = (int)image.fillMethod, fillOrigin = image.fillOrigin, fillAmount = image.fillAmount,
            fillClockwise = image.fillClockwise, useSpriteMesh = image.useSpriteMesh,
            pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier
        };

        internal void ObserveOriginalImageInitialization(string key, Image image)
        {
            if (originalImageInitialized.ContainsKey(key)) throw new InvalidOperationException("Duplicate original Image initialization: " + key);
            originalImageInitialized.Add(key, ReadOriginalImageFields(key, image));
        }

        static bool OriginalImageFieldsMatch(OriginalImageFields observed, OriginalImageFields source) =>
            observed.imageType == source.imageType && observed.imageEnabled == source.imageEnabled &&
            observed.maskable == source.maskable && observed.preserveAspect == source.preserveAspect &&
            observed.fillMethod == source.fillMethod && observed.fillOrigin == source.fillOrigin &&
            Mathf.Abs(observed.fillAmount - source.fillAmount) < .000001f &&
            observed.fillClockwise == source.fillClockwise && observed.useSpriteMesh == source.useSpriteMesh &&
            Mathf.Abs(observed.pixelsPerUnitMultiplier - source.pixelsPerUnitMultiplier) < .000001f;

        OriginalImageFieldReport OriginalImageInitializationReport()
        {
            if (originalImageFieldSource == null)
            {
                var source = NativeContent.Load<TextAsset>("NativeVerification/AuthoredImageFieldsSource");
                if (!source) throw new InvalidOperationException("Original Image rendering baseline missing");
                originalImageFieldSource = JsonUtility.FromJson<OriginalImageFieldSource>(source.text);
            }
            var baseline = originalImageFieldSource;
            var sourceKeys = new HashSet<string>(baseline.rows.Select(row => row.key));
            var missing = sourceKeys.Where(key => !originalImageInitialized.ContainsKey(key)).OrderBy(key => key).ToArray();
            var unexpected = originalImageInitialized.Keys.Where(key => !sourceKeys.Contains(key)).OrderBy(key => key).ToArray();
            var rows = baseline.rows.Select(source =>
            {
                bool observed = originalImageInitialized.TryGetValue(source.key, out var initialized);
                transforms.TryGetValue(source.key, out var target);
                var current = target ? target.GetComponent<Image>() : null;
                return new OriginalImageFieldObservation {
                    key = source.key, source = source, initialized = initialized, observed = observed,
                    initializationMatchesSource = observed && OriginalImageFieldsMatch(initialized, source),
                    current = current ? ReadOriginalImageFields(source.key, current) : null,
                    currentActive = current && current.gameObject.activeInHierarchy, currentEnabled = current && current.enabled,
                    currentSpriteInstanceId = current && current.sprite ? current.sprite.GetInstanceID() : 0
                };
            }).ToArray();
            return new OriginalImageFieldReport {
                passed = baseline.sourceImageCount == 823 && baseline.rows.Length == 823 && sourceKeys.Count == 823 &&
                    originalImageInitialized.Count == 823 && missing.Length == 0 && unexpected.Length == 0 &&
                    rows.All(row => row.initializationMatchesSource),
                visualParityAccepted = false, sourceImageCount = baseline.sourceImageCount,
                observedImageCount = originalImageInitialized.Count, inactiveSourceImageCount = baseline.inactiveSourceImageCount,
                disabledSourceImageCount = baseline.disabledSourceImageCount, emptySpriteImageCount = baseline.emptySpriteImageCount,
                missingKeys = missing, unexpectedKeys = unexpected, images = rows,
                checks = new[] {
                    (baseline.sourceImageCount == 823 && baseline.rows.Length == 823 && sourceKeys.Count == 823 ? "PASS" : "FAIL") + " source823Unique",
                    (originalImageInitialized.Count == 823 ? "PASS" : "FAIL") + " observed823",
                    (missing.Length == 0 && unexpected.Length == 0 ? "PASS" : "FAIL") + " missing0Unexpected0",
                    (rows.All(row => row.initializationMatchesSource) ? "PASS" : "FAIL") + " allInitializationFieldsMatchSource"
                }
            };
        }

        internal bool OriginalImageInitializationBound(out string detail)
        {
            var report = OriginalImageInitializationReport();
            detail = "original Image fields initialized=" + report.observedImageCount + "/" + report.sourceImageCount +
                "; matching=" + report.images.Count(row => row.initializationMatchesSource) +
                "; missing=" + report.missingKeys.Length + "; unexpected=" + report.unexpectedKeys.Length;
            return report.passed;
        }

        internal void WriteOriginalImageInitializationReport(string directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "authored-image-fields-runtime-report.json"), JsonUtility.ToJson(OriginalImageInitializationReport(), true));
        }
    }

    public sealed partial class NativeRaceHud
    {
        public bool OriginalImageInitializationBound(out string detail)
        {
            detail = "Authored UI has not been constructed";
            return authoredUi != null && authoredUi.OriginalImageInitializationBound(out detail);
        }
        public void WriteOriginalImageInitializationReport(string directory)
        {
            if (authoredUi == null) throw new InvalidOperationException("Authored UI has not been constructed");
            authoredUi.WriteOriginalImageInitializationReport(directory);
        }
    }
}
