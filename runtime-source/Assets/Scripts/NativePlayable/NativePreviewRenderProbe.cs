using System;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativePreviewRenderProbe
    {
        [Serializable] public sealed class Values
        {
            public Color color, color2, dirtColor;
            public float speedIntensity, dirt, wheelDirt, wheelHeat, dirtColorEnabled;
        }
        [Serializable] public sealed class Target
        {
            public string sourceIdentity, rendererName;
            public int reflectionProbeUsage, lightProbeUsage, materialCount, expectedReflectionProbeUsage, expectedLightProbeUsage;
            public Values rendererBlock;
            public Values[] slotBlocks;
            public bool[] slotEmpty;
            public bool fieldsMatch;
        }
        [Serializable] public sealed class Component
        {
            public string sourceIdentity;
            public bool sourceEnabled, selectionOwner, sourceHasVehicleCharacter, shouldHaveStart, menuStartApplied, lastApplyWasStart;
            public int selectionRefreshCount;
            public Values expected;
            public Target[] targets;
        }
        [Serializable] public sealed class Manifest
        {
            public string sourceIdentity;
            public int expectedComponentCount, actualComponentCount, expectedRootOwnerCount, actualRootOwnerCount;
            public int[] expectedTargetCounts, actualTargetCounts;
            public string[] expectedComponentIdentities, actualComponentIdentities;
            public bool selectionContext, matches;
            public string[] rendererIdentities;
            public int[] expectedLightProbeUsage, actualLightProbeUsage, expectedReflectionProbeUsage, actualReflectionProbeUsage;
        }
        [Serializable] public sealed class EffectiveTexture
        {
            public string graphicsFormat, depthStencilFormat, stencilFormat, dimension, memoryless, vrUsage, shadowSamplingMode;
            public int msaaSamples, volumeDepth, mipCount, anisoLevel;
            public bool bindMS, enableRandomWrite, useMipMap, autoGenerateMips, useDynamicScale;
            public string filterMode, wrapModeU, wrapModeV, wrapModeW;
            public float mipMapBias;
        }
        [Serializable] public sealed class EffectiveCamera
        {
            public string configuredRenderingPath, clearFlags, gateFit;
            public bool allowMSAA, allowDynamicResolution, orthographic, usePhysicalProperties, useOcclusionCulling, enabled;
            public Color backgroundColor;
            public float aspect, orthographicSize, focalLength, depth, fieldOfView;
            public Vector2 sensorSize, lensShift;
            public Rect rect, pixelRect;
            public Matrix4x4 projectionMatrix, nonJitteredProjectionMatrix, gpuProjectionMatrix, worldToCameraMatrix;
            public int cullingMask, targetDisplay;
        }
        [Serializable] public sealed class EnvironmentObservation
        {
            public string domain;
            public bool present;
            public int ambientMode;
            public Color sky, equator, ground;
            public float ambientIntensity, reflectionIntensity;
            public float[] ambientProbe;
        }
        [Serializable] public sealed class Report
        {
            public string backend, actualRenderingPath, textureFormat;
            public bool hdr, srgb, cameraTextureBound, fieldCheckPassed;
            public int width, height, depth;
            public EffectiveTexture effectiveTexture;
            public EffectiveCamera effectiveCamera;
            public string loadedExportSourceScene, loadedExportSourceSHA256;
            public EnvironmentObservation nativeAtRender, nativeAtReport, originalLoadedSceneExport;
            public Component[] components;
            public Manifest[] manifests;
            public bool runtimeRenderingAccepted, visualParityAccepted;
        }
        public static string Capture(Camera camera, RenderTexture texture, GameObject model,
            NativeMenuEnvironment atRender, NativeSelectionPreviewDefinition exported)
        {
            if (!camera || !texture || !model) throw new InvalidOperationException("Selection preview has not rendered");
            var report = new Report
            {
                backend = SystemInfo.graphicsDeviceType.ToString(), actualRenderingPath = camera.actualRenderingPath.ToString(),
                hdr = camera.allowHDR, srgb = texture.sRGB, textureFormat = texture.format.ToString(),
                cameraTextureBound = camera.targetTexture == texture, width = texture.width, height = texture.height, depth = texture.depth,
                effectiveTexture = CaptureTexture(texture), effectiveCamera = CaptureCamera(camera),
                loadedExportSourceScene = exported == null ? null : exported.sourceScene,
                loadedExportSourceSHA256 = exported == null ? null : exported.sourceSHA256,
                nativeAtRender = CaptureEnvironment(atRender, "Native immediately before Camera.Render after preview environment Apply"),
                nativeAtReport = CaptureEnvironment(NativeMenuEnvironment.Capture(), "Native report-time environment after Render finally restoration"),
                originalLoadedSceneExport = CaptureEnvironment(exported == null ? null : exported.environment,
                    "Original selection scene loaded/exported by Native exporter; no original game runtime observation"),
                components = model.GetComponentsInChildren<NativePreviewKartProperties>(true).Select(CaptureComponent).ToArray(),
                manifests = model.GetComponentsInChildren<NativePreviewKartSourceManifest>(true).Select(CaptureManifest).ToArray()
            };
            report.fieldCheckPassed = report.cameraTextureBound && report.width == 512 && report.height == 512 && report.depth == 0
                && !report.srgb && report.manifests.Length == 2 && report.manifests.All(manifest => manifest.matches)
                && report.manifests.Count(manifest => manifest.selectionContext) == 1
                && report.components.All(component => (!component.shouldHaveStart || component.menuStartApplied)
                    && (!(component.shouldHaveStart || component.selectionRefreshCount > 0) || component.targets.All(target => target.fieldsMatch)));
            return JsonUtility.ToJson(report, true);
        }
        static EffectiveTexture CaptureTexture(RenderTexture texture)
        {
            var descriptor = texture.descriptor;
            return new EffectiveTexture
            {
                graphicsFormat = descriptor.graphicsFormat.ToString(), depthStencilFormat = descriptor.depthStencilFormat.ToString(),
                stencilFormat = descriptor.stencilFormat.ToString(), dimension = descriptor.dimension.ToString(),
                memoryless = descriptor.memoryless.ToString(), vrUsage = descriptor.vrUsage.ToString(),
                shadowSamplingMode = descriptor.shadowSamplingMode.ToString(), msaaSamples = descriptor.msaaSamples,
                volumeDepth = descriptor.volumeDepth, mipCount = descriptor.mipCount, bindMS = descriptor.bindMS,
                enableRandomWrite = descriptor.enableRandomWrite, useMipMap = descriptor.useMipMap,
                autoGenerateMips = descriptor.autoGenerateMips, useDynamicScale = descriptor.useDynamicScale,
                filterMode = texture.filterMode.ToString(), wrapModeU = texture.wrapModeU.ToString(),
                wrapModeV = texture.wrapModeV.ToString(), wrapModeW = texture.wrapModeW.ToString(),
                anisoLevel = texture.anisoLevel, mipMapBias = texture.mipMapBias
            };
        }
        static EffectiveCamera CaptureCamera(Camera camera)
        {
            return new EffectiveCamera
            {
                configuredRenderingPath = camera.renderingPath.ToString(), allowMSAA = camera.allowMSAA,
                allowDynamicResolution = camera.allowDynamicResolution, orthographic = camera.orthographic,
                orthographicSize = camera.orthographicSize, usePhysicalProperties = camera.usePhysicalProperties,
                sensorSize = camera.sensorSize, lensShift = camera.lensShift, focalLength = camera.focalLength,
                gateFit = camera.gateFit.ToString(), fieldOfView = camera.fieldOfView, aspect = camera.aspect, rect = camera.rect, pixelRect = camera.pixelRect,
                clearFlags = camera.clearFlags.ToString(), backgroundColor = camera.backgroundColor,
                projectionMatrix = camera.projectionMatrix, nonJitteredProjectionMatrix = camera.nonJitteredProjectionMatrix,
                gpuProjectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true),
                worldToCameraMatrix = camera.worldToCameraMatrix, depth = camera.depth,
                cullingMask = camera.cullingMask, targetDisplay = camera.targetDisplay,
                useOcclusionCulling = camera.useOcclusionCulling, enabled = camera.enabled
            };
        }
        static EnvironmentObservation CaptureEnvironment(NativeMenuEnvironment source, string domain)
        {
            var result = new EnvironmentObservation { domain = domain, present = source != null };
            if (source == null) return result;
            result.ambientMode = (int)source.ambientMode; result.sky = source.sky; result.equator = source.equator;
            result.ground = source.ground; result.ambientIntensity = source.ambientIntensity;
            result.reflectionIntensity = source.reflectionIntensity;
            result.ambientProbe = source.ambientProbe == null ? null : (float[])source.ambientProbe.Clone();
            return result;
        }
        static Manifest CaptureManifest(NativePreviewKartSourceManifest source)
        {
            var components = source.GetComponentsInChildren<NativePreviewKartProperties>(true);
            var roots = source.GetComponents<NativePreviewKartProperties>().Where(component => component.selectionOwner).ToArray();
            var expectedIds = source.componentIdentities ?? new string[0];
            var expectedCounts = source.targetCounts ?? new int[0];
            var record = new Manifest
            {
                sourceIdentity = source.sourceIdentity, selectionContext = source.selectionContext,
                expectedComponentCount = expectedIds.Length, actualComponentCount = components.Length,
                expectedRootOwnerCount = source.rootOwnerCount, actualRootOwnerCount = roots.Length,
                expectedComponentIdentities = expectedIds, actualComponentIdentities = components.Select(component => component.sourceIdentity).ToArray(),
                expectedTargetCounts = expectedCounts, actualTargetCounts = components.Select(component => component.targets == null ? -1 : component.targets.Length).ToArray()
            };
            record.matches = source.componentIdentities != null && source.targetCounts != null && expectedIds.Length == expectedCounts.Length
                && record.expectedComponentCount == record.actualComponentCount && record.expectedRootOwnerCount == record.actualRootOwnerCount;
            for (int index = 0; index < expectedIds.Length && record.matches; index++)
            {
                var matches = components.Where(component => component.sourceIdentity == expectedIds[index]).ToArray();
                record.matches &= matches.Length == 1 && matches[0].targets != null && matches[0].targets.Length == expectedCounts[index];
            }
            record.rendererIdentities = source.rendererIdentities;
            record.expectedLightProbeUsage = source.lightProbeUsage;
            record.expectedReflectionProbeUsage = source.reflectionProbeUsage;
            record.actualLightProbeUsage = source.renderers == null ? new int[0] : source.renderers.Select(renderer => renderer ? (int)renderer.lightProbeUsage : -1).ToArray();
            record.actualReflectionProbeUsage = source.renderers == null ? new int[0] : source.renderers.Select(renderer => renderer ? (int)renderer.reflectionProbeUsage : -1).ToArray();
            record.matches &= source.renderers != null && source.rendererIdentities != null && source.lightProbeUsage != null && source.reflectionProbeUsage != null
                && source.renderers.Length == source.rendererIdentities.Length && source.renderers.Length == source.lightProbeUsage.Length
                && source.renderers.Length == source.reflectionProbeUsage.Length;
            if (record.matches)
                record.matches &= record.actualLightProbeUsage.SequenceEqual(record.expectedLightProbeUsage)
                    && record.actualReflectionProbeUsage.SequenceEqual(record.expectedReflectionProbeUsage);
            if (record.selectionContext)
                record.matches &= roots.All(component => component.selectionRefreshCount == 1);
            return record;
        }
        static Component CaptureComponent(NativePreviewKartProperties properties)
        {
            if (properties.targets == null || properties.targetIdentities == null || properties.targets.Length != properties.targetIdentities.Length
                || properties.targetLightProbeUsage == null || properties.targetReflectionProbeUsage == null
                || properties.targets.Length != properties.targetLightProbeUsage.Length || properties.targets.Length != properties.targetReflectionProbeUsage.Length)
                throw new InvalidOperationException("Original CKC target metadata missing: " + properties.sourceIdentity);
            var expected = new Values
            {
                color = properties.color, color2 = properties.color2, dirtColor = properties.dirtColor,
                speedIntensity = properties.lastApplyWasStart ? 0f : properties.heat * 5f, dirt = properties.dirt, wheelDirt = properties.wheelDirt,
                wheelHeat = properties.lastApplyWasStart ? 0f : properties.wheelHeat, dirtColorEnabled = properties.dirtColorEnabled
            };
            var result = new Component
            {
                sourceIdentity = properties.sourceIdentity, sourceEnabled = properties.sourceEnabled, selectionOwner = properties.selectionOwner,
                sourceHasVehicleCharacter = properties.sourceHasVehicleCharacter,
                shouldHaveStart = properties.sourceEnabled && properties.gameObject.activeInHierarchy,
                menuStartApplied = properties.menuStartApplied, lastApplyWasStart = properties.lastApplyWasStart,
                selectionRefreshCount = properties.selectionRefreshCount,
                expected = expected, targets = new Target[properties.targets.Length]
            };
            var block = new MaterialPropertyBlock();
            for (int index = 0; index < properties.targets.Length; index++)
            {
                var renderer = properties.targets[index];
                if (!renderer) throw new InvalidOperationException("Original CKC target unresolved: " + properties.targetIdentities[index]);
                block.Clear(); renderer.GetPropertyBlock(block);
                var record = new Target
                {
                    sourceIdentity = properties.targetIdentities[index], rendererName = renderer.name,
                    expectedLightProbeUsage = properties.targetLightProbeUsage[index], expectedReflectionProbeUsage = properties.targetReflectionProbeUsage[index],
                    reflectionProbeUsage = (int)renderer.reflectionProbeUsage, lightProbeUsage = (int)renderer.lightProbeUsage,
                    materialCount = renderer.sharedMaterials.Length, rendererBlock = Read(block),
                    slotBlocks = new Values[renderer.sharedMaterials.Length], slotEmpty = new bool[renderer.sharedMaterials.Length]
                };
                for (int slot = 0; slot < record.materialCount; slot++)
                {
                    block.Clear(); renderer.GetPropertyBlock(block, slot);
                    record.slotBlocks[slot] = Read(block); record.slotEmpty[slot] = block.isEmpty;
                }
                record.fieldsMatch = Matches(record.rendererBlock, expected) && record.reflectionProbeUsage == record.expectedReflectionProbeUsage && record.lightProbeUsage == record.expectedLightProbeUsage
                    && record.slotEmpty.All(empty => empty);
                result.targets[index] = record;
            }
            return result;
        }
        static Values Read(MaterialPropertyBlock block)
        {
            return new Values
            {
                color = block.GetColor("_Color"), color2 = block.GetColor("_Color2"), dirtColor = block.GetColor("_DirtColor"),
                speedIntensity = block.GetFloat("_SpeedIntensity"), dirt = block.GetFloat("_DirtAmount"),
                wheelDirt = block.GetFloat("_WheelDirtAmount"), wheelHeat = block.GetFloat("_WheelHeatAmount"),
                dirtColorEnabled = block.GetFloat("_DirtColorEnabled")
            };
        }
        static bool Matches(Values actual, Values expected)
        {
            return actual.color.Equals(expected.color) && actual.color2.Equals(expected.color2) && actual.dirtColor.Equals(expected.dirtColor)
                && actual.speedIntensity == expected.speedIntensity && actual.dirt == expected.dirt && actual.wheelDirt == expected.wheelDirt
                && actual.wheelHeat == expected.wheelHeat && actual.dirtColorEnabled == expected.dirtColorEnabled;
        }
    }
}
