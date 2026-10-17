using UnityEngine;
using System;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public const int SettingsPointerBack = -2, SettingsPointerConfirm = -3;
        public int SettingsPointerFocus { get; private set; } = -1;
        void SettingsHoverRow(int index, Rect rect)
        {
            if (!SuppressPointerInput && GUIUtility.hotControl == 0 && Application.isFocused && (pointerMoved || Event.current.type == EventType.MouseDown)
                && rect.Contains(Event.current.mousePosition)) SettingsPointerFocus = index;
        }
        int SettingsPromptPointer(Rect confirm, Rect back, int clicked)
        {
            if (SuppressPointerInput) return clicked;
            if (back.width > 0 && PointerButton(back)) return SettingsPointerBack;
            if (confirm.width > 0 && PointerButton(confirm)) return SettingsPointerConfirm;
            return clicked;
        }
        public int SettingsMenu(float width,string title,string[] rows,int selected,string status,string page="Root",NativeSettingsControl[] controls=null,bool dropdownOpen=false,int dropdownOption=0,Action<int> selectOption=null,Action cancelDropdown=null)
        {
            activeFont=menuFont; SettingsPointerFocus=-1;
            if(page=="Root")
            {
                const string root="menu2/Canvas/OptionsMenu/";
                const string buttons=root+"LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/";
                const string selectorPivot=root+"LowerOptionsRoot/LowerOptionsPivot/SelectorBG/SelectorPivot";
                authoredUi.Show("menu2","OptionsMenu","AssistButton");
                RestoreOptionsWheelCaptions();
                string[] names={"VideoButton","AudioButton","InputButton","LanguageButton"};int clicked=-1;
                var rects=new Rect[names.Length];var amounts=new float[names.Length];
                for(int i=0;i<names.Length;i++)
                {
                    string key=buttons+names[i];
                    float amount=amounts[i]=ButtonAmount(i,i==selected);
                    authoredUi.SetActive(key,true);
                    if (!authoredUi.ApplyMenuButton(key, amount, i == selected)) continue;
                    rects[i]=authoredUi.RectAt(key);
                    SettingsHoverRow(i,rects[i]);
                    if(!SuppressPointerInput && PointerButton(rects[i]))clicked=i;
                }
                authoredUi.Offset(selectorPivot,Vector2.zero);
                float target=SelectorPosition(rects[Mathf.Clamp(selected,0,rects.Length-1)].center.y+MenuSelectorOffset);
                authoredUi.Offset(selectorPivot,new Vector2(0,target-authoredUi.RectAt(selectorPivot).center.y));
                LocalizeOriginalPrompts(root);
                clicked=SettingsPromptPointer(authoredUi.RectAt(root+"Prompts/AText"),authoredUi.RectAt(root+"Prompts/BText"),clicked);
                if(Event.current.type==EventType.Repaint)
                    MenuFocus=new MenuFocusSnapshot{page="Options",index=selected,selectorVisible=true,
                        selectorY=authoredUi.RectAt(selectorPivot).center.y,buttonAmounts=amounts,buttonRects=rects};
                return clicked;
            }
            if((page=="Video" || page=="Audio" || page=="Language") && controls!=null)return AuthoredSettings(page,controls,selected,dropdownOpen,dropdownOption,selectOption,cancelDropdown);
            if(page=="Profiles" || page=="ProfileDevices" || page=="EditProfile" || page=="DeleteProfile")
            {
                var rects=authoredUi.ProfilesPage(page,rows,controls,selected,!SuppressPointerInput);
                LocalizeOriginalPrompts("profiles/Canvas/"+(page=="EditProfile"?"Rebindings":"Profiles")+"/Prompts/");
                int clicked=-1;
                for(int i=0;i<rects.Length;i++)if(rects[i].width>0 && rects[i].height>0){SettingsHoverRow(i,rects[i]);if(!SuppressPointerInput && PointerButton(rects[i]))clicked=i;}
                string prompts="profiles/Canvas/"+(page=="EditProfile"?"Rebindings":"Profiles")+"/Prompts/";
                return SettingsPromptPointer(authoredUi.RectAt(prompts+"AText"),authoredUi.RectAt(prompts+"BText"),clicked);
            }
            string scene=page=="Profiles" || page=="EditProfile"?"profiles":"menu2";
            string original=page=="Audio"?"AudioOptions":page=="Language"?"LanguageOptions":page=="Profiles"?"Profiles":page=="EditProfile"?"Rebindings":"VideoOptions";
            authoredUi.Show(scene,original,"/Scroll View","/ScreenOptions","/Tabs","/PreviewDescription","/Prompts","/NewSelectDevice","/ConfirmDeletionPanel");
            Rect box=AuthoredRect(scene,original+"/"+(scene=="profiles"?"ProfilesBox":"VideoBox"));
            Label(new Rect(box.x+15,box.y+10,box.width-30,45),title,29,Color.white,TextAnchor.MiddleCenter);
            int first=Mathf.Clamp(selected-4,0,Mathf.Max(0,rows.Length-8));int chosen=-1;
            for(int i=first;i<Mathf.Min(rows.Length,first+8);i++)
            {
                Rect rect=new Rect(box.x+12,box.y+67+(i-first)*47,box.width-24,44);
                SpritePicture(rect,resultEntry,true);
                if(i==selected)Fill(rect,new Color(.4f,.5f,.65f,.45f));
                Label(new Rect(rect.x+10,rect.y,rect.width-20,rect.height),rows[i],22,i==selected?Color.yellow:Color.white);
                SettingsHoverRow(i,rect);
                if(!SuppressPointerInput && PointerButton(rect))chosen=i;
            }
            Label(new Rect(box.x+15,box.yMax-36,box.width-30,28),(selected+1)+" / "+rows.Length,17,Color.white,TextAnchor.MiddleRight);
            Label(new Rect(28,480,Mathf.Max(180,box.x-56),80),status,19,Color.white,TextAnchor.MiddleCenter);
            return chosen;
        }
        string settingsRenderedPage;
        float settingsScroll;
        int settingsLastFocus=-1;
        public bool SettingsAuthoredReady => authoredUi != null;
        public void CloseSettingsDropdown()=>authoredUi?.ClearSettingsDropdown();
        int AuthoredSettings(string page,NativeSettingsControl[] controls,int selected,bool dropdownOpen,int dropdownOption,Action<int> selectOption,Action cancelDropdown)
        {
            string original=page=="Audio"?"AudioOptions":page=="Language"?"LanguageOptions":"VideoOptions";
            if(settingsRenderedPage!=page){settingsRenderedPage=page;settingsScroll=0;settingsLastFocus=-1;authoredUi.ClearSettingsDropdown();}
            authoredUi.ShowSettingsPage(original);
            string content=original+"/VideoBox/Scroll View/Viewport/Content/";
            authoredUi.SettingsLayout(original,settingsScroll);
            Rect viewport=authoredUi.SettingsRectAt("menu2/Canvas/"+original+"/VideoBox/Scroll View/Viewport");
            if(!dropdownOpen && !SuppressPointerInput && Event.current.type==EventType.ScrollWheel && viewport.Contains(Event.current.mousePosition)){settingsScroll+=Event.current.delta.y*32;Event.current.Use();}
            authoredUi.SettingsLayout(original,settingsScroll);
            if(selected>=0 && selected<controls.Length && selected!=settingsLastFocus && controls[selected]!=null){settingsScroll=authoredUi.SettingsScrollTo(original,controls[selected].node,settingsScroll);settingsLastFocus=selected;authoredUi.SettingsLayout(original,settingsScroll);}
            if(!dropdownOpen)
            {
                settingsScroll=authoredUi.SettingsScrollbarPointer("menu2/Canvas/"+original+"/VideoBox/Scroll View",settingsScroll,!SuppressPointerInput);
                authoredUi.SettingsLayout(original,settingsScroll);
            }
            int clicked=-1;
            for(int i=0;i<controls.Length;i++)
            {
                var control=controls[i];if(control==null)continue;
                authoredUi.SettingsControl(original,control,i==selected && !dropdownOpen);
                Rect row=authoredUi.SettingsRectAt("menu2/Canvas/"+content+control.node);
                if(!row.Overlaps(viewport) || dropdownOpen || SuppressPointerInput)continue;
                Rect visible=Rect.MinMaxRect(Mathf.Max(row.x,viewport.x),Mathf.Max(row.y,viewport.y),Mathf.Min(row.xMax,viewport.xMax),Mathf.Min(row.yMax,viewport.yMax));
                SettingsHoverRow(i,visible);
                if(control.options!=null){if(PointerButton(visible))clicked=i;}
                else
                {
                    Rect slider=authoredUi.SettingsRectAt("menu2/Canvas/"+content+control.node+"/Value/Slider");
                    if(Event.current.type==EventType.MouseDown && Event.current.button==0 && slider.Contains(Event.current.mousePosition))clicked=i;
                    float value=GUI.HorizontalSlider(slider,control.normalized,0,1,GUIStyle.none,GUIStyle.none);
                    if(Mathf.Abs(value-control.normalized)>.0001f)control.setNormalized(value);
                }
            }
            var confirmPrompt=new Rect(610,536,115,42);var backPrompt=new Rect(720,536,165,42);
            if(dropdownOpen && controls[selected]!=null)
            {
                var options=authoredUi.SettingsDropdown(original,controls[selected],dropdownOption);
                if(!SuppressPointerInput)for(int i=0;i<options.Length;i++)if(PointerButton(options[i])){selectOption?.Invoke(i);break;}
                Rect popup=authoredUi.SettingsRectAt("menu2/Canvas/"+content+controls[selected].node+"/Value/Dropdown/Template");
                if(!SuppressPointerInput && Event.current.type==EventType.MouseUp && Event.current.button==0 && !popup.Contains(Event.current.mousePosition) && !confirmPrompt.Contains(Event.current.mousePosition) && !backPrompt.Contains(Event.current.mousePosition))
                {cancelDropdown?.Invoke();Event.current.Use();}
            }
            Prompt(confirmPrompt,keyConfirm,"Select",24);
            Prompt(backPrompt,keyCancel,"Go Back",24);
            return SettingsPromptPointer(confirmPrompt,backPrompt,clicked);
        }
        public string SettingsNameField(float width,string value)
        {
            GUI.SetNextControlName("NativeProfileName");GUIStyle style=new GUIStyle(GUI.skin.textField){font=font,fontSize=22};
            return GUI.TextField(new Rect(75,115,width-150,35),value,128,style);
        }
    }
}
