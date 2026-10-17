using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class CharacterProfileAudioObservation
        {
            public string clip, mixerGroup, deviceIdentity, selectedProfile, bindingError;
            public int pendingIndex, committedIndex;
            public int sourceId, frame;
            public float listenerVolume, sourceVolume;
            public bool muted, uiVerificationPlayback, sourceEnabled, sourcePlaying;
        }
        [Serializable] sealed class CharacterProfileVisibleSample
        {
            public string page, screenshot, expectedName;
            public int pendingIndex;
            public bool compatible;
            public NativeAuthoredUi.CharacterProfileControlSample[] controls;
            public int[] visiblePixels;
        }
        [Serializable] sealed class CharacterProfilePreviewSample
        {
            public string page, stage, screenshot, renderTextureCapture, pixelSHA256;
            public int actorId,cameraId,textureId,character,vehicle,skin,nonzeroAlphaPixels;
            public bool rawActive,rawMatchesCamera,textureCreated;
            public Quaternion rotation;
        }
        [Serializable] sealed class CharacterProfileVerificationReport
        {
            public bool passed, nativeInjectedState = true, realKeyboardEvents, fourHumanLiveAccepted, originalPairAccepted, visualParityAccepted;
            public string error, cleanupError, assemblySHA256, baselineSHA256;
            public string fixtureMode;
            public string audioScope = "Actual post-PlayOneShot callback/source/object/count; listener volume forced0, DSP/audibility acceptance remains open";
            public List<CharacterProfileAudioObservation> audio = new List<CharacterProfileAudioObservation>();
            public List<CharacterProfileVisibleSample> visibleControls = new List<CharacterProfileVisibleSample>();
            public List<CharacterProfilePreviewSample> previews = new List<CharacterProfilePreviewSample>();
            public string platformBoundary = "P1 joined/not-ready uses real keyboard actions; P2-P4 source controls and state guards tested by injection; four-human joined device dispatch remains open";
            public List<string> checks = new List<string>(), actualAudio = new List<string>(), screenshots = new List<string>();
        }
        static void ProfileRequire(CharacterProfileVerificationReport report, bool value, string text)
        {
            report.checks.Add((value ? "PASS " : "FAIL ") + text);
            if (!value) throw new InvalidOperationException(text);
        }
        static string ProfileSha256(byte[] bytes)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        static void SilenceCharacterProfileVerification() => AudioListener.volume = 0;
        readonly List<AudioClip> characterProfileObservedClips = new List<AudioClip>();
        internal IEnumerator CheckCharacterProfileLive()
        {
            var report = new CharacterProfileVerificationReport();
            var savedData = NativeUserSettings.CopyData();
            bool pointer = hud.SuppressPointerInput, playback = NativeAudioRuntime.UiVerificationPlayback;
            float previousListener = AudioListener.volume;
            NativeUserSettings.Changed += SilenceCharacterProfileVerification; AudioListener.volume = 0;
            int vsync = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0; hud.SuppressPointerInput = true; NativeAudioRuntime.UiVerificationPlayback = true;
            characterProfileObservedClips.Clear();
            System.Action<AudioSource,AudioClip> observe = (source,clip) =>
            {
                if (source != hud.OriginalSelectionMoveSource) return;
                report.actualAudio.Add(clip.name); characterProfileObservedClips.Add(clip);
                report.audio.Add(new CharacterProfileAudioObservation { clip=clip.name, sourceId=source.GetInstanceID(), frame=Time.frameCount,
                    mixerGroup=source.outputAudioMixerGroup ? source.outputAudioMixerGroup.name : "", listenerVolume=AudioListener.volume,
                    sourceVolume=source.volume, muted=NativeAudioRuntime.Muted, uiVerificationPlayback=NativeAudioRuntime.UiVerificationPlayback,
                    sourceEnabled=source.enabled, sourcePlaying=source.isPlaying, deviceIdentity=NativeInputBindings.AppliedDeviceIdentity(),
                    selectedProfile=NativeInputBindings.SelectedProfile(), bindingError=NativeInputBindings.LastError,
                    pendingIndex=characterProfiles[0].CurrentIndex, committedIndex=characterProfiles[0].SelectedIndex });
            };
            NativeAudioRuntime.UiOneShotPlayed += observe;
            var pending = new Stack<IEnumerator>(); pending.Push(CheckCharacterProfileCore(report));
            try
            {
                while (pending.Count > 0)
                {
                    bool next; object value;
                    try { next = pending.Peek().MoveNext(); value = next ? pending.Peek().Current : null; }
                    catch (Exception e) { report.error = e.ToString(); break; }
                    if (!next) { pending.Pop(); if (pending.Count == 0) report.passed = true; continue; }
                    if (value is IEnumerator nested && !(value is CustomYieldInstruction)) { pending.Push(nested); continue; }
                    yield return value;
                }
            }
            finally
            {
                foreach (var routine in pending) try { (routine as IDisposable)?.Dispose(); } catch (Exception e) { report.cleanupError = e.ToString(); report.passed = false; }
                NativeAudioRuntime.UiOneShotPlayed -= observe; NativeAudioRuntime.UiVerificationPlayback = playback;
                if (!NativeSaveStore.ReplaceData(savedData)) { report.cleanupError = NativeSaveStore.LastError; report.passed = false; }
                NativeInputBindings.Initialize();
                NativeUserSettings.Changed -= SilenceCharacterProfileVerification; AudioListener.volume = previousListener;
                hud.SuppressPointerInput = pointer; QualitySettings.vSyncCount = vsync;
                Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(Path.Combine(reportDirectory,"character-profile-report.json"),JsonUtility.ToJson(report,true));
            }
            Debug.Log("CHARACTER_PROFILE_RESULT passed=" + report.passed); Application.Quit(report.passed ? 0 : 2);
        }
        IEnumerator CheckCharacterProfileCore(CharacterProfileVerificationReport report)
        {
            report.assemblySHA256 = ProfileSha256(File.ReadAllBytes(typeof(NativeRace).Assembly.Location));
            var baseline = NativeContent.Load<TextAsset>("NativeVerification/CharacterProfileSource");
            ProfileRequire(report,baseline,"independent source obligation resource exists");
            report.baselineSHA256 = ProfileSha256(System.Text.Encoding.UTF8.GetBytes(baseline.text));
            phase = Phase.Menu;
            for (int i = 0; !hud.SettingsAuthoredReady && i < 120; i++) yield return null;
            ProfileRequire(report,hud.SettingsAuthoredReady,"production authored menus ready");
            for (int human = 0; human < 4; human++)
            {
                var state = new NativeCharacterProfileState(human);
                ProfileRequire(report,hud.CharacterProfileControls(human).Length == 6,"P" + (human+1) + " six original Profile nodes");
                ProfileRequire(report,state.CurrentIndex == -1 && state.SelectedIndex == -1,"P" + (human+1) + " default and committed initialization");
                ProfileRequire(report,state.Config(true,true,2,NativeMenuPage.Characters,3,_=>true) == NativeCharacterProfileResult.Ignored,"ready guard");
                ProfileRequire(report,state.Config(false,false,2,NativeMenuPage.Characters,3,_=>true) == NativeCharacterProfileResult.Ignored,"unjoined guard");
                ProfileRequire(report,state.Config(true,false,0,NativeMenuPage.Characters,3,_=>true) == NativeCharacterProfileResult.Opened && state.CurrentIndex == -1,"Default profile opens without saved presets");
                ProfileRequire(report,state.Cancel() == NativeCharacterProfileResult.Cancelled,"Default-only profile closes");
                ProfileRequire(report,state.Config(true,false,2,NativeMenuPage.Characters,3,_=>true) == NativeCharacterProfileResult.Opened,"open stores return target");
                ProfileRequire(report,state.Navigate(0,2) == NativeCharacterProfileResult.Ignored && state.Navigate(2,2) == NativeCharacterProfileResult.Ignored && state.CurrentIndex == -1,"vertical no-op");
                state.Navigate(3,2); ProfileRequire(report,state.CurrentIndex == 1,"left wraps Default to last");
                state.Navigate(1,2); ProfileRequire(report,state.CurrentIndex == -1,"right wraps last to Default");
                state.Navigate(1,2); ProfileRequire(report,state.Confirm(_=>false) == NativeCharacterProfileResult.Rejected && state.Open && state.SelectedIndex == -1,"reject preserves pending and committed");
                ProfileRequire(report,state.Cancel() == NativeCharacterProfileResult.Cancelled && state.CurrentIndex == 0 && state.SelectedIndex == -1,"cancel keeps pending without commit");
                state.Config(true,false,2,NativeMenuPage.Vehicles,1,_=>true);
                ProfileRequire(report,state.CurrentIndex == 0 && state.Config(true,false,2,NativeMenuPage.Vehicles,1,_=>true) == NativeCharacterProfileResult.Applied && state.SelectedIndex == 0,"Use applies pending");
                ProfileRequire(report,state.ReturnPage == NativeMenuPage.Vehicles && state.ReturnIndex == 1,"cached target intact");
            }
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
            var previousBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var gamepad = InputSystem.AddDevice<Gamepad>();
            try
            {
                foreach (var mode in new[] { NativeSessionMode.GrandPrix, NativeSessionMode.TimeTrials, NativeSessionMode.QuickRace, NativeSessionMode.CustomRace })
                {
                    selectedMode = mode; UpdateModeSummary(); OpenModeCharacters();
                    while (hud.MenuTransitionActive) yield return null;
                    yield return null; yield return null;
                    bool canJoin = mode != NativeSessionMode.TimeTrials;
                    ProfileRequire(report,hud.AdditionalPlayersCanJoin==canJoin,mode + " native mode join gate");
                    for (int human=1;human<4;human++)
                    {
                        var visible = hud.CharacterProfileOtherBoxVisibility(human);
                        ProfileRequire(report,visible[0]==!canJoin && visible[1]==canJoin && visible[2]==canJoin,mode+" P"+(human+1)+" blocked/join/p flags");
                    }
                }
                selectedMode = NativeSessionMode.TimeTrials; UpdateModeSummary(); report.fixtureMode = selectedMode.ToString();
                var emptyFixture = NativeUserSettings.CopyData(); emptyFixture.inputProfiles = new NativeSaveData.InputProfile[0];
                ProfileRequire(report,NativeSaveStore.ReplaceData(emptyFixture),"empty profile fixture passes production save validation");
                OpenModeCharacters(); while (hud.MenuTransitionActive) yield return null;
                yield return ProfileKeyboard(report,keyboard,Key.Space,"MenuMove");
                ProfileRequire(report,!characterProfiles[0].Open,"actual zero-profile Use rejection");
                var profileFixture = NativeUserSettings.CopyData();
                profileFixture.inputProfiles = new[] {
                    new NativeSaveData.InputProfile { index=0,profileId="profile-check-keyboard",name="Keyboard fixture",devicePath="<Keyboard>",overrides=new NativeSaveData.InputOverride[0] },
                    new NativeSaveData.InputProfile { index=1,profileId="profile-check-gamepad",name="Gamepad fixture",devicePath="<Gamepad>",overrides=new NativeSaveData.InputOverride[0] } };
                ProfileRequire(report,NativeSaveStore.ReplaceData(profileFixture),"two unique-index profiles pass production save validation");
                ProfileRequire(report,NativeInputBindings.SelectProfile(""),"fixture starts from valid default applied controls");
                OpenModeCharacters(); hud.SelectMenuIndex(0);
                while (hud.MenuTransitionActive) yield return null;
                foreach (var page in new[] { NativeMenuPage.Characters,NativeMenuPage.Vehicles,NativeMenuPage.Skins })
                {
                    characterProfiles[0] = new NativeCharacterProfileState(0);
                    hud.SetMenuPage(page); hud.SelectMenuIndex(0);
                    while (hud.MenuTransitionActive) yield return null;
                    yield return null; yield return null;
                    yield return new WaitForEndOfFrame();
                    CaptureProfilePreview(report,"before-open");
                    int actor = selectionPreviewModel.GetInstanceID(), camera = selectionPreviewCamera.GetInstanceID(), texture = selectionPreviewTexture.GetInstanceID();
                    int ci=previewCharacter,vi=previewVehicle,si=previewSkin;
                    yield return ProfileKeyboard(report,keyboard,Key.Space,"CharacterSubMove");
                    var state=characterProfiles[0];
                    var openPreview=CaptureProfilePreview(report,"open-default");
                    ProfileRequire(report,!openPreview.rawActive,"Profile hides Joined RawImage while retaining RT");
                    ProfileRequire(report,state.Open && hud.CharacterProfileActive(0),page + " Use.started opens original panel");
                    ProfileRequire(report,hud.CharacterProfileNameColor(0)==Color.white,page + " Default white");
                    Quaternion pausedRotation = selectionPreviewModel.transform.localRotation;
                    yield return ProfileKeyboard(report,keyboard,Key.RightArrow,"CharacterSubMove",true);
                    ProfileRequire(report,selectionPreviewModel.transform.localRotation==pausedRotation,"Profile hides Joined and suspends actor rotation");
                    var heldPreview=CaptureProfilePreview(report,"held-navigation");
                    ProfileRequire(report,heldPreview.pixelSHA256==openPreview.pixelSHA256,"hidden Profile keeps exact rendered RT pixels during two-second hold");
                    ProfileRequire(report,state.CurrentIndex==0 && hud.CharacterProfileNameColor(0)==Color.white,"compatible actual keyboard profile white");
                    yield return ProfileKeyboard(report,keyboard,Key.UpArrow,null);
                    yield return ProfileKeyboard(report,keyboard,Key.DownArrow,null);
                    ProfileRequire(report,state.CurrentIndex==0,"actual Up/Down do not change pending");
                    yield return ProfileKeyboard(report,keyboard,Key.RightArrow,"CharacterSubMove");
                    ProfileRequire(report,state.CurrentIndex==1 && hud.CharacterProfileNameColor(0)==Color.red,"incompatible Gamepad profile red although gamepad connected");
                    yield return ProfileKeyboard(report,keyboard,Key.Z,"MenuMove");
                    ProfileRequire(report,state.Open && state.SelectedIndex==-1,"Ok rejects incompatible");
                    yield return ProfileKeyboard(report,keyboard,Key.Space,"MenuMove");
                    ProfileRequire(report,state.Open && state.SelectedIndex==-1,"Use rejects incompatible");
                    yield return ProfileKeyboard(report,keyboard,Key.X,"MenuMove");
                    var cancelPreview=CaptureProfilePreview(report,"after-cancel");
                    ProfileRequire(report,cancelPreview.rawActive,"Cancel restores Joined RawImage visibility");
                    ProfileRequire(report,!state.Open && state.CurrentIndex==1 && hud.MenuPage==page && hud.SelectedMenuIndex==0,"Cancel restores exact cached target and keeps pending");
                    yield return ProfileKeyboard(report,keyboard,Key.Space,"CharacterSubMove");
                    ProfileRequire(report,state.CurrentIndex==1,"reopening keeps pending");
                    yield return ProfileKeyboard(report,keyboard,Key.LeftArrow,"CharacterSubMove");
                    yield return ProfileKeyboard(report,keyboard,Key.Z,"CharacterSubSelect",true);
                    var applyPreview=CaptureProfilePreview(report,"after-compatible");
                    ProfileRequire(report,applyPreview.rawActive,"compatible apply restores Joined RawImage visibility");
                    ProfileRequire(report,!state.Open && state.SelectedIndex==0 && NativeInputBindings.SelectedProfile()=="profile-check-keyboard" && hud.MenuPage==page && hud.SelectedMenuIndex==0,"Ok applies compatible controls");
                    yield return ProfileKeyboard(report,keyboard,Key.Space,"CharacterSubMove");
                    yield return ProfileKeyboard(report,keyboard,Key.LeftArrow,"CharacterSubMove");
                    yield return ProfileKeyboard(report,keyboard,Key.Space,"CharacterSubSelect",true);
                    var defaultPreview=CaptureProfilePreview(report,"after-default");
                    ProfileRequire(report,defaultPreview.rawActive,"Default apply restores Joined RawImage visibility");
                    ProfileRequire(report,!state.Open && state.SelectedIndex==-1 && NativeInputBindings.SelectedProfile()=="" && hud.MenuPage==page && hud.SelectedMenuIndex==0,"Use applies Default controls");
                    ProfileRequire(report,actor==selectionPreviewModel.GetInstanceID() && camera==selectionPreviewCamera.GetInstanceID() && texture==selectionPreviewTexture.GetInstanceID()
                        && ci==previewCharacter && vi==previewVehicle && si==previewSkin,page + " actor camera RT and selection identities retained");
                    ProfileRequire(report,NativeInputBindings.AppliedProfileDevice()==keyboard,"default retains actual human keyboard device");
                }
                report.realKeyboardEvents=true;
            }
            finally { InputSystem.RemoveDevice(gamepad); InputSystem.RemoveDevice(keyboard); InputSystem.settings.backgroundBehavior=previousBackground; }
#else
            throw new PlatformNotSupportedException("Character Profile device verification requires Player Input System");
#endif
        }
        void CaptureProfileControls(CharacterProfileVerificationReport report,string screenshot)
        {
            var state=characterProfiles[0];var profiles=NativeInputBindings.Profiles;
            string name=state.CurrentIndex==-1 ? "Default" : profiles[state.CurrentIndex].name;
            bool compatible=state.CurrentIndex==-1 || NativeInputBindings.CanSelectProfile(profiles[state.CurrentIndex].profileId);
            var sample=new CharacterProfileVisibleSample { page=hud.MenuPage.ToString(),screenshot=screenshot,expectedName=name,pendingIndex=state.CurrentIndex,
                compatible=compatible,controls=hud.ReadCharacterProfileControls(0),visiblePixels=new int[3] };
            report.visibleControls.Add(sample);
            for (int index=0;index<sample.controls.Length;index++)
            {
                var control=sample.controls[index];var rect=control.screenRect;var box=control.boxRect;
                ProfileRequire(report,control.activeSelf && control.activeInHierarchy && control.graphicEnabled && !control.cull
                    && control.color.a>0 && control.inheritedAlpha>0 && control.canvasAlpha>0,control.path+" actual visible hierarchy/graphic/alpha");
                ProfileRequire(report,rect.width>1 && rect.height>1 && rect.xMin>=box.xMin-1 && rect.xMax<=box.xMax+1
                    && rect.yMin>=box.yMin-1 && rect.yMax<=box.yMax+1,control.path+" actual screen rect within P1 box");
                if (index==0) ProfileRequire(report,control.text==name && control.color==(compatible ? Color.white : Color.red)
                    && control.textCharacters>0 && control.textVertices>0,control.path+" actual raw text/color/generated glyph vertices");
                else ProfileRequire(report,!string.IsNullOrEmpty(control.sprite),control.path+" exact authored arrow Sprite bound");
                int left=Mathf.Clamp(Mathf.FloorToInt(rect.xMin),0,Screen.width-1),bottom=Mathf.Clamp(Mathf.FloorToInt(Screen.height-rect.yMax),0,Screen.height-1);
                int width=Mathf.Min(Mathf.CeilToInt(rect.width),Screen.width-left),height=Mathf.Min(Mathf.CeilToInt(rect.height),Screen.height-bottom);
                var pixels=new Texture2D(width,height,TextureFormat.RGBA32,false);
                try
                {
                    pixels.ReadPixels(new Rect(left,bottom,width,height),0,0);pixels.Apply();
                    sample.visiblePixels[index]=pixels.GetPixels32().Count(color=> index==0 && !compatible ? color.r>=160 && color.g<90 && color.b<90
                        : index==0 ? color.r>=190 && color.g>=190 && color.b>=190
                        : color.r>=120 && color.g>=120 && color.b>=120 && Mathf.Abs(color.r-color.g)<20 && Mathf.Abs(color.r-color.b)<20);
                }
                finally { Destroy(pixels); }
                ProfileRequire(report,sample.visiblePixels[index]>=8,control.path+" actual framebuffer name/arrow pixels");
            }
        }
        CharacterProfilePreviewSample CaptureProfilePreview(CharacterProfileVerificationReport report,string stage)
        {
            string prefix="profile-preview-"+hud.MenuPage+"-"+stage;
            var raw=hud.PlayerBoxPreviewImage;
            var sample=new CharacterProfilePreviewSample { page=hud.MenuPage.ToString(),stage=stage,actorId=selectionPreviewModel.GetInstanceID(),
                cameraId=selectionPreviewCamera.GetInstanceID(),textureId=selectionPreviewTexture.GetInstanceID(),character=previewCharacter,vehicle=previewVehicle,skin=previewSkin,
                rawActive=raw.gameObject.activeInHierarchy,rawMatchesCamera=raw.texture==selectionPreviewCamera.targetTexture,textureCreated=selectionPreviewTexture.IsCreated(),
                rotation=selectionPreviewModel.transform.localRotation,screenshot=prefix+".png",renderTextureCapture=prefix+"-rt.png" };
            var previous=RenderTexture.active;
            var pixels=new Texture2D(selectionPreviewTexture.width,selectionPreviewTexture.height,TextureFormat.RGBA32,false);
            try
            {
                RenderTexture.active=selectionPreviewTexture;pixels.ReadPixels(new Rect(0,0,pixels.width,pixels.height),0,0);pixels.Apply();
                sample.nonzeroAlphaPixels=pixels.GetPixels32().Count(color=>color.a>0);
                byte[] png=pixels.EncodeToPNG();sample.pixelSHA256=ProfileSha256(png);File.WriteAllBytes(Path.Combine(reportDirectory,sample.renderTextureCapture),png);
            }
            finally { RenderTexture.active=previous;Destroy(pixels); }
            report.previews.Add(sample);
            ProfileRequire(report,sample.textureCreated && sample.rawMatchesCamera && sample.nonzeroAlphaPixels>100,stage+" actual retained RT pixels and Camera/Raw binding");
            ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory,sample.screenshot));return sample;
        }
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
        IEnumerator ProfileKeyboard(CharacterProfileVerificationReport report, Keyboard keyboard, Key key, string expected, bool hold=false)
        {
            int before=report.actualAudio.Count;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(key)); yield return null; yield return null;
            if(hold) {yield return new WaitForSecondsRealtime(2);}
            ProfileRequire(report,report.actualAudio.Count==before+(expected==null?0:1),key + " actual post-PlayOneShot count");
            if(expected!=null)
            {
                var clip = expected=="CharacterSubMove" ? NativePlayerBoxSoundData.Move : expected=="CharacterSubSelect" ? NativePlayerBoxSoundData.PlayerBoxSelect : NativePlayerBoxSoundData.PlayerBoxCancel;
                ProfileRequire(report,characterProfileObservedClips.Last()==clip,key + " actual original AudioClip object "+expected);
                ProfileRequire(report,report.audio.Last().listenerVolume==0 && report.audio.Last().uiVerificationPlayback,key + " callback verification listener0 boundary");
                var source=hud.OriginalSelectionMoveSource;
                ProfileRequire(report,source.enabled && source.outputAudioMixerGroup && source.outputAudioMixerGroup.name=="UI",key+" production enabled AudioSource/UI mixer");
            }
            if(characterProfiles[0].Open)
            {
                yield return new WaitForEndOfFrame();
                string name="profile-"+hud.MenuPage+"-"+report.screenshots.Count+".png";
                CaptureProfileControls(report,name);
                ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory,name)); report.screenshots.Add(name);
            }
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null; yield return null;
        }
#endif
    }
}
