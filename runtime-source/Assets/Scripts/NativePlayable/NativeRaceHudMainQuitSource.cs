using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public bool MainQuitSourceParentActive=>Menu2SourceMainParentActive;
        bool mainQuitSourcePanelActive;
        float mainQuitSourceFill=1;
        public void SetMainQuitSourceState(bool active,float fill){mainQuitSourcePanelActive=active;mainQuitSourceFill=fill;}
        public bool MainQuitSourcePanelMatches(bool active,float fill)=>authoredUi.MainQuitSourcePanelMatches(active,fill);
        void ApplyMainQuitSourcePanel(){authoredUi.SetMainQuitSourcePanel(mainQuitSourcePanelActive,mainQuitSourceFill);}
    }
    public sealed partial class NativeAuthoredUi
    {
        public GameObject CloneSelectionQuit(Transform parent)
        {
            var source=transforms["selection/Canvas/CharSelection/quitting"];
            var clone=UnityEngine.Object.Instantiate(source.gameObject,parent,false);
            clone.name="Original selection quit panel";
            foreach(Transform child in clone.transform)child.gameObject.SetActive(true);
            return clone;
        }
        public bool MainQuitSourcePanelMatches(bool active,float fill)
        {return transforms["menu2/Canvas/MainMenu/quitting"].gameObject.activeSelf==active && transforms["menu2/Canvas/MainMenu/quitting/quitbar"].GetComponent<Image>().fillAmount==fill;}
        public void SetMainQuitSourcePanel(bool active,float fill)
        {
            SetQuitPanel("menu2/Canvas/MainMenu/quitting",active,fill);
        }
        void SetQuitPanel(string root,bool active,float fill)
        {
            if(active)foreach(var node in Nodes(root+"/"))SetActive(node.scene+"/"+node.path,node.active);
            SetActive(root,active);
            transforms[root+"/quitbar"].GetComponent<Image>().fillAmount=Mathf.Clamp01(fill);
        }
    }
}
