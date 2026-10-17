using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartAIVerification
    {
        [Serializable]
        public sealed class Result
        {
            public string version;
            public int routeCount, frames, startWaypoint, endWaypoint;
            public float maxSpeed, distance;
            public int driftRequestFrames, actualDriftFrames, groundedFrames;
            public Sample[] samples;
            public bool mathPassed;
            public bool circuitPassed;
            public NativeKartAIWaypoint[] waypoints;
            public Gate[] gates;
            public bool pass;
        }

        [Serializable]
        public struct Gate
        {
            public Vector3 position, boxCenter, boxSize;
            public Quaternion rotation;
            public bool colliderEnabled, isTrigger, kinematic;
        }

        [Serializable]
        public struct Sample
        {
            public int frame, waypoint;
            public Vector3 position, target;
            public float speed, steering, driftDirection;
            public bool grounded;
        }

        public static IEnumerator Run(NativeRace race, NativeKartOriginalAI ai, string directory,
            Action<Result> completed)
        {
            if (!race || !ai || !ai.motor || ai.waypoints == null || ai.waypoints.Length != 51)
                throw new InvalidOperationException("Forest AI route is incomplete");
            var motor = ai.motor;
            var result = new Result { version = Application.version,
                routeCount = ai.waypoints.Length, startWaypoint = ai.WaypointIndex,
                samples = new Sample[60],
                mathPassed = NativeKartAIMathVerification.Run(directory), circuitPassed = NativeCircuitProgressVerification.Run(directory) };
            result.waypoints = ai.waypoints;
            result.gates = new Gate[race.gates.Length];
            for (int i = 0; i < race.gates.Length; i++)
            {
                var box = race.gateColliders[i];
                var body = box.GetComponent<Rigidbody>();
                result.gates[i] = new Gate { position = race.gates[i].position, rotation = race.gates[i].rotation,
                    boxCenter = box.center, boxSize = box.size, colliderEnabled = box.enabled,
                    isTrigger = box.isTrigger, kinematic = body && body.isKinematic };
            }
            float startDistance = motor.Distance;
            int maxWaypoint = result.startWaypoint;
            for (int frame = 0; frame < 1800; frame++)
            {
                yield return new WaitForFixedUpdate();
                result.frames++;
                result.maxSpeed = Mathf.Max(result.maxSpeed, motor.Speed);
                if (motor.Grounded) result.groundedFrames++;
                if (Mathf.Abs(motor.AIDriftDirection) > 0f) result.driftRequestFrames++;
                if (motor.IsDrifting) result.actualDriftFrames++;
                maxWaypoint = Mathf.Max(maxWaypoint, ai.WaypointIndex);
                if (frame % 30 == 0)
                    result.samples[frame / 30] = new Sample { frame = frame,
                        waypoint = ai.WaypointIndex, position = motor.transform.position,
                        target = ai.Target, speed = motor.Speed, steering = motor.steering,
                        driftDirection = motor.AIDriftDirection, grounded = motor.Grounded };
            }
            result.endWaypoint = ai.WaypointIndex;
            result.distance = motor.Distance - startDistance;
            result.pass = result.circuitPassed && result.mathPassed && result.maxSpeed > 15f && result.distance > 100f &&
                          result.groundedFrames > 100 && maxWaypoint > result.startWaypoint &&
                          result.driftRequestFrames > 0 && result.actualDriftFrames > 0;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "ai-report.json"),
                JsonUtility.ToJson(result, true) + "\n");
            Debug.Log("NATIVE_AI_RESULT passed=" + result.pass + " frames=" + result.frames);
            completed?.Invoke(result);
        }
    }
}
