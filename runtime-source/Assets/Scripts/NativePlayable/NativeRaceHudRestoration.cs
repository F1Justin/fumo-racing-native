using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        readonly NativeKartMotor[] rearRacers = new NativeKartMotor[2];
        readonly NativeItemActor[] radarItems = new NativeItemActor[2];
        readonly Vector2[] rearCoordinates = new Vector2[2], radarCoordinates = new Vector2[2];
        bool restoredMarkersInitialized, restoredRankRequested;
        float restoredMarkersFixedTime = -1;

        public NativeAuthoredUi.RaceHudNodeSnapshot[] ReadRestoredRaceHud() => authoredUi.ReadRestoredRaceHud();

        public static float OriginalRearMarkerScale(float distance) => Mathf.Lerp(1.65f, .25f,
            Mathf.Pow(Mathf.Lerp(3, 75, distance / 75) / 75, 2));

        void UpdateRestoredMarkerTargets(NativeKartMotor[] racers)
        {
            if (!indicatorRace) indicatorRace = UnityEngine.Object.FindObjectOfType<NativeRace>();
            if (!indicatorRace || racers.Length == 0 || !racers[0]) return;
            var player = racers[0];
            var sensor = player.GetComponent<NativeItemRadarSensor>();
            for (int i = 0; i < 2; i++)
            {
                rearRacers[i] = HudRelativeRacer(i + 1);
                rearCoordinates[i] = OriginalHorizontalMarkerCoordinates(player.transform,
                    rearRacers[i] ? rearRacers[i].transform : null);
                radarItems[i] = null;
                if (sensor) sensor.GetItem(i, out radarItems[i]);
                radarCoordinates[i] = OriginalHorizontalMarkerCoordinates(player.transform,
                    radarItems[i] ? radarItems[i].transform : null);
            }
            restoredMarkersInitialized = true;
            restoredMarkersFixedTime = Time.fixedTime;
        }

        static Vector2 OriginalHorizontalMarkerCoordinates(Transform player, Transform target)
        {
            if (!target) return Vector2.zero;
            float distance = Vector3.Distance(player.position, target.position);
            return distance > 3 && distance <= 75
                ? new Vector2(player.InverseTransformPoint(target.position).x * 30, OriginalRearMarkerScale(distance))
                : Vector2.zero;
        }

        void DrawRestoredRaceIndicators(NativeKartMotor[] racers)
        {
            if (!restoredMarkersInitialized || restoredMarkersFixedTime != Time.fixedTime) UpdateRestoredMarkerTargets(racers);
            for (int i = 0; i < 2; i++)
            {
                bool identity=indicatorRace.TrySourceIndicatorIdentity(rearRacers[i],out string caption,out Color color);
                authoredUi.OriginalRearIndicator(i, identity&&rearRacers[i] && rearCoordinates[i].y > 0,
                    rearCoordinates[i].x, rearCoordinates[i].y, caption,
                    color, rearRacers[i]);
                authoredUi.OriginalItemIndicator(i, radarItems[i] && radarCoordinates[i].y > 0,
                    radarCoordinates[i].x, radarCoordinates[i].y,
                    radarItems[i] ? radarItems[i].RadarIcon : null, radarItems[i]);
            }
        }

        void ResetRestoredRaceHud()
        {
            restoredMarkersInitialized = false;
            authoredUi?.ResetOriginalRaceRank();
        }
    }
}
