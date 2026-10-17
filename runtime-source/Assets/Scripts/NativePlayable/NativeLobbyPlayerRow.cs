using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed class NativeLobbyPlayerRow : MonoBehaviour
    {
        [SerializeField] Image highlight, character;
        [SerializeField] RawImage vehicle;
        [SerializeField] NativeLobbyMarquee playerName;
        [SerializeField] NativeLobbyLabel status, empty;
        [SerializeField] GameObject separator;
        [SerializeField] Color readyColor = new Color(.5f, 1, .67f);
        public void Present(string name, string state, Sprite icon, Texture preview, bool local, bool ready, bool last)
        {
            bool occupied = name != null;
            highlight.enabled = local;
            character.gameObject.SetActive(occupied && icon);
            vehicle.gameObject.SetActive(occupied && preview);
            character.sprite = icon; vehicle.texture = preview;
            playerName.gameObject.SetActive(occupied); status.gameObject.SetActive(occupied);
            empty.gameObject.SetActive(!occupied); separator.SetActive(!last);
            if (occupied)
            {
                playerName.Set(name);
                status.Set(state);
                status.StateColor(ready ? readyColor : (Color?)null);
            }
            else empty.Set("Waiting for player...");
        }
    }
}
