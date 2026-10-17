using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Fumo.NativePlayable
{
    public sealed class NativeItemSceneLoading : MonoBehaviour
    {
        string expectedScene;
        public static void Begin(string scene)
        {
            NativeItemDestructionContext.IsSceneLoading = true;
            var node = new GameObject("NativeItemSceneLoading");
            DontDestroyOnLoad(node);
            var bridge = node.AddComponent<NativeItemSceneLoading>();
            bridge.expectedScene = scene;
            SceneManager.sceneLoaded += bridge.Loaded;
        }
        void Loaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != expectedScene) return;
            NativeTelemetry.Event("scene.item-loading.observed", "completed", "engine-scene-loaded", scene.name);
            SceneManager.sceneLoaded -= Loaded;
            StartCoroutine(Release());
        }
        IEnumerator Release()
        {
            yield return null;
            NativeItemDestructionContext.IsSceneLoading = false;
            NativeTelemetry.Event("scene.item-loading.released", "applied");
            Destroy(gameObject);
        }
        void OnDestroy() { SceneManager.sceneLoaded -= Loaded; }
    }
}
