using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class OriginalSpriteBindingReport
        {
            public bool passed, visualParityAccepted;
            public int sourceCount, consumerCount;
            public string evidenceScope = "Actual keyboard state traversal, source Sprite identity, original bundle metadata/mesh/padding/bounds and source texture identity. Paused masked pixels compare an independently instantiated original-Sprite disc with source Image/Mask flags over an injected constant background that excludes moving grass. Original-game pixel acceptance remains open.";
            public string[] checks, screenshots;
            public OriginalDiscPixelComparison[] pixels;
            public NativeAuthoredUi.OriginalSpriteObservation[] observations;
        }
        [Serializable] sealed class OriginalDiscPixelComparison
        {
            public string id, actualHash, sourceRenderHash, actualPNG, sourceRenderPNG;
            public bool passed;
            public Rect screenRect;
            public int comparedPixels, differentPixels;
        }
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
        internal IEnumerator CheckOriginalSpriteBindingsLive()
        {
            var source=JsonUtility.FromJson<NativeAuthoredUi.OriginalSpriteSource>(NativeContent.Load<TextAsset>("NativeVerification/OriginalSpriteBindingsSource").text);
            var observations=new List<NativeAuthoredUi.OriginalSpriteObservation>(); var checks=new List<string>(); var screenshots=new List<string>(); var pixels=new List<OriginalDiscPixelComparison>();
            Action<bool,string> check=(ok,label)=>checks.Add((ok ? "PASS " : "FAIL ")+label);
            var oldBackground=InputSystem.settings.backgroundBehavior; InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard=InputSystem.AddDevice<Keyboard>(); bool oldPointer=hud.SuppressPointerInput; hud.SuppressPointerInput=true;
            Directory.CreateDirectory(reportDirectory);
            try
            {
                yield return new WaitForSecondsRealtime(.3f); yield return new WaitForEndOfFrame();
                check(source.sourceCount==11 && source.rows.Length==11 && source.sourceImageCount==823,"independent original11/source823 baseline");
                check(hud.MenuPage==NativeMenuPage.Main,"main initial state");
                yield return CaptureRequiredMenuFieldVerification("MainMenu");
                yield return CaptureMainAliasVerification();
                yield return CheckMainPromptSourceLive(keyboard, check);
                yield return CheckDiscordPointerFixtureLive(keyboard,check);
                yield return MenuParityKey(keyboard,Key.DownArrow); yield return MenuParityKey(keyboard,Key.DownArrow); yield return MenuParityKey(keyboard,Key.Enter);
                check(hud.MenuPage==NativeMenuPage.Extras,"actual Extras route");
                yield return CaptureRequiredMenuFieldVerification("ExtrasMenu");
                CaptureOriginalButtonSprites("extras0", "ExtrasMenu", new[]{"CreditsButton","MusicRoomButton"},0,source,observations);
                yield return MenuParityKey(keyboard,Key.DownArrow);
                CaptureOriginalButtonSprites("extras1", "ExtrasMenu", new[]{"CreditsButton","MusicRoomButton"},1,source,observations);
                yield return MenuParityKey(keyboard,Key.Enter); check(hud.MenuPage==NativeMenuPage.MusicRoom,"actual MusicRoom route");
                string[] ids={"Menu","CharSelect","Forest","Mountains","Purplefield","WistyLake","Results","FullPower"};
                for(int i=0;i<8;i++)
                {
                    check(hud.MusicSelection==i && NativeMusicRoomData.At(i).id==ids[i],"actual cover selection "+ids[i]);
                    CaptureOriginalDiscSprites("cover-"+ids[i],source,observations);
                    string name="sprite-cover-"+ids[i]; screenshots.Add(name+".png"); yield return MenuParityScreenshot(name);
                    yield return MenuParityKey(keyboard,Key.Space);
                    check(NativeAudioRuntime.MusicPaused,"fixed mask pixel state paused "+ids[i]);
                    yield return CompareOriginalDiscPixels(ids[i],pixels);
                    yield return MenuParityKey(keyboard,Key.Space);
                    yield return MenuParityKey(keyboard,Key.DownArrow);
                }
                check(hud.MusicSelection==0,"next wraps7 to0"); CaptureOriginalDiscSprites("wrap-next",source,observations);
                yield return MenuParityKey(keyboard,Key.UpArrow); check(hud.MusicSelection==7,"previous wraps0 to7"); CaptureOriginalDiscSprites("wrap-prev",source,observations);
                yield return MenuParityKey(keyboard,Key.Space); check(NativeAudioRuntime.MusicPaused,"actual pause"); CaptureOriginalDiscSprites("paused",source,observations);
                yield return MenuParityKey(keyboard,Key.Space); check(!NativeAudioRuntime.MusicPaused,"actual resume"); CaptureOriginalDiscSprites("resumed",source,observations);
                yield return MenuParityKey(keyboard,Key.Escape);
                yield return MenuParityKey(keyboard,Key.DownArrow);
                yield return MenuParityKey(keyboard,Key.Enter);
                check(hud.MenuPage==NativeMenuPage.MusicRoom,"MusicRoom reentry"); CaptureOriginalDiscSprites("reentry",source,observations);
                yield return MenuParityKey(keyboard,Key.Escape); yield return MenuParityKey(keyboard,Key.Escape); yield return MenuParityKey(keyboard,Key.DownArrow); yield return MenuParityKey(keyboard,Key.Enter);
                check(settingsOpen,"actual Options route");
                yield return CaptureRequiredMenuFieldVerification("OptionsMenu");
                for(int i=0;i<4;i++)
                {
                    CaptureOriginalButtonSprites("options"+i,"OptionsMenu",new[]{"VideoButton","AudioButton","InputButton","LanguageButton"},i,source,observations);
                    if(i<3) yield return MenuParityKey(keyboard,Key.DownArrow);
                }
                check(observations.Count==46,"8 consumer states observed across8 covers+wrap+pause+reentry and6 button states");
            }
            finally
            {
                hud.SuppressPointerInput=oldPointer; InputSystem.RemoveDevice(keyboard); InputSystem.settings.backgroundBehavior=oldBackground;
            }
            checks.AddRange(requiredMenuFieldChecks);
            var report=new OriginalSpriteBindingReport { sourceCount=11, consumerCount=8, observations=observations.ToArray(), checks=checks.ToArray(), screenshots=screenshots.ToArray(), pixels=pixels.ToArray(), visualParityAccepted=false,
                passed=checks.All(c=>c.StartsWith("PASS ")) && observations.All(o=>o.passed) && pixels.Count==8 && pixels.All(p=>p.passed) };
            File.WriteAllText(Path.Combine(reportDirectory,"original-sprite-bindings.json"),JsonUtility.ToJson(report,true));
            Application.Quit(report.passed ? 0 : 2);
        }
        IEnumerator CompareOriginalDiscPixels(string id,List<OriginalDiscPixelComparison> results)
        {
            var ui=hud.OriginalSpriteUi;
            var background=ui.DiscComparisonBackground();
            yield return null; yield return new WaitForEndOfFrame();
            var region=ui.OriginalDiscScreenRect();
            var actual=ReadOriginalDiscPixels(region); GameObject clone=null; Texture2D reference=null;
            try
            {
                clone=ui.SourceDiscRenderReference(NativeMusicRoomData.At(hud.MusicSelection).cover);
                yield return null; yield return new WaitForEndOfFrame();
                reference=ReadOriginalDiscPixels(region); var left=actual.GetPixels32(); var right=reference.GetPixels32();
                int differences=0; for(int i=0;i<left.Length;i++) if(!left[i].Equals(right[i])) differences++;
                string actualName="mask-"+id+"-actual.png", sourceName="mask-"+id+"-source-render.png";
                File.WriteAllBytes(Path.Combine(reportDirectory,actualName),actual.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(reportDirectory,sourceName),reference.EncodeToPNG());
                results.Add(new OriginalDiscPixelComparison { id=id, screenRect=region, comparedPixels=left.Length,differentPixels=differences,
                    actualHash=DiscPixelDigest(actual),sourceRenderHash=DiscPixelDigest(reference),actualPNG=actualName,sourceRenderPNG=sourceName,passed=differences==0 });
            }
            finally
            {
                ui.RestoreDiscRenderReference(clone); UnityEngine.Object.Destroy(actual); if(reference) UnityEngine.Object.Destroy(reference);
                background.SetActive(false); UnityEngine.Object.Destroy(background);
            }
            yield return null; yield return new WaitForEndOfFrame();
        }
        static Texture2D ReadOriginalDiscPixels(Rect region)
        {
            var image=new Texture2D((int)region.width,(int)region.height,TextureFormat.RGBA32,false);
            image.ReadPixels(region,0,0); image.Apply(); return image;
        }
        static string DiscPixelDigest(Texture2D image)
        {
            using(var hash=System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(image.GetRawTextureData())).Replace("-", "").ToLowerInvariant();
        }
        void CaptureOriginalButtonSprites(string state,string page,string[] names,int selected,NativeAuthoredUi.OriginalSpriteSource source,List<NativeAuthoredUi.OriginalSpriteObservation> observations)
        {
            for(int i=0;i<names.Length;i++)
            {
                bool active=i==selected; string id=active ? "genericbuttonhover" : "genericbutton";
                string key="menu2/Canvas/"+page+"/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/"+names[i];
                observations.Add(hud.OriginalSpriteUi.ObserveOriginalSprite(state,key,NativeMenuSpriteData.At(active),source.rows.Single(r=>r.id==id),true));
            }
        }
        void CaptureOriginalDiscSprites(string state,NativeAuthoredUi.OriginalSpriteSource source,List<NativeAuthoredUi.OriginalSpriteObservation> observations)
        {
            const string key="menu2/Canvas/MusicRoom/Disc/labelmask"; var ui=hud.OriginalSpriteUi; var cover=NativeMusicRoomData.At(hud.MusicSelection);
            observations.Add(ui.ObserveOriginalSprite(state,key,NativeMenuSpriteData.Source.labelMask,source.rows.Single(r=>r.id=="VinylLabel"),true));
            observations.Add(ui.ObserveOriginalSprite(state,key+"/label",cover.cover,source.rows.Single(r=>r.id==cover.id),false));
        }
#else
        internal IEnumerator CheckOriginalSpriteBindingsLive()
        {
            throw new InvalidOperationException("Original Sprite input verification requires the built Player.");
        }
#endif
    }
}
