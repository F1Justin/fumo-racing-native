using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [CreateAssetMenu(menuName = "THFR/Broadcast camera profile")]
    public sealed class NativeBroadcastCameraProfile : ScriptableObject
    {
        [Serializable] public sealed class Station
        {
            public string id;
            public NativeBroadcastShotKind kind = NativeBroadcastShotKind.Trackside;
            public bool useWorldPose;
            public Vector3 worldPosition, worldEuler;
            [Range(-10, 10)] public int aimSampleOffset = 2;
            [Range(.05f, .6f)] public float screenHeight = .24f;
            [Range(5, 100)] public float minimumFov = 18, maximumFov = 72;
            [Range(0, 1)] public float routeFraction;
            public float sideOffset = 12, height = 5, coverageRadius = 45;
            [Range(5, 100)] public float fieldOfView = 55;
            public bool finishOnly;
        }
        public string track;
        public float aerialTeleLead = 40, aerialTeleSide = -8, aerialTeleHeight = 12;
        public float aerialWideBehind = 25, aerialWideSide = 0, aerialWideHeight = 18;
        public float fpvSide = 4, fpvHeight = 4;
        [Min(5)] public float followDistance = 12;
        [Min(2)] public float followHeight = 5;
        [Min(8)] public float pairDistance = 15;
        [Min(3)] public float pairHeight = 7;
        public LayerMask obstructionMask = 512;
        public Station[] stations = Array.Empty<Station>();
    }
}
