using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class MinimapSourceSet { public string bundleSHA256; public MinimapSource[] sprites; }
        [Serializable] sealed class MinimapSource
        {
            public string id, vertexHash, triangleHash;
            public Rect logicalRect, textureRect;
            public Vector2 pivot, sourceOffset, textureRectOffset;
            public Vector2[] vertices;
            public int[] triangles;
        }
        [Serializable] sealed class MinimapLiveState
        {
            public string id, screenshot, vertexHash, triangleHash, totalText;
            public bool active, preserveAspect, useSpriteMesh, recordPanelActive;
            public int frame, vertexCount, indexCount, imageType;
            public Rect logicalRect, textureRect;
            public Vector2 pivotPixels, textureRectOffset, size, anchoredPosition;
            public Vector3 scale;
            public Vector3[] worldCorners;
            public Bounds bounds;
            public string[] lapTexts;
        }
        [Serializable] sealed class MinimapVerificationReport
        {
            public bool passed, visualParityAccepted;
            public string sourceBundleSHA256;
            public string evidenceScope = "Four original Tight Sprite resources and production TT circuit UI; rendered region acceptance remains separate. Sprite source m_Offset is stored in baseline; runtime API exposes textureRectOffset, pivot, bounds and vertices.";
            public string renderBinding = "Each selected map remains unchanged through two complete render frames before screenshot and UI sampling";
            public string[] checks;
            public MinimapLiveState[] states;
        }
        static string MinimapHash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        static string MinimapVertexHash(Vector2[] values)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            { foreach (var value in values) { writer.Write(value.x); writer.Write(value.y); } return MinimapHash(stream.ToArray()); }
        }
        static string MinimapIndexHash(ushort[] values)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            { foreach (var value in values) writer.Write(value); return MinimapHash(stream.ToArray()); }
        }
        static Transform MinimapAuthoredNode(string path)
        {
            var root = GameObject.Find("Original authored menus");
            var canvas = root ? root.transform.Find("selection authored Canvas") : null;
            return canvas ? canvas.Find(path) : null;
        }
        static IEnumerator MinimapSettle()
        {
            for (int i = 0; i < 2; i++) { yield return null; Canvas.ForceUpdateCanvases(); yield return new WaitForEndOfFrame(); }
        }
        internal IEnumerator CheckCircuitMinimapsLive()
        {
            QualitySettings.vSyncCount = 0;
            var checks = new List<string>(); var states = new List<MinimapLiveState>();
            bool passed = true;
            Action<bool, string> require = (ok, text) => { passed &= ok; checks.Add((ok ? "PASS " : "FAIL ") + text); Debug.Log("NATIVE_CIRCUIT_MINIMAP_CHECK " + ok + " " + text); };
            var baselineAsset = NativeContent.Load<TextAsset>("NativeVerification/CircuitMinimapSource");
            if (!baselineAsset) throw new InvalidOperationException("Original minimap baseline resource missing");
            var baseline = JsonUtility.FromJson<MinimapSourceSet>(baselineAsset.text);
            var savedRecords = NativeSaveStore.Data.timeTrialRecords;
            var savedSettings = NativeSaveStore.Data.settings;
            try
            {
                Directory.CreateDirectory(reportDirectory);
                Screen.SetResolution(1280, 800, FullScreenMode.Windowed);
                float deadline = Time.realtimeSinceStartup + 5;
                while ((Screen.width != 1280 || Screen.height != 800) && Time.realtimeSinceStartup < deadline) yield return null;
                require(Screen.width == 1280 && Screen.height == 800, "1280x800 framebuffer");
                hud.SuppressPointerInput = true;
                NativeSaveStore.Data.settings = savedSettings.Where(s => s.key != "lang_game").Concat(new[] { new NativeSaveData.Setting { key = "lang_game", value = "0", type = "int" } }).ToArray();
                NativeSaveStore.Data.timeTrialRecords = new[] { new NativeSaveData.TimeTrialRecord {
                    trackId = "forest", characterId = "reimu", vehicleId = "kart", bestSeconds = 96.504,
                    lapSeconds = new[] {20.553,18.600,18.833,19.351,19.167}, lapCount = 5, source = "original-TT-circuit-minimap-fixture" } };
                phase = Phase.Menu; selectedMode = NativeSessionMode.TimeTrials;
                selectedTrackId = "forest"; selectedCharacterIndex = vehicleRoster.CharacterIndexOf("reimu");
                selectedVehicleIndex = vehicleRoster.VehicleIndexOf("kart"); selectedSkinIndex = 0;
                OpenModeCup(); deadline = Time.realtimeSinceStartup + 5;
                while (hud.MenuTransitionActive && Time.realtimeSinceStartup < deadline) yield return null;
                ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Cups));
                foreach (string id in new[] { "forest", "mountains", "purplefield", "wintermistylake" })
                {
                    selectedTrackId = id; hud.SelectMenuIndex(NativeTrackCatalog.IndexOf(id));
                    deadline = Time.realtimeSinceStartup + 5;
                    while (hud.MenuTransitionActive && Time.realtimeSinceStartup < deadline) yield return null;
                    yield return MinimapSettle();
                    var source = baseline.sprites.Single(s => s.id == id);
                    var resource = NativeContent.Load<Sprite>("NativeSelectionPreview/" + id + "-minimap");
                    require(resource, id + " production resource exists");
                    if (!resource) continue;
                    var node = MinimapAuthoredNode("CupSelection/Minimap");
                    var image = node ? node.GetComponent<Image>() : null;
                    require(image && image.sprite == resource && image.gameObject.activeInHierarchy, id + " production Image binds resource");
                    if (!image) continue;
                    var rect = (RectTransform)node; var vertices = resource.vertices; var indices = resource.triangles;
                    string vertexHash = MinimapVertexHash(vertices), triangleHash = MinimapIndexHash(indices);
                    require(resource.rect == source.logicalRect && resource.pivot == source.pivot * 512 &&
                        resource.texture.width == 512 && resource.texture.height == 512, id + " original logical512 and center pivot");
                    require(Vector2.Distance(resource.textureRectOffset, source.textureRectOffset) < .0001f &&
                        resource.textureRect == source.textureRect, id + " original tight rect and padding offset");
                    require(vertices.Length == source.vertices.Length && vertices.Zip(source.vertices, (a,b) => a == b).All(v => v) &&
                        indices.Length == source.triangles.Length && indices.Select(v => (int)v).SequenceEqual(source.triangles) &&
                        vertexHash == source.vertexHash && triangleHash == source.triangleHash, id + " original Tight vertices and indices exact values/hash");
                    var definition = hud.authoredUiData.nodes.Single(n => n.scene == "selection" && n.path == "Canvas/CupSelection/Minimap");
                    require(rect.rect.size == new Vector2(600,600) && rect.anchoredPosition == new Vector2(-600,132) &&
                        rect.localScale == Vector3.one && !image.preserveAspect && !image.useSpriteMesh && image.type == Image.Type.Simple &&
                        image.material == (definition.imageOverride ? definition.imageOverride : hud.uiImageMaterial ? hud.uiImageMaterial : Graphic.defaultGraphicMaterial) && definition.size == rect.sizeDelta, id + " actual authored600 UI geometry and source Image flags");
                    var panel = MinimapAuthoredNode("CupSelection/TimePanel");
                    var total = panel ? panel.Find("TimeTotal").GetComponent<TMP_Text>() : null;
                    var laps = new string[5];
                    for (int i = 0; i < 5; i++) laps[i] = panel.Find("lap" + (i+1) + "time").GetComponent<TMP_Text>().text;
                    require(panel.gameObject.activeInHierarchy == (id == "forest"), id + " matching original record panel eligibility");
                    if (id == "forest") require(total.text == "<mspace=24>01:36.504</mspace>" && laps.SequenceEqual(new[] {
                        "<mspace=16>00:20.553</mspace>", "<mspace=16>00:18.600</mspace>", "<mspace=16>00:18.833</mspace>", "<mspace=16>00:19.351</mspace>", "<mspace=16>00:19.167</mspace>" }), "Forest actual TT record text");
                    var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                    string file = "circuit-minimap-" + id + ".png";
                    var texture = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(reportDirectory,file), texture.EncodeToPNG()); Destroy(texture);
                    states.Add(new MinimapLiveState { id=id, screenshot=file, frame=Time.frameCount, active=image.gameObject.activeInHierarchy,
                        logicalRect=resource.rect, textureRect=resource.textureRect, pivotPixels=resource.pivot, textureRectOffset=resource.textureRectOffset,
                        vertexCount=vertices.Length, indexCount=indices.Length, vertexHash=vertexHash, triangleHash=triangleHash, bounds=resource.bounds,
                        size=rect.rect.size, anchoredPosition=rect.anchoredPosition, scale=rect.localScale, worldCorners=corners,
                        preserveAspect=image.preserveAspect, useSpriteMesh=image.useSpriteMesh, imageType=(int)image.type,
                        recordPanelActive=panel.gameObject.activeInHierarchy,totalText=total.text,lapTexts=laps });
                }
            }
            finally { NativeSaveStore.Data.timeTrialRecords=savedRecords; NativeSaveStore.Data.settings=savedSettings; }
            File.WriteAllText(Path.Combine(reportDirectory,"circuit-minimap-verification.json"),JsonUtility.ToJson(new MinimapVerificationReport {
                passed=passed, visualParityAccepted=false, sourceBundleSHA256=baseline.bundleSHA256, checks=checks.ToArray(), states=states.ToArray() },true));
            Debug.Log("NATIVE_CIRCUIT_MINIMAP_VERIFICATION " + passed); Application.Quit(passed ? 0 : 2);
        }
    }
}
