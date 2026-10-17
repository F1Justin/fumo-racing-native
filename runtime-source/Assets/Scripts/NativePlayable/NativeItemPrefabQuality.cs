using System;
using UnityEngine;
using UnityEngine.Events;

namespace Fumo.NativePlayable
{
    public sealed class NativeItemPrefabQuality : MonoBehaviour
    {
        [Serializable] public sealed class QualityOptAction
        {
            public UnityEvent onTrue = new UnityEvent();
            public UnityEvent onFalse = new UnityEvent();
        }
        public string _optionKey;
        public QualityOptAction[] _actions;
        public string sourceComponentId, sourceSiblingPath;
        public int RefreshCount { get; private set; }
        public int SelectedIndex { get; private set; }

        void Awake()
        {
            Refresh();
            NativeUserSettings.Changed += Refresh;
        }
        void OnDestroy() { NativeUserSettings.Changed -= Refresh; }
        public void Refresh()
        {
            int selected = NativeUserSettings.GetInt(_optionKey);
            foreach (var action in _actions) action.onFalse.Invoke();
            _actions[selected].onTrue.Invoke();
            SelectedIndex = selected;
            RefreshCount++;
        }
    }
}
