using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeVisualParityVerification
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Report { public string trackId, scope; public bool passed; public Check[] checks; }
        public static IEnumerator Run(NativeRace race, string directory, bool includeRoster = true)
        {
            Directory.CreateDirectory(directory);
            race.SetVisualCapture("race", false);
            var checks = new List<Check>();
            void Add(string name, bool passed, string detail) => checks.Add(new Check { name = name, passed = passed, detail = detail });
            var expected = race.menuPresentation.raceEnvironment;
            Add("original-race-ambient", RenderSettings.ambientMode == expected.ambientMode && RenderSettings.ambientSkyColor == expected.sky && RenderSettings.ambientEquatorColor == expected.equator && RenderSettings.ambientGroundColor == expected.ground && RenderSettings.ambientIntensity == expected.ambientIntensity, "source mode=" + expected.ambientMode);
            Add("original-race-fog", RenderSettings.fog == expected.fog && RenderSettings.fogMode == expected.fogMode && RenderSettings.fogColor == expected.fogColor && RenderSettings.fogDensity == expected.fogDensity && RenderSettings.fogStartDistance == expected.fogStart && RenderSettings.fogEndDistance == expected.fogEnd, "source density=" + expected.fogDensity);
            Add("original-race-reflection", RenderSettings.reflectionIntensity == expected.reflectionIntensity && RenderSettings.skybox == expected.skybox, "source reflection=" + expected.reflectionIntensity);
            int mapCount = race.trackId == "forest" || race.trackId == "wintermistylake" ? 3 : race.trackId == "mountains" ? 2 : 0;
            int probeCount = race.trackId == "forest" ? 1893 : race.trackId == "mountains" ? 4299 : race.trackId == "wintermistylake" ? 5990 : 0;
            Add("original-baked-lighting", LightmapSettings.lightmaps.Length == mapCount && LightmapSettings.lightmaps.All(m => m.lightmapColor && m.lightmapDir && m.shadowMask), "source maps=" + mapCount);
            Add("original-light-probes", probeCount == 0 ? !LightmapSettings.lightProbes || LightmapSettings.lightProbes.count == 0 : LightmapSettings.lightProbes && LightmapSettings.lightProbes.count == probeCount, "source probes=" + probeCount);
            var roster = race.vehicleRoster;
            var player = race.racers[0];
            bool savedEnabled = race.enabled;
            race.enabled = false;
            var sheet = new Texture2D(320 * roster.VehicleCount, 180 * roster.CharacterCount, TextureFormat.RGB24, false);
            try
            {
                for (int character = 0; includeRoster && character < roster.CharacterCount; character++)
                    for (int vehicle = 0; vehicle < roster.VehicleCount; vehicle++)
                    {
                        roster.ApplySelection(character, vehicle, player);
                        yield return null;
                        var kart = player.driftPivot.Find("AnimPivot/Kart");
                        var active = kart.GetComponentsInChildren<Renderer>().Where(r => r.gameObject.activeInHierarchy).ToArray();
                        CaptureCell(race.raceCamera, player.transform, sheet, vehicle * 320, (roster.CharacterCount - 1 - character) * 180);
                        string id = roster.CharacterNameAt(character) + "/" + roster.VehicleNameAt(vehicle);
                        Add("roster-renderers-" + id, active.Length > 0 && active.All(r => r.sharedMaterials.All(m => m && m.shader && m.shader.isSupported)), "renderers=" + active.Length);
                        foreach (var renderer in active)
                            for (int slot = 0; slot < renderer.sharedMaterials.Length; slot++)
                            {
                                var material = renderer.sharedMaterials[slot];
                                if (!material || !material.HasProperty("_KartMask") || material.GetFloat("_KartMask") < .5f) continue;
                                var block = new MaterialPropertyBlock();
                                renderer.GetPropertyBlock(block, slot);
                                var source = roster.characters[character];
                                Add("roster-source-paint-" + id + "/" + renderer.name + "/" + slot,
                                    block.GetColor("_Color") == source.kartColorA && block.GetColor("_Color2") == source.kartColorB,
                                    "original Racer.ModelInit applies CharacterData colors to each ColoredKartComponent");
                            }
                    }
                sheet.Apply();
                if(includeRoster) File.WriteAllBytes(Path.Combine(directory, "visual-roster-24x7.png"), sheet.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.Destroy(sheet);
                roster.ApplySelection(roster.DefaultCharacterIndex, 0, player);
                race.enabled = savedEnabled;
            }
            var states = UnityEngine.Object.FindObjectsOfType<NativeWaterSourceState>();
            Add("source-water-material-count", states.Length == (race.trackId == "mountains" ? 4 : race.trackId == "wintermistylake" ? 2 : race.trackId == "purplefield" ? 1 : 0), "actual=" + states.Length);
            foreach (var state in states)
            {
                var material = state.material;
                Add("water-shader-" + state.name, material && material.shader.name == state.expectedShader && material.shader.isSupported, state.sourcePath + " sha256=" + state.sourceSHA256);
                if (!material) continue;
                Add("water-queue-keywords-" + state.name, material.renderQueue == state.renderQueue && material.shaderKeywords.OrderBy(x => x).SequenceEqual(state.keywords.OrderBy(x => x)), "queue=" + material.renderQueue);
                foreach (var scalar in state.scalars) Add("water-" + scalar.name, material.HasProperty(scalar.name) && material.GetFloat(scalar.name) == scalar.value, "source=" + scalar.value);
                foreach (var tint in state.tints) Add("water-" + tint.name, material.HasProperty(tint.name) && material.GetColor(tint.name) == tint.value, "source=" + tint.value);
                foreach (var image in state.images) Add("water-" + image.name, material.HasProperty(image.name) && material.GetTexture(image.name) == image.value && material.GetTextureScale(image.name) == image.scale && material.GetTextureOffset(image.name) == image.offset, "source=" + (image.value ? image.value.name : "null"));
            }
            var flows = UnityEngine.Object.FindObjectsOfType<NativeWaterFlow>(true);
            Add("source-water-flow-count", race.trackId == "forest" || race.trackId == "purplefield" ? flows.Length == 0 : flows.Length > 0, "actual=" + flows.Length);
            if (flows.Any(flow => flow.isActiveAndEnabled))
            {
                float savedScale = Time.timeScale;
                Time.timeScale = 1f;
                float before = Shader.GetGlobalFloat("_WaterLocalUvX");
                yield return new WaitForSecondsRealtime(.25f);
                float after = Shader.GetGlobalFloat("_WaterLocalUvX");
                Time.timeScale = savedScale;
                Add("water-real-update-flow", before != after && !float.IsNaN(after), "before=" + before + " after=" + after);
            }
            File.WriteAllText(Path.Combine(directory, "visual-source-state.json"), JsonUtility.ToJson(new Report { trackId = race.trackId, scope = "Original serialized water parameters and live UV updates. Rendered original/native parity uses separate matched-camera captures.", passed = checks.All(c => c.passed), checks = checks.ToArray() }, true));
        }
        static void CaptureCell(Camera camera, Transform player, Texture2D sheet, int x, int y)
        {
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(320, 180, 24);
            try
            {
                camera.transform.position = player.TransformPoint(3f, 2.1f, -3.5f);
                camera.transform.LookAt(player.position + Vector3.up * 1.2f);
                camera.fieldOfView = 40f;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                sheet.ReadPixels(new Rect(0, 0, 320, 180), x, y, false);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                RenderTexture.active = previousActive;
                UnityEngine.Object.Destroy(target);
            }
        }
    }
}
