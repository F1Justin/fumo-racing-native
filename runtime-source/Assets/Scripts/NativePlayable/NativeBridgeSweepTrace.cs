using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeBridgeSweepTrace
    {
        [Serializable] public sealed class Pose
        {
            public Vector3 position, up, boundsCenter, boundsExtents;
            public Quaternion rotation;
            public bool grounded, inWater, recoveryPending;
            public float speed, verticalSpeed;
        }
        [Serializable] public sealed class Move
        {
            public int sequence, frame, flags;
            public float time, delta;
            public Vector3 requested;
            public Pose before, after;
        }
        [Serializable] public sealed class Trigger
        {
            public int frame, moveSequence;
            public float time;
            public string volume, other, otherType, tag;
            public bool water, currentPosePenetrates, recoveryAlreadyPending, legacyParentWouldRecover, directPlayerWouldRecover;
            public Vector3 separationDirection;
            public float separationDistance;
            public Pose pose;
        }
        public static NativeKartMotor Motor;
        public static readonly List<Move> Moves = new List<Move>();
        public static readonly List<Trigger> Triggers = new List<Trigger>();
        public static void Bind(NativeKartMotor motor) { Motor= motor; Moves.Clear(); Triggers.Clear(); }
        public static Pose Capture(NativeKartMotor motor)
        {
            var body=motor.GetComponent<CharacterController>();
            var recovery=motor.GetComponent<NativeKartRecovery>();
            return new Pose {position=motor.transform.position,rotation=motor.transform.rotation,up=motor.transform.up,
                boundsCenter=body.bounds.center,boundsExtents=body.bounds.extents,grounded=motor.Grounded,
                inWater=motor.InWater,recoveryPending=recovery&&recovery.Pending,speed=motor.Speed,verticalSpeed=motor.CurrentVerticalSpeed};
        }
        public static void BeforeMove(NativeKartMotor motor, Vector3 delta, float dt)
        {
            if(motor!=Motor)return;
            Moves.Add(new Move {sequence=Moves.Count,frame=Time.frameCount,time=Time.time,delta=dt,requested=delta,before=Capture(motor)});
        }
        public static void AfterMove(NativeKartMotor motor, CollisionFlags flags)
        {
            if(motor!=Motor||Moves.Count==0)return;
            var move=Moves[Moves.Count-1];move.after=Capture(motor);move.flags=(int)flags;
        }
        public static void Enter(NativeTrackFallVolume volume, Collider other)
        {
            if(!Motor||other.GetComponentInParent<NativeKartMotor>()!=Motor)return;
            var body=volume.GetComponent<Collider>();
            bool penetration=Physics.ComputePenetration(other,other.transform.position,other.transform.rotation,
                body,body.transform.position,body.transform.rotation,out var direction,out float distance);
            Triggers.Add(new Trigger {frame=Time.frameCount,moveSequence=Moves.Count-1,time=Time.time,
                volume=Path(volume.transform),other=Path(other.transform),otherType=other.GetType().Name,tag=other.tag,
                water=volume.water,legacyParentWouldRecover=Motor.driving&&(!volume.water||!Motor.Waterproof),
                directPlayerWouldRecover=other.CompareTag("Player")&&other.GetComponent<NativeKartMotor>()==Motor&&Motor.driving&&(!volume.water||!Motor.Waterproof),
                currentPosePenetrates=penetration,separationDirection=direction,separationDistance=distance,
                recoveryAlreadyPending=Motor.GetComponent<NativeKartRecovery>().Pending,pose=Capture(Motor)});
        }
        static string Path(Transform node) { string path=node.name;while(node.parent){node=node.parent;path=node.name+"/"+path;}return path; }
    }
}
