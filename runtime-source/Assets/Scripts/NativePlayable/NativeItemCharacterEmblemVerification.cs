using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public static class NativeItemCharacterEmblemVerification
    {
        static readonly string[] sourceIds = { "BlueStar", "Bnuuy", "Bomb", "DriftPlusPowerup", "FantasySeal", "FreezeBomb", "Frog", "FullPower", "Insanity", "JumperFrog", "MarisaShield", "Ofuda", "Power", "Power2", "Power3", "RockThrow", "SanaeSnakes", "Shangai", "SwordSlash", "WindBoost", "YellowStar", "YinYang", "YinYang2", "YinYangHoming", "YinYangHoming2" };
        static readonly string[] sourceCharacters = { "", "Tewi", "", "", "Reimu", "Cirno", "", "", "Reisen", "", "Marisa", "", "", "", "", "Suika", "Sanae", "Alice", "Youmu", "Aya", "", "", "", "", "" };
        static readonly string[] sourceSpriteNames = { "", "Icon8_tewi", "", "", "Icon1_reimu", "Icon3_cirno", "", "", "Icon7_reisen", "", "Icon2_marisa", "", "", "", "", "Icon6_suika", "Icon94_sanae", "Icon55_alice", "Icon5_youmu", "Icon93_aya", "", "", "", "", "" };
        [Serializable] public sealed class Sample
        {
            public string item, character, state, expectedSprite, actualSprite, frame;
            public bool passed, enabled, activeInHierarchy, frameExists;
            public bool clockAdvancing, rouletteObserved, productionLateUpdateObserved, panelVisible, mainIconVisible, autoUsed, animationSettled;
            public int renderedFrames, giveCount, decideCount, doneCount;
            public float effectiveAlpha, mainEffectiveAlpha, scaledElapsed, decisionClipLength, sampledDecisionElapsed, screenshotWaitSeconds;
        }
        [Serializable] public sealed class Report
        {
            public bool passed, scriptedState = true, gameplayAcceptance = false;
            public string scope = "25 original randomized item decisions after two-second source delay; production LateUpdate, source nonlooping decision clip completion, two rendered frames and fresh complete PNGs; original matched frames pending";
            public int exclusiveCount, ordinaryCount;
            public string[] checks;
            public Sample[] samples;
        }
        static bool FreshCompletePng(string path, DateTime previousWrite)
        {
            try
            {
                if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= previousWrite) return false;
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (file.Length < 32) return false;
                    byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                    foreach (byte value in signature) if (file.ReadByte() != value) return false;
                    file.Seek(-12, SeekOrigin.End);
                    byte[] end = { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 };
                    foreach (byte value in end) if (file.ReadByte() != value) return false;
                    return true;
                }
            }
            catch (IOException) { return false; }
        }

        public static IEnumerator Run(NativeRace race, string directory)
        {
            Directory.CreateDirectory(directory);
            var samples = new List<Sample>();
            var hud = race.itemHud;
            var inventory = race.racers[0].GetComponent<NativeKartItemInventory>();
            var roster = race.vehicleRoster;
            int exclusiveCount = 0, ordinaryCount = 0;
            bool allPassed = hud && inventory && roster && inventory.catalog.Count == 25;
            if (allPassed)
            {
                hud.SetVisible(true);
                foreach (var entry in inventory.catalog.entries)
                {
                    inventory.ResetItems();
                    hud.ResetState();
                    float scaledStart = Time.time;
                    bool given = inventory.TryGive(entry.id, true);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    bool rouletteObserved = hud.CurrentState == "itemidle" && !inventory.ItemDecided;
                    float decisionDeadline = Time.realtimeSinceStartup + 4f;
                    while ((!inventory.ItemDecided || hud.CurrentState != "decidedItem") &&
                        Time.realtimeSinceStartup < decisionDeadline) yield return null;
                    var decisionClip = hud.clips != null ? Array.Find(hud.clips, clip => clip.name == "decidedItem") : null;
                    float decisionObservedAt = Time.time;
                    var sourceAlpha = decisionClip != null && decisionClip.curves != null
                        ? Array.Find(decisionClip.curves, curve => curve.node == hud.iconNode && curve.property == "m_Color.a") : null;
                    bool sourceClipValid = decisionClip != null && !decisionClip.loop &&
                        Mathf.Approximately(decisionClip.length, 1f) && sourceAlpha != null &&
                        sourceAlpha.curve != null && Mathf.Approximately(sourceAlpha.curve.Evaluate(decisionClip.length), 1f);
                    if (sourceClipValid)
                    {
                        float animationDeadline = Time.realtimeSinceStartup + decisionClip.length + 4f;
                        while (Time.time - decisionObservedAt < decisionClip.length &&
                            Time.realtimeSinceStartup < animationDeadline) yield return null;
                    }
                    int renderedFrames = 0;
                    for (int i = 0; i < 2; i++)
                    {
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        renderedFrames++;
                    }
                    float scaledElapsed = Time.time - scaledStart;
                    float sampledDecisionElapsed = Time.time - decisionObservedAt;
                    bool animationSettled = sourceClipValid && sampledDecisionElapsed >= decisionClip.length;
                    bool clockAdvancing = Time.timeScale > 0f && scaledElapsed >= 2f;
                    bool productionLateUpdateObserved = rouletteObserved && hud.CurrentState == "decidedItem" &&
                        hud.enabled && hud.gameObject.activeInHierarchy && inventory.enabled &&
                        inventory.GiveCount == 1 && inventory.DecideCount == 1 && inventory.DoneCount == 0;
                    int sourceIndex = Array.IndexOf(sourceIds, entry.id);
                    bool sourceMatch = sourceIndex >= 0 && string.Equals(sourceCharacters[sourceIndex],
                        entry.characterConditionId ?? "", StringComparison.OrdinalIgnoreCase);
                    if (sourceIndex >= 0 && sourceCharacters[sourceIndex].Length > 0) exclusiveCount++;
                    else ordinaryCount++;
                    int index = string.IsNullOrEmpty(entry.characterConditionId)
                        ? -1 : roster.CharacterIndexOf(entry.characterConditionId);
                    Sprite expected = index >= 0 ? roster.CharacterIconAt(index) : null;
                    var image = hud.characterIcon;
                    float alpha = image ? image.color.a : 0f;
                    if (image)
                        foreach (var group in image.GetComponentsInParent<CanvasGroup>()) alpha *= group.alpha;
                    bool visible = image && image.enabled && image.gameObject.activeInHierarchy && alpha > .001f;
                    var canvas = hud.GetComponent<Canvas>();
                    bool panelVisible = hud.panel && hud.panel.activeInHierarchy && canvas && canvas.enabled;
                    float mainAlpha = hud.icon ? hud.icon.color.a : 0f;
                    if (hud.icon)
                        foreach (var group in hud.icon.GetComponentsInParent<CanvasGroup>()) mainAlpha *= group.alpha;
                    bool mainIconVisible = hud.icon && hud.icon.enabled && hud.icon.gameObject.activeInHierarchy &&
                        mainAlpha > .001f && hud.icon.sprite == inventory.CurrentIcon;
                    bool autoUsed = inventory.State.used;
                    bool autoUseSourceMatch = autoUsed == (entry.id == "DriftPlusPowerup");
                    bool passed = given && clockAdvancing && animationSettled && renderedFrames == 2 &&
                        productionLateUpdateObserved && panelVisible && mainIconVisible && autoUseSourceMatch &&
                        inventory.HasItem && inventory.ItemDecided && sourceMatch && (index < 0 ||
                        expected && expected.name == sourceSpriteNames[sourceIndex]) && hud.CurrentState == "decidedItem" &&
                        (index >= 0 ? visible && image.sprite == expected : image && !image.enabled);
                    string frame = "emblem-" + entry.id + ".png";
                    string framePath = Path.Combine(directory, frame);
                    DateTime previousWrite = File.Exists(framePath) ? File.GetLastWriteTimeUtc(framePath) : DateTime.MinValue;
                    var sample = new Sample { item = entry.id, character = entry.characterConditionId,
                        state = hud.CurrentState, expectedSprite = expected ? expected.name : "",
                        actualSprite = image && image.sprite ? image.sprite.name : "", frame = frame,
                        enabled = image && image.enabled, activeInHierarchy = image && image.gameObject.activeInHierarchy,
                        effectiveAlpha = alpha, clockAdvancing = clockAdvancing, rouletteObserved = rouletteObserved,
                        productionLateUpdateObserved = productionLateUpdateObserved, renderedFrames = renderedFrames,
                        panelVisible = panelVisible, mainIconVisible = mainIconVisible, mainEffectiveAlpha = mainAlpha,
                        autoUsed = autoUsed, scaledElapsed = scaledElapsed, animationSettled = animationSettled,
                        decisionClipLength = decisionClip != null ? decisionClip.length : 0f,
                        sampledDecisionElapsed = sampledDecisionElapsed,
                        giveCount = inventory.GiveCount, decideCount = inventory.DecideCount, doneCount = inventory.DoneCount };
                    float captureStarted = Time.realtimeSinceStartup;
                    ScreenCapture.CaptureScreenshot(framePath);
                    float deadline = captureStarted + 15f;
                    while (!FreshCompletePng(framePath, previousWrite) &&
                        Time.realtimeSinceStartup < deadline) yield return null;
                    sample.frameExists = FreshCompletePng(framePath, previousWrite);
                    sample.screenshotWaitSeconds = Time.realtimeSinceStartup - captureStarted;
                    passed &= sample.frameExists;
                    sample.passed = passed;
                    samples.Add(sample);
                    allPassed &= passed;
                    Debug.Log("NATIVE_ITEM_EMBLEM " + (passed ? "PASS " : "FAIL ") + entry.id);
                    yield return null;
                }
                inventory.ResetItems();
                hud.ResetState();
            }
            File.WriteAllText(Path.Combine(directory, "item-character-emblem-report.json"),
                JsonUtility.ToJson(new Report { passed = allPassed && samples.Count == 25 && exclusiveCount == 10 && ordinaryCount == 15,
                    exclusiveCount = exclusiveCount, ordinaryCount = ordinaryCount, samples = samples.ToArray(),
                    checks = samples.Select(sample => (sample.passed ? "PASS " : "FAIL ") + "source item emblem " + sample.item)
                        .Concat(new[] { (samples.Count == 25 && exclusiveCount == 10 && ordinaryCount == 15 ? "PASS " : "FAIL ") + "source denominator 25 items, 10 exclusive, 15 ordinary" }).ToArray() }, true));
        }
    }
}
