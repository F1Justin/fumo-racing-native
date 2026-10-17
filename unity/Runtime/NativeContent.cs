using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace Fumo.NativePlayable
{
    // The public Player contains this loader, never the reference content bundles.
    // The importer creates Content on the player's own computer.
    public static class NativeContent
    {
        [Serializable] public sealed class InstallManifest
        {
            public int schema;
            public string contentId, engineVersion, platform, initialScene, catalogFile, catalogSHA256;
            public BundleEntry[] bundles;
            public SceneEntry[] scenes;
        }
        [Serializable] public sealed class BundleEntry
        {
            public string file, sha256;
            public long bytes;
        }
        [Serializable] public sealed class SceneEntry { public string id, path, bundle; }
        [Serializable] public sealed class Catalog
        {
            public int schema;
            public ResourceEntry[] resources;
            public BundleDefinition[] bundles;
        }
        [Serializable] public sealed class ResourceEntry { public string key, bundle, asset; }
        [Serializable] public sealed class BundleDefinition
        {
            public string file;
            public bool scene;
            public string[] dependencies;
        }

        static readonly Dictionary<string, AssetBundle> loaded = new Dictionary<string, AssetBundle>(StringComparer.Ordinal);
        static readonly Dictionary<string, BundleDefinition> definitions = new Dictionary<string, BundleDefinition>(StringComparer.Ordinal);
        static readonly Dictionary<string, ResourceEntry> resources = new Dictionary<string, ResourceEntry>(StringComparer.OrdinalIgnoreCase);
        static InstallManifest manifest;
        static string root, failure;
        static bool loading;
#if FUMO_EXTERNAL_CONTENT
        static bool allowBuiltInFallback;
#else
        static bool allowBuiltInFallback = true;
#endif
        public static bool IsReady => manifest != null;
        public static string ContentRoot => root;
        public static string Error => failure;
        public static string InitialScene { get { EnsureInstalled(); return manifest.initialScene; } }

        // Fallback is a deliberate development option; the distribution build defaults to false.
        public static void SetBuiltInFallback(bool enabled)
        {
            if (manifest != null || loading) throw new InvalidOperationException("Content mode is already active");
            allowBuiltInFallback = enabled;
        }

        public static T Load<T>(string key) where T : UnityEngine.Object
        {
            if (allowBuiltInFallback && manifest == null) return Resources.Load<T>(key);
            EnsureInstalled();
            if (!resources.TryGetValue(key, out var entry)) return null;
            var bundle = Mount(entry.bundle, new HashSet<string>(StringComparer.Ordinal));
            if (typeof(Component).IsAssignableFrom(typeof(T)))
            {
                var prefab = bundle.LoadAsset<GameObject>(entry.asset);
                return prefab ? prefab.GetComponent(typeof(T)) as T : null;
            }
            var value = bundle.LoadAsset<T>(entry.asset);
            if (value) return value;
            return bundle.LoadAssetWithSubAssets<T>(entry.asset).FirstOrDefault();
        }

        public static UnityEngine.Object Load(string key) => Load<UnityEngine.Object>(key);

        public static T[] LoadAll<T>(string key) where T : UnityEngine.Object
        {
            if (allowBuiltInFallback && manifest == null) return Resources.LoadAll<T>(key);
            EnsureInstalled();
            string prefix = (key ?? "").TrimEnd('/');
            return resources.Keys.Where(k => prefix.Length == 0 || k.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || k.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.Ordinal).Select(Load<T>).Where(v => v).ToArray();
        }

        public static bool CanLoadScene(string scene)
        {
            if (allowBuiltInFallback && manifest == null) return Application.CanStreamedLevelBeLoaded(scene);
            PrepareScene(scene);
            return Application.CanStreamedLevelBeLoaded(scene);
        }

        public static string PrepareScene(string scene)
        {
            EnsureInstalled();
            var row = manifest.scenes.FirstOrDefault(s => s.id == scene || s.path == scene
                || Path.GetFileNameWithoutExtension(s.path) == scene);
            if (row == null) throw new InvalidDataException("Scene is not declared in installed content: " + scene);
            if (string.IsNullOrEmpty(row.bundle))
            {
                foreach (var definition in definitions.Values.Where(b => b.scene)) Mount(definition.file, new HashSet<string>(StringComparer.Ordinal));
            }
            else Mount(row.bundle, new HashSet<string>(StringComparer.Ordinal));
            if (!loaded.Values.Any(b => b.isStreamedSceneAssetBundle && b.GetAllScenePaths().Contains(row.path)))
                throw new InvalidDataException("Installed scene bundle does not contain " + row.path);
            return row.path;
        }

        public static void EnsureInstalled()
        {
            if (manifest != null) return;
            if (failure != null) throw new InvalidDataException(failure);
            if (loading) throw new InvalidOperationException("Recursive content initialization");
            loading = true;
            try
            {
                root = ResolveContentRoot();
                var install = ReadJson<InstallManifest>("install-manifest.json");
                if (install.schema != 1 || string.IsNullOrEmpty(install.contentId) || install.bundles == null
                    || install.bundles.Length == 0 || install.bundles.Length > 4096 || install.scenes == null
                    || install.scenes.Length == 0 || string.IsNullOrEmpty(install.initialScene))
                    throw new InvalidDataException("Unsupported or incomplete content installation manifest");
                if (!string.IsNullOrEmpty(install.engineVersion) && install.engineVersion != Application.unityVersion)
                    throw new InvalidDataException("Content requires Unity " + install.engineVersion + ", Player is " + Application.unityVersion);
                string platform = Application.platform == RuntimePlatform.OSXPlayer ? "macos"
                    : Application.platform == RuntimePlatform.WindowsPlayer ? "windows"
                    : Application.platform == RuntimePlatform.LinuxPlayer ? "linux" : "editor";
                if (!string.IsNullOrEmpty(install.platform) && platform != "editor" && install.platform != platform)
                    throw new InvalidDataException("Content platform does not match this Player");
                string catalogFile = string.IsNullOrEmpty(install.catalogFile) ? "catalog.json" : install.catalogFile;
                if (!string.IsNullOrEmpty(install.catalogSHA256)) CheckHash(catalogFile, install.catalogSHA256, 0);
                var catalog = ReadJson<Catalog>(catalogFile);
                if (catalog.schema != 1 || catalog.resources == null || catalog.bundles == null)
                    throw new InvalidDataException("Unsupported content catalog");
                var files = new HashSet<string>(StringComparer.Ordinal);
                foreach (var bundle in install.bundles)
                {
                    if (!files.Add(bundle.file)) throw new InvalidDataException("Duplicate content bundle");
                    Debug.Log("FUMO_CONTENT_VERIFY " + bundle.file);
                    CheckHash(bundle.file, bundle.sha256, bundle.bytes);
                }
                foreach (var definition in catalog.bundles)
                {
                    if (!files.Contains(definition.file) || definitions.ContainsKey(definition.file))
                        throw new InvalidDataException("Catalog bundle is not uniquely declared by manifest");
                    definitions.Add(definition.file, definition);
                }
                if (definitions.Count != files.Count) throw new InvalidDataException("Bundle catalog and installation disagree");
                foreach (var entry in catalog.resources)
                {
                    if (string.IsNullOrEmpty(entry.key) || string.IsNullOrEmpty(entry.asset)
                        || !definitions.ContainsKey(entry.bundle) || resources.ContainsKey(entry.key))
                        throw new InvalidDataException("Invalid or duplicate resource catalog entry");
                    resources.Add(entry.key, entry);
                }
                foreach (var definition in definitions.Values.Where(b => !b.scene)) Mount(definition.file, new HashSet<string>(StringComparer.Ordinal));
                manifest = install;
                // TMP package code uses its own Resources.Load; bind the locally imported
                // settings before any scene/UI objects awaken. The public shell has only
                // fresh defaults for the bootstrap screen.
                var tmp = Load<TMPro.TMP_Settings>("TMP Settings");
                if (tmp)
                {
                    var field = typeof(TMPro.TMP_Settings).GetField("s_Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    if (field == null) throw new InvalidDataException("TMP settings binding changed in the locked engine");
                    field.SetValue(null, tmp);
                }
                Debug.Log("FUMO_CONTENT_READY id=" + manifest.contentId + " bundles=" + loaded.Count + " resources=" + resources.Count);
            }
            catch (Exception error)
            {
                failure = "Original game content is not installed or failed verification. Run the importer and select the original game. " + error.Message;
                foreach (var bundle in loaded.Values) if (bundle) bundle.Unload(true);
                loaded.Clear(); definitions.Clear(); resources.Clear(); manifest = null;
                throw new InvalidDataException(failure, error);
            }
            finally { loading = false; }
        }

        static AssetBundle Mount(string name, HashSet<string> visiting)
        {
            if (loaded.TryGetValue(name, out var existing)) return existing;
            if (!definitions.TryGetValue(name, out var definition))
                throw new InvalidDataException("Invalid content bundle dependency: " + name);
            // Unity bundle dependencies can form cycles (a resource prefab can
            // refer to a shared material that refers back to a resource texture).
            // Loading a bundle file does not instantiate its assets. Finish
            // mounting the complete graph before LoadAsset or scene activation.
            if (!visiting.Add(name)) return null;
            foreach (var dependency in definition.dependencies ?? Array.Empty<string>()) Mount(dependency, visiting);
            Debug.Log("FUMO_CONTENT_MOUNT " + name);
            var bundle = AssetBundle.LoadFromFile(SafeFile(name));
            if (!bundle) throw new InvalidDataException("Unity rejected the content bundle: " + name);
            loaded.Add(name, bundle); visiting.Remove(name); return bundle;
        }

        static T ReadJson<T>(string name)
        {
            string path = SafeFile(name);
            if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024 * 1024)
                throw new InvalidDataException("Missing or oversized content manifest: " + name);
            var value = JsonUtility.FromJson<T>(File.ReadAllText(path));
            if (value == null) throw new InvalidDataException("Invalid content manifest: " + name);
            return value;
        }

        static void CheckHash(string name, string expected, long length)
        {
            string path = SafeFile(name);
            if (expected == null || expected.Length != 64 || expected.Any(c => !Uri.IsHexDigit(c)) || !File.Exists(path))
                throw new InvalidDataException("Missing content file or hash: " + name);
            if (length > 0 && new FileInfo(path).Length != length) throw new InvalidDataException("Content file length mismatch: " + name);
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                if (!string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Content hash mismatch: " + name);
        }

        static string SafeFile(string name)
        {
            if (string.IsNullOrEmpty(name) || Path.IsPathRooted(name) || name.Contains("\\") || name.Contains(":"))
                throw new InvalidDataException("Invalid content path");
            string current = root;
            foreach (string part in name.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == "..") throw new InvalidDataException("Invalid content path segment");
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Content symlinks are not accepted");
            }
            return current;
        }

        static string ResolveContentRoot()
        {
            string selected = Environment.GetEnvironmentVariable("FUMO_CONTENT_PATH");
            var args = Environment.GetCommandLineArgs();
            int option = Array.IndexOf(args, "--fumo-content-dir");
            if (option < 0) option = Array.IndexOf(args, "-fumo-content-dir");
            if (string.IsNullOrEmpty(selected) && option >= 0)
            {
                if (option + 1 >= args.Length) throw new ArgumentException("--fumo-content-dir needs a path");
                selected = args[option + 1];
            }
            if (string.IsNullOrEmpty(selected))
            {
                var directory = new DirectoryInfo(Application.dataPath);
                if (Application.platform == RuntimePlatform.OSXPlayer)
                {
                    while (directory != null && !directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) directory = directory.Parent;
                    if (directory == null) throw new InvalidDataException("Cannot locate Player application directory");
                    directory = directory.Parent;
                }
                else directory = directory.Parent;
                selected = Path.Combine(directory.FullName, "Content");
                if (!Directory.Exists(selected) && directory.Name == "Game") selected = Path.Combine(directory.Parent.FullName, "Content");
            }
            return Path.GetFullPath(selected);
        }
    }
}
