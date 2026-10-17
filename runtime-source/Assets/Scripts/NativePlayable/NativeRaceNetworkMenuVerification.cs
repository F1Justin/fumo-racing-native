using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        internal bool onlineUiFixture;

#if ENABLE_INPUT_SYSTEM
        [Serializable] sealed class OnlineMenuReceipt
        {
            public bool passed,physicalInputAccepted;
            public string scope="Actual rendered Player UI with virtual Keyboard navigation and TMP editing events; real loopback TCP broker protocol. Results rendering uses authority-state fixtures.",error;
            public List<string> checks=new List<string>();
        }
        void OnlineRequire(OnlineMenuReceipt report,bool condition,string check)
        {report.checks.Add((condition?"PASS ":"FAIL ")+check);if(!condition)throw new InvalidOperationException(check);}
        public IEnumerator CheckOnlineMenuFlow(string directory)
        {
            var receipt=new OnlineMenuReceipt();Directory.CreateDirectory(directory);
            var stack=new Stack<IEnumerator>();stack.Push(CheckOnlineMenuCore(receipt,directory));
            while(stack.Count>0)
            {
                bool next;object value=null;
                try{next=stack.Peek().MoveNext();if(next)value=stack.Peek().Current;}
                catch(Exception failure){receipt.error=failure.ToString();break;}
                if(!next){stack.Pop();if(stack.Count==0)receipt.passed=true;continue;}
                if(value is IEnumerator nested && !(value is CustomYieldInstruction)){stack.Push(nested);continue;}
                yield return value;
            }
            onlineUiFixture=false;
            File.WriteAllText(Path.Combine(directory,"online-menu.json"),JsonUtility.ToJson(receipt,true));
            Application.Quit(receipt.passed?0:1);
        }
        static void SaveOnlineTexture(Texture source,string path)
        {
            var previous=RenderTexture.active;
            var target=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var pixels=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,true);
            try
            {
                Graphics.Blit(source,target);RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,source.width,source.height),0,0);pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Destroy(pixels);}
        }
        IEnumerator CaptureOnline(string path)
        {yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(path);yield return null;yield return new WaitForEndOfFrame();}
        IEnumerator WaitOnline(Func<bool> condition,string name,Action tick=null)
        {
            float deadline=Time.realtimeSinceStartup+12;
            while(!condition()){tick?.Invoke();if(Time.realtimeSinceStartup>deadline)throw new TimeoutException(name);yield return null;}
        }
        IEnumerator CheckOnlineMenuCore(OnlineMenuReceipt report,string directory)
        {
            Application.runInBackground=true;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            var keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();NativeInputBindings.SelectProfile("");NativeInputBindings.Initialize();
            onlineUiFixture=true;Open=true;Race.NetworkOpenMenu(keyboard);playerId=null;mode=null;
            // One authority plus two participants and an independent viewer share real transport frames.
            using(var fixtureBroker=new NativeNetBroker())
            {
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();fixtureBroker.Start(port);
                var connections=new[]{new NativeNetTransport(),new NativeNetTransport(),new NativeNetTransport()};
                var received=new[]{new List<NativeNetMessage>(),new List<NativeNetMessage>(),new List<NativeNetMessage>()};
                Action pump=()=>{fixtureBroker.Pump();for(int i=0;i<3;i++){NativeNetMessage packet;while(connections[i].TryReceive(out packet))received[i].Add(packet);}};
                try
                {
                    foreach(var connection in connections)connection.Connect("127.0.0.1",port);
                    yield return WaitOnline(()=>connections.All(c=>c.IsConnected),"TCP connect",pump);
                    connections[0].Send(new NativeNetMessage{kind="create",room="online-menu",name="Host",capacity=8});
                    yield return WaitOnline(()=>received[0].Any(m=>m.kind=="welcome"),"host welcome",pump);
                    connections[1].Send(new NativeNetMessage{kind="join",room="online-menu",name="Marisa"});
                    connections[2].Send(new NativeNetMessage{kind="join",room="online-menu",name="Observer",spectator=true});
                    yield return WaitOnline(()=>received[2].Any(m=>m.kind=="welcome")&&received[1].Any(m=>m.kind=="welcome"),"join",pump);
                    var identity=received.Select(messages=>messages.First(m=>m.kind=="welcome")).ToArray();
                    Action<int,string,string,bool> send=(index,kind,payload,ready)=>connections[index].Send(new NativeNetMessage{kind=kind,room="online-menu",player=identity[index].player,seat=identity[index].seat,round=1,payload=payload,ready=ready,sequence=kind=="results"||kind=="snapshot"?1:0});
                    OnlineRequire(report,identity[0].host&&identity[2].spectator&&identity[2].seat>=8,"observer has independent seat and host authority is explicit");
                    var customWire = new NativeNetworkConfig { mode = NativeSessionMode.CustomRace, racers = 100, laps = 7, itemType = 1, humanSeats = new[] { 0, 1 }, customRules = new NativeCustomRaceRules { topSpeed = 150, itemRespawn = 1 } };
                    send(0,"config",JsonUtility.ToJson(customWire),true);
                    yield return WaitOnline(()=>received[1].Any(m=>m.kind=="config"),"custom config broadcast",pump);
                    var synced = JsonUtility.FromJson<NativeNetworkConfig>(received[1].Last(m=>m.kind=="config").payload);
                    OnlineRequire(report,synced.racers==100&&synced.laps==7&&synced.itemType==1&&synced.customRules.topSpeed==150&&synced.humanSeats.SequenceEqual(new[]{0,1}),"real TCP broker synchronizes 100-racer custom configuration and grid identities");
                    send(0,"ready",null,true);send(1,"ready",null,true);
                    yield return WaitOnline(()=>received[0].Last(m=>m.kind=="lobby").peers.Count(p=>p.ready)==2,"ready",pump);
                    send(0,"setup",null,true);
                    yield return WaitOnline(()=>received[0].Last(m=>m.kind=="lobby").setup,"setup acknowledgement",pump);
                    send(0,"candidate","mountains",true);send(1,"ready",null,false);
                    yield return WaitOnline(()=>received[1].Any(m=>m.candidateTrack=="mountains")&&received[0].Last(m=>m.kind=="lobby").peers.Any(p=>!p.spectator&&!p.ready),"setup candidate",pump);
                    OnlineRequire(report,received[0].Last(m=>m.kind=="lobby").setup,"unready preserves shared setup phase and candidate track");
                    send(1,"ready",null,true);
                    yield return WaitOnline(()=>received[0].Last(m=>m.kind=="lobby").peers.Count(p=>p.ready)==2,"re-ready",pump);
                    send(0,"start",JsonUtility.ToJson(Config),true);
                    yield return WaitOnline(()=>received[2].Any(m=>m.kind=="start"),"start",pump);
                    OnlineRequire(report,received[2].Any(m=>m.kind=="start"),"unready spectator never blocks participant start");
                    connections[2].Dispose();pump();
                    yield return WaitOnline(()=>received[0].Last(m=>m.kind=="lobby").peers.Any(p=>p.spectator&&!p.connected),"observer detach",pump);
                    OnlineRequire(report,!received[0].Any(m=>m.kind=="abort"),"observer disconnect leaves race running");
                    connections[2]=new NativeNetTransport();connections[2].Connect("127.0.0.1",port);
                    yield return WaitOnline(()=>connections[2].IsConnected,"reconnect transport",pump);
                    connections[2].Send(new NativeNetMessage{kind="resume",room="online-menu",player=identity[2].player,seat=identity[2].seat,resumeToken=identity[2].resumeToken});
                    yield return WaitOnline(()=>received[0].Last(m=>m.kind=="lobby").peers.All(p=>p.connected),"reconnect connected",pump);
                    OnlineRequire(report,received[2].Last(m=>m.kind=="welcome").spectator,"reconnect retains spectator identity");
                    var result=new NativeNetworkSnapshot{phase="Finished",karts=new[]{new NativeNetworkKart{seat=0,finished=true,place=1,finish=40},new NativeNetworkKart{seat=1,finished=true,place=2,finish=45}}};
                    send(0,"results",JsonUtility.ToJson(result),true);
                    yield return WaitOnline(()=>received[1].Any(m=>m.kind=="results"),"results",pump);
                    send(0,"return-room",null,true);
                    yield return WaitOnline(()=>received[0].Any(m=>m.kind=="return-room"),"personal return",pump);
                    OnlineRequire(report,!received[1].Any(m=>m.kind=="return-room"||m.kind=="abort"||m.kind=="rematch"),"host personal return preserves guest results and connection");
                    OnlineRequire(report,received[0].Last(m=>m.kind=="lobby").peers.First(p=>p.seat==1).returned==false,"guest must return and ready before next start");
                    send(1,"return-room",null,true);send(2,"return-room",null,true);
                    yield return WaitOnline(()=>received[1].Any(m=>m.kind=="return-room")&&received[2].Any(m=>m.kind=="return-room"),"everyone returned",pump);
                    OnlineRequire(report,received[0].Last(m=>m.kind=="lobby").peers.All(p=>!p.ready),"post-race return resets readiness");
                    OnlineRequire(report,!received[0].Last(m=>m.kind=="lobby").setup&&string.IsNullOrEmpty(received[0].Last(m=>m.kind=="lobby").candidateTrack),"new round resets setup gate while preserving race configuration");
                    send(0,"ready",null,true);send(1,"ready",null,true);
                    yield return WaitOnline(()=>received[0].Last(m=>m.kind=="lobby").peers.Count(p=>p.ready)==2,"re-ready",pump);
                    send(0,"start",JsonUtility.ToJson(Config),true);
                    yield return WaitOnline(()=>received[1].Count(m=>m.kind=="start")==2,"second round",pump);
                    OnlineRequire(report,received[0].All(m=>m.kind!="error"),"second round starts with same network identities");
                }
                finally{foreach(var connection in connections)connection.Dispose();}
            }
            IsHost=true;playerId="fixture";LocalSeat=0;InRace=false;Loading=false;Config.racers=8;
            Peers=Enumerable.Range(0,8).Select(i=>new NativeNetPeer{id="p"+i,seat=i,name="Player "+i+" Long nickname",ready=true}).ToArray();
            Config.selections=Enumerable.Range(0,8).Select(i=>new NativeNetworkSelection{seat=i,character=Race.vehicleRoster.CharacterNameAt(i),vehicle=Race.vehicleRoster.VehicleNameAt(i%Race.vehicleRoster.VehicleCount),skin=0}).ToArray();
            setupActive=setupPage=true;onlineChoice=OnlineChoice.None;onlineFocus="laps";
            ChangeNumber("laps",1);int laps=Config.laps;ChangeNumber("laps",-1);
            OnlineRequire(report,Config.laps==laps-1&&onlineFocus=="laps","left/right numeric adjustment retains focus");
            Peers[1].ready=false;OnlineRequire(report,!RoomReadyToStart&&setupPage&&setupActive,"unready disables start without closing setup");
            invitationAddress="myhost.example";invitationPort="8899";OnlineRequire(report,InvitationEndpoint()=="myhost.example:8899","invite contains external hostname and non-default port");
            invitationAddress="2001:db8::1";OnlineRequire(report,InvitationEndpoint()=="[2001:db8::1]:8899","IPv6 invite brackets and port");
            setupPage=false;setupActive=false;InRace=false;SetOnlineChoice(OnlineChoice.None);
            ConfirmOnlineAction(()=>{},"Close Room");yield return new WaitForEndOfFrame();
            OnlineRequire(report,onlineFocus=="stay"&&onlineButtons.All(b=>b.key=="stay"||b.key=="confirm-close"),"confirmation focus and controls are trapped in modal");DismissOnlineConfirmation();
            OnlineRequire(report,Status!="Close Room","dismissed confirmation restores its previous page status");
            IsHost=false;InRace=true;Loading=false;Round=1;lastResultsRound=-1;ResetPersonalResults();
            Latest=new NativeNetworkSnapshot{phase="Racing",karts=Enumerable.Range(0,8).Select(i=>new NativeNetworkKart{seat=i,finished=i==0,place=i+1,finish=i==0?43.567f:0}).ToArray()};
            float started=Time.unscaledTime;TickPersonalResults();yield return new WaitForEndOfFrame();
            yield return CaptureOnline(Path.Combine(directory,"finish-camera.png"));
            yield return new WaitForSecondsRealtime(2.7f);
            OnlineRequire(report,!PersonalResultsVisible,"finish camera stays visible before default three seconds");
            yield return WaitOnline(()=>PersonalResultsVisible,"three second result reveal");
            OnlineRequire(report,Time.unscaledTime-started>=3&&!CanReturnToRoom,"three second reveal displays partial results with return disabled");
            yield return new WaitForSecondsRealtime(.3f);
            yield return CaptureOnline(Path.Combine(directory,"results-en-waiting.png"));
            OnlineRequire(report,onlineButtons.All(b=>b.key!="room"),"waiting results omit return prompt");
            ResetPersonalResults();TickPersonalResults();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Z));yield return null;yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            OnlineRequire(report,PersonalResultsVisible&&!personalReturnPending,"virtual Z opens results early and never returns in same press");
            foreach(var kart in Latest.karts){kart.finished=true;kart.finish=43.567f+kart.seat;}lastResultsRound=Round;CompletedRounds=1;
            yield return new WaitForSecondsRealtime(.35f);yield return new WaitForEndOfFrame();
            OnlineRequire(report,CanReturnToRoom&&onlineButtons.Any(b=>b.key=="room"),"all-finished results keep visible with return prompt");
            yield return CaptureOnline(Path.Combine(directory,"results-en-complete.png"));
            NativeUserSettings.TrySetInt("lang_game",3);yield return null;yield return new WaitForEndOfFrame();
            yield return CaptureOnline(Path.Combine(directory,"results-cn-complete.png"));
            var labels=UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>().Where(t=>t.GetComponentInParent<NativeOnlinePageView>() != null).Select(t=>t.text).ToArray();
            OnlineRequire(report,labels.Contains("返回房间")&&labels.Contains("比赛结束"),"Chinese complete status and return caption render");
            requestedSpectator=true;onlinePage=null;onlineFocus=null;yield return null;yield return new WaitForEndOfFrame();
            OnlineRequire(report,onlineFocus=="room","spectator final results focus the return action for confirmation input");
            requestedSpectator=false;
            NativeUserSettings.TrySetInt("lang_game",0);InRace=false;Latest=null;playerId=null;setupPage=false;connectionStep=OnlineConnectionStep.Menu;onlineFocus="host-direct";
            yield return null;yield return new WaitForEndOfFrame();yield return CaptureOnline(Path.Combine(directory,"online-entry.png"));
            OnlineRequire(report,onlineButtons.Any(b=>b.key=="host-direct")&&onlineButtons.Any(b=>b.key=="join-direct")&&!onlineButtons.Any(b=>b.key=="community"),"default original-wheel entry has direct host/join and hides community");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.DownArrow));yield return null;yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            OnlineRequire(report,onlineFocus=="join-direct","virtual keyboard arrow navigates original-wheel Online entry");
            onlineFocus="host-direct";BeginConnectionStep(OnlineConnectionStep.HostDirect,"host-direct");yield return null;yield return new WaitForEndOfFrame();
            yield return CaptureOnline(Path.Combine(directory,"online-host.png"));
            OnlineRequire(report,onlineFocus=="connect"&&onlineButtons.All(b=>b.key!="host-direct"),"host is a separate page with primary action focus");
            var portField=onlineFields["port"];portField.Select();portField.ActivateInputField();
            yield return null;yield return null;yield return new WaitForEndOfFrame();
            OnlineRequire(report,portField.isFocused,"input focus survives repeated production redraws");
            portField.MoveTextEnd(false);portField.ProcessEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.Alpha8,character='8'});yield return null;yield return null;
            OnlineRequire(report,Port.EndsWith("8"),"focused input receives TMP editing event without losing focus");
            portField.DeactivateInputField();
            CancelConnectionStep();OnlineRequire(report,onlineFocus=="host-direct","back restores entry origin focus");
            communityEnabled=false;serverConnection=false;Status="Room fumo";
            setupPage=setupActive=false;IsHost=true;playerId="fixture";InRace=false;Loading=false;automaticSetupRequested=false;
            readySent=true;Peers[1].ready=true;TryAutomaticRaceSetup();
            OnlineRequire(report,setupPage,"all participants ready automatically advances host to race setup");
            setupActive=true;ReturnFromRaceSetup();TryAutomaticRaceSetup();
            OnlineRequire(report,!readySent&&!setupPage&&setupActive,"setup back cancels host readiness and retains draft");
            readySent=true;automaticSetupRequested=false;TryAutomaticRaceSetup();
            OnlineRequire(report,setupPage&&setupActive,"re-ready automatically reopens retained setup without next button");
            Peers[1].ready=false;setupPage=setupActive=false;automaticSetupRequested=false;
            EditInvitationEndpoint("[2001:db8::1]:8899");
            OnlineRequire(report,InvitationEndpoint()=="[2001:db8::1]:8899","single invitation field parses bracketed IPv6 and port");

            requestedSpectator=false;InRace=false;playerId="fixture";IsHost=true;setupActive=false;setupPage=false;SetOnlineChoice(OnlineChoice.None);
            selection=Config.selections.First(c=>c.seat==LocalSeat);readySent=Peers.First(p=>p.seat==LocalSeat).ready;
            // Exercise real rendered row thumbnails and the material property blocks used by the race.
            foreach(var characterId in new[]{"alice","marisa","biten"})
            {
                int ci=Race.vehicleRoster.CharacterIndexOf(characterId);
                foreach(var vehicleId in new[]{"kart","bike","skate"})
                {
                    Texture previousSkin=null;
                    for(int skin=0;skin<Race.vehicleRoster.SkinCountAt(ci);skin++)
                    {
                        Race.NetworkSelectionPreview(characterId,vehicleId,skin);
                        var thumbnail=Race.NetworkRosterVehiclePreview(characterId,vehicleId,skin);
                        var rowModel=GameObject.Find("Original selection preview/Preview model");
                        var owner=rowModel.transform.GetChild(0).GetComponents<NativePreviewKartProperties>().Single(c=>c.selectionOwner);
                        Race.vehicleRoster.ColorsAt(ci,skin,out var first,out var second);
                        var block=new MaterialPropertyBlock();
                        bool matches=owner.targets.Length>0;
                        foreach(var target in owner.targets)
                        {
                            target.GetPropertyBlock(block);
                            matches&=block.GetColor("_Color")==first&&block.GetColor("_Color2")==second;
                        }
                        OnlineRequire(report,matches,"row thumbnail material palette matches selected "+characterId+" / "+vehicleId+" / skin "+skin);
                        OnlineRequire(report,thumbnail!=previousSkin&&ReferenceEquals(thumbnail,Race.NetworkRosterVehiclePreview(characterId,vehicleId,skin)),
                            "row thumbnail caches the chosen skin independently: "+characterId+" / "+vehicleId+" / "+skin);
                        SaveOnlineTexture(thumbnail,Path.Combine(directory,"roster-"+characterId+"-"+vehicleId+"-"+skin+".png"));
                        previousSkin=thumbnail;
                        yield return null;
                    }
                }
            }
            yield return null;yield return new WaitForEndOfFrame();yield return CaptureOnline(Path.Combine(directory,"lobby-eight-en.png"));
            NativeUserSettings.TrySetInt("lang_game",3);yield return null;yield return new WaitForEndOfFrame();yield return CaptureOnline(Path.Combine(directory,"lobby-eight-cn.png"));
            var titleRect = onlineLobby.transform.Find("Content/RoomTitle").GetComponent<RectTransform>();
            var savedPosition = titleRect.anchoredPosition; var savedSize = titleRect.sizeDelta;
            titleRect.anchoredPosition += new Vector2(11, -7); titleRect.sizeDelta += new Vector2(23, 5);
            var editedPosition = titleRect.anchoredPosition; var editedSize = titleRect.sizeDelta;
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            OnlineRequire(report, titleRect.anchoredPosition == editedPosition && titleRect.sizeDelta == editedSize,
                "runtime data refresh preserves manually edited prefab title position and size");
            titleRect.anchoredPosition = savedPosition; titleRect.sizeDelta = savedSize;
            var staticRects = new[] { onlineLobby.CharacterButton.Rect, onlineLobby.CopyButton.Rect, onlineLobby.SpectateButton.Rect, onlineLobby.ReadyButton.Rect,
                (RectTransform)onlineLobby.InvitationInput.transform, onlineLobby.transform.Find("Content/PlayersPanel").GetComponent<RectTransform>() };
            var positions = staticRects.Select(rect => rect.anchoredPosition).ToArray();
            var sizes = staticRects.Select(rect => rect.sizeDelta).ToArray();
            var mins = staticRects.Select(rect => rect.anchorMin).ToArray(); var maxs = staticRects.Select(rect => rect.anchorMax).ToArray();
            var preview=GameObject.Find("Original selection preview/Preview model");var rotation=preview.transform.localRotation;
            yield return new WaitForSecondsRealtime(1);
            OnlineRequire(report, staticRects.Select((rect,index) => rect.anchoredPosition == positions[index] && rect.sizeDelta == sizes[index]
                && rect.anchorMin == mins[index] && rect.anchorMax == maxs[index]).All(equal => equal),
                "static model, buttons, input and player panel geometry stays unchanged through redraws");
            OnlineRequire(report,Quaternion.Angle(rotation,preview.transform.localRotation)>10,"personal top preview continuously rotates with original selection clock");
            yield return CaptureOnline(Path.Combine(directory,"lobby-rotation-cn.png"));
            OnlineRequire(report,!onlineButtons.Any(b=>b.key=="setup")&&onlineButtons.Any(b=>b.key=="character"),"lobby uses selectable top model and removes next-step button");
            var role=onlineButtons.First(b=>b.key=="role");var ready=onlineButtons.First(b=>b.key=="ready");
            OnlineRequire(report,Mathf.Approximately(role.rect.yMax,570)&&Mathf.Approximately(ready.rect.yMax,570),"player panel, spectate and ready share bottom baseline");
            var invitation=onlineButtons.First(b=>b.key=="field-invite-address");var copy=onlineButtons.First(b=>b.key=="copy-invite");
            OnlineRequire(report,copy.rect.x>invitation.rect.xMax&&copy.rect.y==invitation.rect.y&&copy.rect.width<150,"compact copy button sits beside combined invitation input at original height");
            onlineFocus = "role"; yield return null; yield return new WaitForEndOfFrame();
            var selectedSurface = onlineLobby.SpectateButton.GetComponent<UnityEngine.UI.Image>();
            var idleSurface = onlineLobby.ReadyButton.GetComponent<UnityEngine.UI.Image>();
            OnlineRequire(report, selectedSurface.material.shader.name == "Fumo/UI/LobbySelectionGlow"
                && idleSurface.material.shader.name != "Fumo/UI/LobbySelectionGlow", "only the selected lobby button uses animated gold; idle remains gray");
            yield return CaptureOnline(Path.Combine(directory, "lobby-selected-gold-a.png"));
            yield return new WaitForSecondsRealtime(.65f);
            yield return CaptureOnline(Path.Combine(directory, "lobby-selected-gold-b.png"));
            var legacyCanvas = GameObject.Find("Native original UI Canvas");
            OnlineRequire(report, !legacyCanvas || !legacyCanvas.activeInHierarchy, "Online pages keep legacy NativeUiCanvas hidden");
            var motor = Race.racers[0]; var oldStats = motor.stats; motor.stats = oldStats.Copy();
            var moduleRoot = new GameObject("Online module authoring regression");
            var moduleHud = moduleRoot.AddComponent<NativeVehicleModuleHud>();
            foreach (var kind in new[] { NativeVehicleModuleKind.BroomAirBoost, NativeVehicleModuleKind.GenjiGrassBoost, NativeVehicleModuleKind.ShoppingCartDriftCap })
            {
                motor.stats._moduleKind = kind; moduleHud.Present(motor, Race.raceCamera, true, false, true);
                var before = moduleHud.ReadState();
                var view = moduleHud.AuthoredView;
                view.barRoot.anchoredPosition += new Vector2(13, -11); var savedModulePosition = view.barRoot.anchoredPosition;
                for (int repeat = 0; repeat < 30; repeat++) moduleHud.Present(motor, Race.raceCamera, true, false, true);
                OnlineRequire(report, view.barRoot.anchoredPosition == savedModulePosition && moduleHud.ReadState().nodeCount == before.nodeCount,
                    "Online " + kind + " retains authored module geometry without rebuilding nodes");
            }
            motor.stats = oldStats; UnityEngine.Object.Destroy(moduleRoot);
            var model=onlineButtons.First(b=>b.key=="character");ActivateOnlineButton(model);yield return null;
            OnlineRequire(report,onlineChoice==OnlineChoice.Characters&&!readySent,"selecting top model opens modification and clears readiness");
            yield return null; yield return new WaitForEndOfFrame();
            OnlineRequire(report,!onlineButtons.Any(b => b.key == "choice-back" || b.key == "choice-confirm"),
                "online selection omits footer buttons and retains original keyboard/controller prompts");
            yield return CaptureOnline(Path.Combine(directory, "online-character-prefab.png"));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Z)); yield return null; yield return null;
            Debug.Log("ONLINE_SELECTION_CONFIRM_TRACE choice=" + onlineChoice + " editing=" + OnlineEditing + " transition=" + OnlineHud.MenuTransitionActive + " consumed=" + onlineInputConsumedFrame + " frame=" + Time.frameCount + " open=" + Open + " inRace=" + InRace + " keyboard=" + keyboard.zKey.isPressed + " device=" + NativeInputBindings.ActualConfirmDevice());
            OnlineRequire(report, onlineChoice == OnlineChoice.Vehicles, "virtual Z confirms character without a footer button");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Z)); yield return null; yield return null;
            OnlineRequire(report, onlineChoice == OnlineChoice.Skins, "virtual Z confirms vehicle without a footer button");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.X)); yield return null; yield return null;
            OnlineRequire(report, onlineChoice == OnlineChoice.Vehicles, "virtual X returns from skin without a footer button");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.X)); yield return null; yield return null;
            OnlineRequire(report, onlineChoice == OnlineChoice.Characters, "virtual X returns from vehicle without a footer button");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            CancelOnlineChoice();readySent=Peers.First(p=>p.seat==LocalSeat).ready;
            yield return null;yield return new WaitForEndOfFrame();
            OnlineRequire(report,!onlineButtons.Any(b=>b.key=="leave")&&onlineButtons.Any(b=>b.key=="ready"&&b.rect.height>=70&&b.rect.x>Screen.width*600f/Screen.height*.5f),"lobby has large bottom-right ready and no close-room button");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.X));yield return new WaitForSecondsRealtime(.35f);
            OnlineRequire(report,lobbyExitHold.HoldingTime>.25f&&playerId!=null,"virtual X short hold displays progress and keeps room");
            yield return CaptureOnline(Path.Combine(directory,"lobby-hold-exit-cn.png"));
            var quit=onlineLobby ? onlineLobby.ExitControl : null;
            OnlineRequire(report,quit&&quit.transform.Find("quitbar").GetComponent<UnityEngine.UI.Image>().type==UnityEngine.UI.Image.Type.Filled,"room hold reuses original filled-image selection quitting panel");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
            OnlineRequire(report,!lobbyExitHeld&&lobbyExitHold.HoldingTime==0,"release resets room exit progress");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.X));yield return new WaitForSecondsRealtime(1.1f);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
            OnlineRequire(report,playerId==null&&Open&&connectionStep==OnlineConnectionStep.Menu,"full X hold closes room and returns to Online entry");
            OnlineRequire(report,errors.Count==0,"Player reports zero runtime errors");
        }
#else
        public IEnumerator CheckOnlineMenuFlow(string directory) {yield break;}
#endif
    }
}
