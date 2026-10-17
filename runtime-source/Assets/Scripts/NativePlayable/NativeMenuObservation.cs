#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Fumo.NativePlayable
{
    public sealed class NativeMenuObservation : MonoBehaviour
    {
        [Serializable] sealed class Command { public string id, action, value; }
        [Serializable] sealed class TextSample
        {
            public string path, sourcePath, text, font;
            public int instance;
            public bool active, culled, autoSize;
            public float fontSize, minimum, maximum, canvasScale, glyphHeight;
            public Vector3 scale;
            public Vector2 rectSize;
            public Rect glyphBounds;
        }
        [Serializable] sealed class Frame
        {
            public string image, page, from, to;
            public int frame, language, focus;
            public float seconds, transitionElapsed;
            public bool transition, focused, keyboardEnabled;
            public int keyboardDeviceId;
            public TextSample[] texts;
        }
        [Serializable] sealed class Clip { public string input = "InputSystem Keyboard events; production menu navigation"; public Frame[] frames; }
        string directory, commandPath, lastCommand;
        string clipDirectory;
        Keyboard keyboard;
        readonly List<Frame> frames = new List<Frame>();
        bool recording, keyPending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--menu-observe-directory");
            if (index < 0 || index + 1 >= args.Length) return;
            var observer = new GameObject("Menu visual observation").AddComponent<NativeMenuObservation>();
            DontDestroyOnLoad(observer);
            observer.directory = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(observer.directory);
            observer.commandPath = Path.Combine(observer.directory, "command.json");
        }
        IEnumerator Start()
        {
            keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            File.WriteAllText(Path.Combine(directory, "ready.json"), "{\"ready\":true,\"pid\":" + System.Diagnostics.Process.GetCurrentProcess().Id + "}");
            while (true)
            {
                yield return new WaitForEndOfFrame();
                if (!recording) continue;
                var race = FindObjectOfType<NativeRace>();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                string image = frames.Count.ToString("D5") + ".jpg";
                File.WriteAllBytes(Path.Combine(clipDirectory, image), texture.EncodeToJPG(95));
                Destroy(texture);
                var hud = race ? race.hud : null;
                var state = hud?.TransitionState;
                frames.Add(new Frame { image = image, frame = Time.frameCount, seconds = Time.realtimeSinceStartup,
                    language = NativeLocalization.Language, page = hud?.MenuPage.ToString(), focus = hud != null && hud.MenuPage == NativeMenuPage.Online ? hud.NetworkMenuFocus?.index ?? -1 : hud?.SelectedMenuIndex ?? -1,
                    focused = Application.isFocused, keyboardEnabled = keyboard.enabled, keyboardDeviceId = keyboard.deviceId,
                    transition = hud != null && hud.MenuTransitionActive, from = state?.from, to = state?.to,
                    transitionElapsed = state?.elapsed ?? 0,
                    texts = FindObjectsOfType<TMP_Text>().Where(t => t.isActiveAndEnabled && !string.IsNullOrWhiteSpace(t.text)).Select(ReadText).ToArray() });
                if (frames.Count == 1) File.WriteAllText(Path.Combine(clipDirectory, "first-frame.json"), JsonUtility.ToJson(frames[0], true));
            }
        }
        void Update()
        {
            if (keyPending || !File.Exists(commandPath)) return;
            Command command;
            try { command = JsonUtility.FromJson<Command>(File.ReadAllText(commandPath)); }
            catch (Exception) { return; }
            if (command == null || command.id == lastCommand) return;
            lastCommand = command.id;
            if (command.action == "key") { StartCoroutine(Press(command)); return; }
            if (command.action == "record")
            {
                if (recording) throw new InvalidOperationException("Stop the current clip first");
                clipDirectory = Path.Combine(directory, command.value);
                Directory.CreateDirectory(clipDirectory); frames.Clear(); recording = true;
            }
            else if (command.action == "stop")
            {
                recording = false;
                File.WriteAllText(Path.Combine(clipDirectory, "frames.json"), JsonUtility.ToJson(new Clip { frames = frames.ToArray() }, true));
            }
            else if (command.action == "quit") { Application.Quit(); return; }
            Acknowledge(command);
        }
        IEnumerator Press(Command command)
        {
            keyPending = true;
            var key = (Key)Enum.Parse(typeof(Key), command.value);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null; yield return null;
            keyPending = false; Acknowledge(command);
        }
        void Acknowledge(Command command) => File.WriteAllText(Path.Combine(directory, "ack.json"),
            "{\"id\":\"" + command.id + "\",\"seconds\":" + Time.realtimeSinceStartup.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");

        static string PathOf(Transform transform)
        {
            var names = new List<string>();
            for (var node = transform; node; node = node.parent) names.Add(node.name);
            names.Reverse(); return string.Join("/", names);
        }
        static TextSample ReadText(TMP_Text text)
        {
            var identity = text.GetComponent<NativeUiSourceIdentity>();
            string path = PathOf(text.transform);
            string source = identity ? identity.sourcePath : null;
            if (string.IsNullOrEmpty(source) && path.StartsWith("Original authored menus/menu2 authored Canvas/"))
                source = "menu2/Canvas/" + path.Substring("Original authored menus/menu2 authored Canvas/".Length);
            var heights = new List<float>();
            Vector2 minimum = new Vector2(float.MaxValue, float.MaxValue), maximum = new Vector2(float.MinValue, float.MinValue);
            var camera = text.canvas && text.canvas.renderMode != RenderMode.ScreenSpaceOverlay ? text.canvas.worldCamera : null;
            foreach (var character in text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(c => c.isVisible))
            {
                var bottom = RectTransformUtility.WorldToScreenPoint(camera, text.transform.TransformPoint(character.bottomLeft));
                var top = RectTransformUtility.WorldToScreenPoint(camera, text.transform.TransformPoint(character.topRight));
                minimum = Vector2.Min(minimum, Vector2.Min(bottom, top)); maximum = Vector2.Max(maximum, Vector2.Max(bottom, top));
                heights.Add(Mathf.Abs(top.y - bottom.y));
            }
            heights.Sort();
            return new TextSample { path = path, sourcePath = source, instance = text.GetInstanceID(), text = text.text,
                font = text.font ? text.font.name : "", active = text.gameObject.activeInHierarchy,
                culled = text.canvasRenderer.cull, autoSize = text.enableAutoSizing, fontSize = text.fontSize,
                minimum = text.fontSizeMin, maximum = text.fontSizeMax, canvasScale = text.canvas ? text.canvas.scaleFactor : 1,
                scale = text.transform.lossyScale, rectSize = text.rectTransform.rect.size,
                glyphHeight = heights.Count == 0 ? 0 : heights[heights.Count / 2],
                glyphBounds = heights.Count == 0 ? Rect.zero : Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y) };
        }
    }
}
#endif
