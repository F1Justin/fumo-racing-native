using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public Sprite originalMinimapSprite;
        public float minimapOrthographicSize;
        NativeKartMotor[] raceMarkerRacers;
        Vector2[] originalMinimapPositions;
        Color[] originalMinimapInteriors;
        void DrawOriginalMinimap(NativeKartMotor[] racers)
        {
            if (!indicatorRace) indicatorRace = UnityEngine.Object.FindObjectOfType<NativeRace>();
            if (indicatorRace) racers = indicatorRace.SourceMinimapRacers();
            if (!originalMinimapSprite && indicatorRace) originalMinimapSprite = NativeContent.Load<Sprite>("NativeSelectionPreview/" + indicatorRace.trackId + "-minimap");
            if (minimapOrthographicSize <= 0 && indicatorRace)
                minimapOrthographicSize = indicatorRace.trackId == "forest" ? 400 : indicatorRace.trackId == "mountains" ? 600 : indicatorRace.trackId == "purplefield" ? 450 : 700;
            if (!originalMinimapSprite || minimapOrthographicSize <= 0) return;
            if (originalMinimapPositions == null || originalMinimapPositions.Length != racers.Length)
            {
                originalMinimapPositions = new Vector2[racers.Length];
                originalMinimapInteriors = new Color[racers.Length];
            }
            for (int i = 0; i < racers.Length; i++)
            {
                Vector3 local = minimapWorldToLocal.MultiplyPoint3x4(racers[i].transform.position);
                originalMinimapPositions[i] = new Vector2(local.x, local.y) / (2 * minimapOrthographicSize) + Vector2.one * .5f;
                var roster = indicatorRace ? indicatorRace.SourceRacerVehicleRoster(racers[i]) : racers[i].GetComponent<NativeVehicleRoster>();
                originalMinimapInteriors[i] = roster ? roster.CurrentKartColor : Color.white;
            }
            var outlines = new Color[racers.Length];
            for (int i = 0; i < racers.Length; i++)
                outlines[i] = indicatorRace.TrySourceIndicatorIdentity(racers[i], out string phrase, out Color color) ? color : Color.white;
            authoredUi.OriginalMinimap(originalMinimapSprite, originalMinimapPositions, originalMinimapInteriors, outlines);
        }
        NativeRace indicatorRace;
        void DrawOriginalFrontIndicators(NativeKartMotor[] racers)
        {
            if (!indicatorRace) indicatorRace = UnityEngine.Object.FindObjectOfType<NativeRace>();
            if (!indicatorRace || !HudRaceCamera || racers.Length == 0) return;
            for (int i = 0; i < 2; i++)
            {
                var target = HudRelativeRacer(-i - 1);
                float distance = target ? Vector3.Distance(target.transform.position, racers[0].transform.position) : 0;
                bool visible = target && distance > 2 && distance <= 50;
                Vector3 viewport = visible ? HudRaceCamera.WorldToViewportPoint(target.transform.position + Vector3.up * 2.75f) : Vector3.zero;
                float ratio = Mathf.Pow(Mathf.Lerp(2, 50, distance / 50) / 50, 2);
                float scale = Mathf.Lerp(.75f, .15f, ratio);
                bool identity=indicatorRace.TrySourceIndicatorIdentity(target,out string caption,out Color color);
                authoredUi.OriginalFrontIndicator(i, visible&&identity, new Vector2(Mathf.Clamp01(viewport.x), Mathf.Clamp01(viewport.y)), scale,
                    caption, color, target);
            }
        }
        void DisposeRaceMarkers()
        {
            indicatorRace = null;
        }
        public bool OriginalFrontIndicatorsBound(out string detail) => authoredUi.OriginalFrontIndicatorsBound(out detail);
        public bool OriginalMinimapBound(int racerCount, out string detail) => authoredUi.OriginalMinimapBound(originalMinimapSprite, racerCount, out detail);
    }
}
