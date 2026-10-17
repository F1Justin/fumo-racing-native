using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Fumo.NativePlayable;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Only installed into isolated producer projects by producer.py.
public static class NativeContentBuild
{
    static readonly string[] ScenePaths = {
        "Assets/NativePlayableGenerated/ForestRace.unity", "Assets/NativePlayableGenerated/MountainsRace.unity",
        "Assets/NativePlayableGenerated/PurplefieldRace.unity", "Assets/NativePlayableGenerated/WinterMistyLakeRace.unity"
    };
    static readonly string[] TrackIds = { "forest", "mountains", "purplefield", "wintermistylake" };
    static string Output => Required("FUMO_DISTRIBUTION_OUTPUT");
    static string Platform => Required("FUMO_DISTRIBUTION_PLATFORM");
    static BuildTarget Target => Platform == "macos" ? BuildTarget.StandaloneOSX : Platform == "windows"
        ? BuildTarget.StandaloneWindows64 : Platform == "linux" ? BuildTarget.StandaloneLinux64 : throw new Exception("Unsupported platform");
    static string Required(string key) => Environment.GetEnvironmentVariable(key) ?? throw new Exception("Missing " + key);
    static string Hash(string file)
    {
        using (var input = File.OpenRead(file)) using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }
    static void Write(string path, object value) => File.WriteAllText(path, JsonUtility.ToJson(value, true) + "\n");
    static void CheckIsolation(string role)
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string workspace = Path.GetFullPath(Required("FUMO_DISTRIBUTION_WORKSPACE"));
        if (!project.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(Path.Combine(project, ".fumo-distribution-" + role)))
            throw new InvalidOperationException("Refusing to modify a project without the isolated distribution marker");
        if (Application.unityVersion != "6000.3.25f1") throw new InvalidOperationException("Locked Unity version is required");
        if (EditorUserBuildSettings.activeBuildTarget != Target) throw new InvalidOperationException("Start Unity with matching -buildTarget");
    }

    [Serializable] sealed class SourceObject { public string name, type, guid, localId, hierarchy; }
    [Serializable] sealed class SourceAsset
    {
        public string path, guid, sha256, bundle;
        public string[] dependencies;
        public SourceObject[] objects;
    }
    [Serializable] sealed class SourceIndex { public int schema = 1; public string scope = "PRIVATE reference inputs; never distribute"; public SourceAsset[] assets; }
    [Serializable] sealed class ResourceMove { public string before, after; }
    [Serializable] sealed class ContentPlan { public ResourceMove[] moves; public NativeContent.ResourceEntry[] resources; }
    [Serializable] sealed class BuiltinEvidence
    {
        public string engineVersion, editorPath, builtinResource, assetPath, textureName, guid, localId, format, rawSHA256, rawError;
        public int width, height;
        public bool readable;
    }
    [Serializable] sealed class BuildReceipt
    {
        public int schema = 1;
        public string role, engineVersion, platform, player, result, contentManifestSHA256;
        public bool publicDistributionAccepted, originalPackageImportVerified, sourceProvenanceReviewed;
        public int resources, scenes, bundles, batchFlagsCleared, uncompressedTextures;
        public string[] publicPlayerAssetPaths, packedAssetPaths;
        public ResourceMove[] movedResources;
    }

    public static void BuildPrivateContent()
    {
        CheckIsolation("content");
        string content = Path.Combine(Output, "private-reference", "Content");
        Directory.CreateDirectory(content);
        var moves = new List<ResourceMove>();
        var catalogResources = new List<NativeContent.ResourceEntry>();
        string planPath = Path.Combine(Output, "private-reference", "content-plan.json");
        if (File.Exists(planPath))
        {
            var previous = JsonUtility.FromJson<ContentPlan>(File.ReadAllText(planPath));
            moves.AddRange(previous.moves); catalogResources.AddRange(previous.resources);
        }
        var resourceFolders = Directory.GetDirectories("Assets", "Resources", SearchOption.AllDirectories)
            .Select(p => p.Replace('\\', '/')).OrderByDescending(p => p.Length).ToArray();
        foreach (string folder in resourceFolders)
        {
            string guid = AssetDatabase.AssetPathToGUID(folder);
            string destination = "Assets/FumoExternalContent/Resources-" + guid;
            EnsureFolder("Assets/FumoExternalContent");
            var assets = AssetDatabase.FindAssets("", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !AssetDatabase.IsValidFolder(p) && IsContentAsset(p)).Distinct().OrderBy(p => p).ToArray();
            foreach (string path in assets)
            {
                string relative = path.Substring(folder.Length + 1);
                catalogResources.Add(new NativeContent.ResourceEntry { key = relative.Substring(0, relative.Length - Path.GetExtension(relative).Length),
                    bundle = "resources.bundle", asset = (destination + "/" + relative).ToLowerInvariant() });
            }
            string error = AssetDatabase.MoveAsset(folder, destination);
            if (error.Length != 0) throw new InvalidOperationException(error);
            moves.Add(new ResourceMove { before = folder, after = destination });
        }
        if (catalogResources.Select(r => r.key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalogResources.Count)
            throw new InvalidOperationException("Duplicate Resources keys cannot be represented without ambiguity");
        Write(planPath, new ContentPlan { moves = moves.ToArray(), resources = catalogResources.ToArray() });

        // Keep originals' transform/renderer identity. Generated combined meshes obscure source provenance.
        int cleared = 0;
        foreach (string path in ScenePaths)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            foreach (var capture in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<NativeMenuPostProcessingSource>(true)))
            {
                // Passive editor provenance captures are not consumed by runtime.
                // Live PostProcessVolume components keep the actual configuration.
                capture.layerJson = ""; capture.volumes = Array.Empty<NativeMenuPostProcessingVolumeSource>();
            }
            foreach (var transform in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Transform>(true)))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(transform.gameObject);
                if ((flags & StaticEditorFlags.BatchingStatic) == 0) continue;
                GameObjectUtility.SetStaticEditorFlags(transform.gameObject, flags & ~StaticEditorFlags.BatchingStatic); cleared++;
            }
            EditorSceneManager.SaveScene(scene);
        }
        PlayerSettings.bakeCollisionMeshes = false;
        int textures = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
            if (!importer) continue;
            var prior = importer.GetPlatformTextureSettings("Standalone");
            if (importer.textureCompression == TextureImporterCompression.Uncompressed && !importer.crunchedCompression
                && prior.overridden && prior.format == TextureImporterFormat.RGBA32 && !prior.crunchedCompression) continue;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            var setting = importer.GetPlatformTextureSettings("Standalone");
            setting.name = "Standalone"; setting.overridden = true; setting.format = TextureImporterFormat.RGBA32;
            setting.textureCompression = TextureImporterCompression.Uncompressed; setting.crunchedCompression = false;
            importer.SetPlatformTextureSettings(setting);
            importer.SaveAndReimport(); textures++;
        }
        AssetDatabase.SaveAssets();
        // Asset names are lowercased in the catalog; map them back to filesystem casing for BuildPipeline.
        var allPaths = AssetDatabase.GetAllAssetPaths().ToDictionary(p => p.ToLowerInvariant(), p => p, StringComparer.Ordinal);
        var resourceAssets = catalogResources.Select(r => allPaths[r.asset]).Distinct().ToArray();
        var rootAssets = new HashSet<string>(ScenePaths.Concat(resourceAssets), StringComparer.Ordinal);
        var common = AssetDatabase.GetDependencies(rootAssets.ToArray(), true)
            .Where(IsContentAsset).Where(p => !rootAssets.Contains(p)).Distinct().OrderBy(p => p).ToArray();
        var assignments = new List<AssetBundleBuild> {
            new AssetBundleBuild { assetBundleName = "resources.bundle", assetNames = resourceAssets },
            new AssetBundleBuild { assetBundleName = "shared.bundle", assetNames = common }
        };
        for (int i = 0; i < ScenePaths.Length; i++) assignments.Add(new AssetBundleBuild {
            assetBundleName = "scene-" + TrackIds[i] + ".bundle", assetNames = new[] { ScenePaths[i] }
        });
        assignments.RemoveAll(a => a.assetNames.Length == 0);
        ExportSources(assignments, Path.Combine(Output, "private-reference", "source-asset-index.json"));
        // Keep type trees and engine version; importer reconstruction requires the complete schema.
        var built = BuildPipeline.BuildAssetBundles(content, assignments.ToArray(),
            BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.ForceRebuildAssetBundle
            | BuildAssetBundleOptions.StrictMode, Target);
        if (!built) throw new InvalidOperationException("Private reference content build failed");
        var names = built.GetAllAssetBundles().OrderBy(n => n).ToArray();
        var catalog = new NativeContent.Catalog { schema = 1, resources = catalogResources.OrderBy(r => r.key).ToArray(),
            bundles = names.Select(n => new NativeContent.BundleDefinition { file = n, scene = n.StartsWith("scene-"),
                dependencies = built.GetDirectDependencies(n).OrderBy(x => x).ToArray() }).ToArray() };
        string catalogPath = Path.Combine(content, "catalog.json"); Write(catalogPath, catalog);
        var manifest = new NativeContent.InstallManifest { schema = 1, contentId = "honkyhood-021-thfr-" + Required("FUMO_DISTRIBUTION_VERSION"),
            engineVersion = Application.unityVersion, platform = Platform, initialScene = ScenePaths[0], catalogFile = "catalog.json",
            catalogSHA256 = Hash(catalogPath), bundles = names.Select(n => new NativeContent.BundleEntry {
                file = n, sha256 = Hash(Path.Combine(content, n)), bytes = new FileInfo(Path.Combine(content, n)).Length }).ToArray(),
            scenes = ScenePaths.Select((p, i) => new NativeContent.SceneEntry { id = TrackIds[i], path = p, bundle = "scene-" + TrackIds[i] + ".bundle" }).ToArray() };
        Write(Path.Combine(content, "install-manifest.json"), manifest);
        Write(Path.Combine(Output, "private-reference", "build-receipt.json"), new BuildReceipt { role = "private-reference-content",
            engineVersion = Application.unityVersion, platform = Platform, result = "passed", resources = catalog.resources.Length,
            scenes = ScenePaths.Length, bundles = names.Length, batchFlagsCleared = cleared, uncompressedTextures = textures,
            movedResources = moves.ToArray(), contentManifestSHA256 = Hash(Path.Combine(content, "install-manifest.json")) });
        Debug.Log("FUMO_PRIVATE_CONTENT_BUILD_PASSED " + content);
    }

    static bool IsContentAsset(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return false;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return (path.StartsWith("Assets/") || path.StartsWith("Packages/"))
            && !new[] { ".cs", ".dll", ".asmdef", ".asmref", ".cginc", ".hlsl", ".meta" }.Contains(extension)
            && !path.Contains("/Editor/");
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    static void ExportSources(IEnumerable<AssetBundleBuild> assignments, string output)
    {
        var rows = new List<SourceAsset>();
        foreach (var assignment in assignments)
        foreach (string path in assignment.assetNames.OrderBy(p => p))
        {
            var objects = new List<SourceObject>();
            IEnumerable<UnityEngine.Object> values = AssetDatabase.LoadAllAssetsAtPath(path).Where(o => o);
            if (path.EndsWith(".unity", StringComparison.Ordinal))
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                values = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .SelectMany(t => new UnityEngine.Object[] { t.gameObject }.Concat(t.GetComponents<Component>().Where(c => c))).ToArray();
            }
            foreach (var value in values)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id);
                if (id == 0) { var global = GlobalObjectId.GetGlobalObjectIdSlow(value); guid = global.assetGUID.ToString(); id = (long)global.targetObjectId; }
                var transform = value is GameObject go ? go.transform : value is Component component ? component.transform : null;
                string hierarchy = "";
                while (transform) { hierarchy = transform.name + (hierarchy.Length == 0 ? "" : "/" + hierarchy); transform = transform.parent; }
                objects.Add(new SourceObject { name = value.name, type = value.GetType().FullName, guid = guid, localId = id.ToString(), hierarchy = hierarchy });
            }
            rows.Add(new SourceAsset { path = path, guid = AssetDatabase.AssetPathToGUID(path), sha256 = File.Exists(path) ? Hash(path) : "",
                bundle = assignment.assetBundleName, dependencies = AssetDatabase.GetDependencies(path, false), objects = objects.ToArray() });
        }
        Write(output, new SourceIndex { assets = rows.ToArray() });
    }

    public static void BuildPublicPlayer()
    {
        CheckIsolation("shell");
        // Retry only our disposable generated bootstrap; never touch authoring inputs.
        if (AssetDatabase.IsValidFolder("Assets/FumoBootstrap")) AssetDatabase.DeleteAsset("Assets/FumoBootstrap");
        var allowed = new HashSet<string>(new[] { ".cs", ".shader", ".cginc", ".hlsl", ".compute", ".xml", ".asmdef", ".asmref" });
        var unexpected = Directory.GetFiles("Assets", "*", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".meta") && !allowed.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray();
        if (unexpected.Length != 0) throw new InvalidOperationException("Non-code inputs entered shell project: " + string.Join(", ", unexpected.Take(10)));
        EnsureFolder("Assets/FumoBootstrap"); EnsureFolder("Assets/FumoBootstrap/Resources");
        // A fresh TMP configuration has no imported font, atlas, sprite or original settings reference.
        var tmpSettings = ScriptableObject.CreateInstance<TMPro.TMP_Settings>();
        AssetDatabase.CreateAsset(tmpSettings, "Assets/FumoBootstrap/Resources/TMP Settings.asset");
        var shaders = AssetDatabase.FindAssets("t:Shader", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Shader>).Where(s => s).Distinct().ToArray();
        var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var included = graphics.FindProperty("m_AlwaysIncludedShaders"); included.arraySize = shaders.Length;
        for (int i = 0; i < shaders.Length; i++) included.GetArrayElementAtIndex(i).objectReferenceValue = shaders[i];
        graphics.ApplyModifiedPropertiesWithoutUndo();
        PlayerSettings.SetPreloadedAssets(Array.Empty<UnityEngine.Object>());
        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, Array.Empty<Texture2D>());
        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone, Array.Empty<Texture2D>());
        PlayerSettings.companyName = "FumoRebuild"; PlayerSettings.productName = "Touhou Fumo Racing Native";
        PlayerSettings.bundleVersion = Required("FUMO_DISTRIBUTION_VERSION");
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, "com.fumoracing.community");
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);
        PlayerSettings.stripEngineCode = false; PlayerSettings.bakeCollisionMeshes = false;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetArchitecture(BuildTargetGroup.Standalone, Platform == "macos" ? 2 : 1);
        PlayerSettings.SetUseDefaultGraphicsAPIs(Target, false);
        PlayerSettings.SetGraphicsAPIs(Target, Target == BuildTarget.StandaloneOSX ? new[] { GraphicsDeviceType.Metal }
            : Target == BuildTarget.StandaloneWindows64 ? new[] { GraphicsDeviceType.Direct3D11 } : new[] { GraphicsDeviceType.OpenGLCore });
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Fumo content bootstrap").AddComponent<NativeContentBootstrap>();
        const string scenePath = "Assets/FumoBootstrap/Bootstrap.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        AssetDatabase.SaveAssets();
        string destination = Path.Combine(Output, "public-player"); Directory.CreateDirectory(destination);
        string player = Path.Combine(destination, Platform == "macos" ? "Touhou Fumo Racing.app" : "Game/Touhou Fumo Racing" + (Platform == "windows" ? ".exe" : ".x86_64"));
        Directory.CreateDirectory(Path.GetDirectoryName(player));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath }, locationPathName = player,
            target = Target, extraScriptingDefines = new[] { "FUMO_EXTERNAL_CONTENT" }, options = BuildOptions.DetailedBuildReport,
            assetBundleManifestPath = Path.Combine(Output, "private-reference/Content/Content.manifest") });
        if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
            throw new InvalidOperationException("Public shell Player build failed: " + report.summary.result);
        var packedPaths = report.packedAssets.SelectMany(p => p.contents).Select(c => c.sourceAssetPath)
            .Where(p => !string.IsNullOrEmpty(p)).Distinct().OrderBy(p => p).ToArray();
        var forbidden = packedPaths.Where(p => p.StartsWith("Assets/") && !allowed.Contains(Path.GetExtension(p).ToLowerInvariant())
            && p != scenePath && p != "Assets/FumoBootstrap/Resources/TMP Settings.asset").ToArray();
        if (forbidden.Length != 0) throw new InvalidOperationException("Original content entered Player build: " + string.Join(", ", forbidden));
        Write(Path.Combine(Output, "public-player-build-receipt.json"), new BuildReceipt { role = "code-only-player-candidate",
            engineVersion = Application.unityVersion, platform = Platform, player = player, result = "passed",
            packedAssetPaths = packedPaths,
            publicPlayerAssetPaths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") && !AssetDatabase.IsValidFolder(p)).OrderBy(p => p).ToArray() });
        Debug.Log("FUMO_PUBLIC_PLAYER_BUILD_PASSED " + player);
    }

    public static void ExportBuiltinEvidence()
    {
        CheckIsolation("content");
        const string resource = "UI/Skin/Knob.psd";
        var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(resource);
        if (!sprite || !sprite.texture) throw new InvalidOperationException("Locked engine builtin Knob sprite missing");
        var texture = sprite.texture;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string guid, out long localId);
        var evidence = new BuiltinEvidence { engineVersion = Application.unityVersion,
            editorPath = EditorApplication.applicationPath, builtinResource = resource,
            assetPath = AssetDatabase.GetAssetPath(texture), textureName = texture.name,
            guid = guid, localId = localId.ToString(), width = texture.width, height = texture.height,
            format = texture.format.ToString(), readable = texture.isReadable };
        try
        {
            byte[] bytes = texture.GetRawTextureData();
            using (var sha = SHA256.Create()) evidence.rawSHA256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        catch (Exception error) { evidence.rawError = error.Message; }
        Write(Path.Combine(Output, "private-reference", "builtin-knob-evidence.json"), evidence);
        Debug.Log("FUMO_BUILTIN_EVIDENCE " + JsonUtility.ToJson(evidence));
    }
}
