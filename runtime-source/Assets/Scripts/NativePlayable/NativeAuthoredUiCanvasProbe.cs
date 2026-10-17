using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed class NativeAuthoredUiCanvasProbe : MonoBehaviour
    {
        [Serializable] sealed class CanvasRecord
        {
            public string path, rootCanvas, renderMode, sourceRenderMode;
            public int sortingOrder, sourceSortingOrder, expectedSortingOrder, sortingLayer, sourceSortingLayer, renderOrder;
            public bool active, enabled, overrideSorting, sourceOverrideSorting, isRootCanvas, sortingFieldsMatch, renderModeMatches;
        }
        [Serializable] sealed class GraphicRecord
        {
            public string path, canvas, rootCanvas, material, shader, renderingMaterial, renderingShader, sprite, texture;
            public int depth, absoluteDepth, relativeDepth, canvasOrder;
            public bool active, enabled, culled, maskable;
            public Color vertexColor, materialColor, renderingMaterialColor;
        }
        [Serializable] sealed class Report
        {
            public string source = "Actual Unity Player Canvas and CanvasRenderer after selection end-of-frame; no render state is modified";
            public bool sourceCanvasFieldsMatch, sourceRenderModesMatch;
            public int frame;
            public CanvasRecord[] canvases;
            public GraphicRecord[] graphics;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Environment.GetCommandLineArgs().Contains("--menu-parity-self-test")) return;
            var go = new GameObject("Authored UI Canvas runtime probe");
            DontDestroyOnLoad(go);
            go.AddComponent<NativeAuthoredUiCanvasProbe>();
        }
        IEnumerator Start()
        {
            Transform sceneRoot = null;
            for (int i = 0; i < 3600; i++)
            {
                sceneRoot = Resources.FindObjectsOfTypeAll<Canvas>().Where(c => c && c.name == "selection authored Canvas" && c.gameObject.scene.IsValid()).Select(c => c.transform).FirstOrDefault();
                var selection = sceneRoot ? sceneRoot.Find("CharSelection") : null;
                if (selection && selection.gameObject.activeInHierarchy) break;
                sceneRoot = null;
                yield return null;
            }
            if (!sceneRoot) { Debug.LogError("NATIVE_CANVAS_PROBE selection did not become visible"); yield break; }
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            var data = NativeContent.Load<NativeAuthoredUiData>("NativeMenu/AuthoredUi");
            if (!data) { Debug.LogError("NATIVE_CANVAS_PROBE authored source asset missing"); yield break; }
            var sourceRoot = data.nodes.Single(n => n.scene == "selection" && n.path == "Canvas");
            var rootCanvas = sceneRoot.GetComponent<Canvas>();
            var canvasRecords = new List<CanvasRecord>();
            foreach (var source in data.nodes.Where(n => n.scene == "selection" && n.canvas))
            {
                var target = source.path == "Canvas" ? sceneRoot : sceneRoot.Find(source.path.Substring("Canvas/".Length));
                var canvas = target ? target.GetComponent<Canvas>() : null;
                if (!canvas) { canvasRecords.Add(new CanvasRecord { path = source.path }); continue; }
                int expected = rootCanvas.sortingOrder + source.canvasSortingOrder - sourceRoot.canvasSortingOrder;
                canvasRecords.Add(new CanvasRecord {
                    path = source.path, active = canvas.gameObject.activeInHierarchy, enabled = canvas.enabled,
                    rootCanvas = canvas.rootCanvas ? canvas.rootCanvas.name : "null", isRootCanvas = canvas.isRootCanvas,
                    renderMode = canvas.renderMode.ToString(), sourceRenderMode = ((RenderMode)source.canvasRenderMode).ToString(),
                    renderModeMatches = (int)canvas.renderMode == source.canvasRenderMode,
                    sortingOrder = canvas.sortingOrder, expectedSortingOrder = expected, sourceSortingOrder = source.canvasSortingOrder,
                    sortingLayer = canvas.sortingLayerID, sourceSortingLayer = source.canvasSortingLayerId,
                    overrideSorting = canvas.overrideSorting, sourceOverrideSorting = source.canvasOverrideSorting, renderOrder = canvas.renderOrder,
                    sortingFieldsMatch = canvas.sortingOrder == expected && canvas.sortingLayerID == source.canvasSortingLayerId && canvas.overrideSorting == source.canvasOverrideSorting && canvas.enabled == source.canvasEnabled });
            }
            var graphics = new List<GraphicRecord>();
            foreach (var graphic in sceneRoot.GetComponentsInChildren<Graphic>(true))
            {
                string path = graphic.name;
                for (var parent = graphic.transform.parent; parent && parent != sceneRoot; parent = parent.parent) path = parent.name + "/" + path;
                path = "Canvas/" + path;
                if (!path.Contains("P1Box") && !path.Contains("P2Box") && !path.Contains("Background") && path != "Canvas/bg" && path != "Canvas/testbg") continue;
                var material = graphic.material; var rendered = graphic.materialForRendering;
                var image = graphic as Image;
                graphics.Add(new GraphicRecord {
                    path = path, active = graphic.gameObject.activeInHierarchy, enabled = graphic.enabled,
                    canvas = graphic.canvas ? graphic.canvas.name : "null", rootCanvas = graphic.canvas && graphic.canvas.rootCanvas ? graphic.canvas.rootCanvas.name : "null",
                    canvasOrder = graphic.canvas ? graphic.canvas.sortingOrder : int.MinValue,
                    depth = graphic.depth, absoluteDepth = graphic.canvasRenderer.absoluteDepth, relativeDepth = graphic.canvasRenderer.relativeDepth,
                    culled = graphic.canvasRenderer.cull, maskable = graphic is MaskableGraphic mask && mask.maskable,
                    vertexColor = graphic.color, material = material ? material.name : "null", shader = material && material.shader ? material.shader.name : "null",
                    renderingMaterial = rendered ? rendered.name : "null", renderingShader = rendered && rendered.shader ? rendered.shader.name : "null",
                    materialColor = material && material.HasProperty("_Color") ? material.GetColor("_Color") : Color.clear,
                    renderingMaterialColor = rendered && rendered.HasProperty("_Color") ? rendered.GetColor("_Color") : Color.clear,
                    sprite = image && image.sprite ? image.sprite.name : "null", texture = graphic.mainTexture ? graphic.mainTexture.name : "null" });
            }
            var report = new Report { frame = Time.frameCount, canvases = canvasRecords.ToArray(), graphics = graphics.ToArray(),
                sourceCanvasFieldsMatch = canvasRecords.All(c => c.sortingFieldsMatch), sourceRenderModesMatch = canvasRecords.All(c => c.renderModeMatches) };
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--report-dir");
            string directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "verification");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "canvas-source-runtime-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_CANVAS_PROBE source_fields=" + report.sourceCanvasFieldsMatch + " source_render_modes=" + report.sourceRenderModesMatch + " canvases=" + canvasRecords.Count + " graphics=" + graphics.Count);
        }
    }
}
