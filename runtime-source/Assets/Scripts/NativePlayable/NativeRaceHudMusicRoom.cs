using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        float musicDiscAngle;
        int musicDiscFrame = -1;
        public NativeMenuAction OriginalMusicRoom()
        {
            const string root = "menu2/Canvas/MusicRoom/";
            authoredUi.Show("menu2", "MusicRoom");
            LocalizeOriginalPrompts(root);
            string[] rows = { "Music (1)", "Music", "Selected", "Music (2)", "Music (3)", "Music (4)" };
            for (int i = 0; i < rows.Length; i++)
            {
                int index = MusicSelection + i - 2;
                var entry = NativeMusicRoomData.At(index);
                if (entry == null) continue;
                string key = root + "Buttons/" + rows[i];
                authoredUi.SetText(key + "/Text", entry.title);
                Rect rect = authoredUi.RectAt(key);
                if (!SuppressPointerInput && PointerButton(rect))
                { MusicSelection = (index % NativeMusicRoomData.Count + NativeMusicRoomData.Count) % NativeMusicRoomData.Count; NativeAudioRuntime.PreviewMusicRoom(MusicSelection); }
            }
            var selected = NativeMusicRoomData.At(MusicSelection);
            if (selected != null)
            {
                authoredUi.SetText(root + "musiccredits", selected.credits);
                authoredUi.AssignOriginalSprite(root + "Disc/labelmask", authoredUi.Definition(root + "Disc/labelmask").sprite);
                authoredUi.AssignOriginalSprite(root + "Disc/labelmask/label", selected.cover);
            }
            if (musicDiscFrame != Time.frameCount)
            { if (!NativeAudioRuntime.MusicPaused) musicDiscAngle -= 36 * Time.unscaledDeltaTime; musicDiscFrame = Time.frameCount; }
            authoredUi.Rotation(root + "Disc", musicDiscAngle);
            return NativeMenuAction.None;
        }
    }
}
