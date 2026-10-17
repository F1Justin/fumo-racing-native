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
                error = "无法加载游戏内容。请打开启动器，重新导入官方原版 Touhou Fumo Racing 0.2.1。\n"
                    + "若仍无法启动，请保留日志并反馈。";
                Debug.LogError("FUMO_CONTENT_INSTALL_REQUIRED " + exception);
            }
        }
        void OnGUI()
        {
            if (error == null) return;
            GUI.Box(new Rect(20, 20, Mathf.Max(300, Screen.width - 40), 220), "Touhou Fumo Racing — 需要导入游戏内容");
            GUI.Label(new Rect(40, 65, Mathf.Max(260, Screen.width - 80), 150), error);
        }
    }
}
