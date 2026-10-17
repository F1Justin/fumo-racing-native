using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fumo.NativePlayable
{
    public sealed class NativeContentBootstrap : MonoBehaviour
    {
        string error;
        void Start()
        {
            try
            {
                if (!NativeContent.IsReady) NativeContent.SetBuiltInFallback(false);
                NativeContent.EnsureInstalled();
                string path = NativeContent.PrepareScene(NativeContent.InitialScene);
                Debug.Log("FUMO_CONTENT_INITIAL_SCENE " + path);
                SceneManager.LoadScene(path, LoadSceneMode.Single);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Debug.LogError("FUMO_CONTENT_INSTALL_REQUIRED " + error);
            }
        }
        void OnGUI()
        {
            if (error == null) return;
            GUI.Box(new Rect(20, 20, Mathf.Max(300, Screen.width - 40), 220), "Touhou Fumo Racing — Content required");
            GUI.Label(new Rect(40, 65, Mathf.Max(260, Screen.width - 80), 150), error);
        }
    }
}
