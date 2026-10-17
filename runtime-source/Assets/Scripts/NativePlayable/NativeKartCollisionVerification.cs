using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartCollisionVerification
    {
        [Serializable]
        public sealed class Sample
        {
            public string scenario;
            public bool grounded, grass, water, sawSides, sawPeer, bothForces;
            public int steps, contacts, vehicleContacts, peerContacts;
            public float speed, crashTime, peerCrashTime;
            public Vector3 position, hitNormal, impactPoint, force, peerForce;
            public string[] problems;
        }

        [Serializable]
        public sealed class Report
        {
            public string version, boundary;
            public bool passed, globalSilence, groundFormula, airFormula, forceDecay, pauseFreezesCollision, resetClearsCollision;
            public Sample[] samples;
            public string[] problems;
        }

        public static IEnumerator Run(NativeRace race, string directory)
        {
            Directory.CreateDirectory(directory);
            var errors = new List<string>();
            var samples = new List<Sample>();
            var report = new Report
            {
                version = Application.version,
                boundary = "Independent real CharacterController fixtures and direct original side-branch formula samples. "
                    + "Contact state, force and cooldown are checked; Forest/original-game whole-car trajectory requires a separate replay.",
                globalSilence = AudioListener.volume == 0f
            };
            if (!report.globalSilence) errors.Add("Construction audio listener is audible");
            float previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;

            var formula = new NativeKartCollisionState();
            formula.hitNormal = Vector3.back;
            float speed = 75f;
            report.groundFormula = formula.ResolveGroundedSideContact(Quaternion.identity, ref speed)
                && Mathf.Abs(speed - 74f) < 0.0001f;
            if (!report.groundFormula) errors.Add("Grounded side-contact formula differs from original x64 fixture");
            formula = new NativeKartCollisionState { hitNormal = Vector3.back, afterGrounded = true };
            var velocity = Vector3.forward * 75f;
            speed = 90f;
            report.airFormula = formula.ResolveAirborneSideContact(ref velocity, ref speed, 20f, 0.1f)
                && velocity.magnitude < 0.0001f && Mathf.Abs(speed - 15f) < 0.0001f
                && Vector3.Distance(formula.force, Vector3.back * 20f) < 0.0001f
                && Mathf.Abs(formula.crashTime - 0.1f) < 0.0001f;
            if (!report.airFormula) errors.Add("Airborne side-contact formula differs from original x64 fixture");
            formula.DampenForce(1f / 60f, 6f);
            formula.AdvanceCrashTime(1f / 60f);
            report.forceDecay = Vector3.Distance(formula.force, Vector3.back * 18f) < 0.0001f
                && Mathf.Abs(formula.crashTime - (0.1f - 1f / 60f)) < 0.0001f;
            if (!report.forceDecay) errors.Add("Original force decay or crash cooldown differs");

            string[] names = { "road", "grass", "water", "wall", "player" };
            for (int index = 0; index < names.Length; index++)
            {
                string name = names[index];
                var scene = new GameObject("CollisionVerification_" + name);
                var origin = new Vector3(10000f + 1000f * index, 1000f, 10000f);
                var sample = new Sample { scenario = name };
                var caseErrors = new List<string>();
                try
                {
                    var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ground.name = name + "_ground";
                    ground.transform.SetParent(scene.transform);
                    ground.transform.position = origin + new Vector3(0f, -0.5f, 0f);
                    ground.transform.localScale = new Vector3(100f, 1f, 100f);
                    if (name == "grass") ground.tag = "Grass";
                    if (name == "water") ground.tag = "Water";
                    if (name == "wall")
                    {
                        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        wall.name = "collision_wall";
                        wall.transform.SetParent(scene.transform);
                        wall.transform.position = origin + new Vector3(0f, 2f, 11f);
                        wall.transform.localScale = new Vector3(8f, 4f, 1f);
                    }

                    var pilot = CreateMotor(race.racers[0], scene.transform, origin + Vector3.up * 2f);
                    NativeKartMotor peer = null;
                    if (name == "player")
                    {
                        peer = CreateMotor(race.racers[0], scene.transform,
                            origin + new Vector3(0f, 2f, 10f));
                        Physics.IgnoreCollision(pilot.GetComponent<CharacterController>(),
                            peer.GetComponent<CharacterController>(), false);
                    }
                    Physics.SyncTransforms();
                    for (int step = 0; step < 120 &&
                        (!pilot.Grounded || (peer && !peer.Grounded)); step++)
                    {
                        yield return new WaitForFixedUpdate();
                        sample.steps++;
                    }
                    sample.grounded = pilot.Grounded;
                    if (!sample.grounded) caseErrors.Add("Motor did not settle on real controller ground");
                    if (peer)
                    {
                        if (!peer.Grounded) caseErrors.Add("Peer motor did not settle on real controller ground");
                        // Keep the peer controller collidable while the initiator owns the hit update.
                        peer.enabled = false;
                    }
                    if (name == "wall" || name == "player")
                    {
                        pilot.driving = true;
                        pilot.throttle = 1f;
                        for (int step = 0; step < 180; step++)
                        {
                            yield return new WaitForFixedUpdate();
                            sample.steps++;
                            if (name == "wall" &&
                                (pilot.LastCollisionFlags & CollisionFlags.Sides) != 0)
                            { sample.sawSides = true; break; }
                            if (name == "player" && pilot.VehicleContactCount > 0)
                            { sample.sawPeer = true; break; }
                        }
                    }
                    sample.position = pilot.transform.position;
                    sample.speed = pilot.Speed;
                    sample.grass = pilot.OnGrass;
                    sample.water = pilot.InWater;
                    sample.contacts = pilot.CollisionContactCount;
                    sample.vehicleContacts = pilot.VehicleContactCount;
                    sample.hitNormal = pilot.LastStepSideContactNormal;
                    sample.impactPoint = pilot.CollisionState.impactPoint;
                    sample.force = pilot.CollisionState.force;
                    sample.crashTime = pilot.CollisionState.crashTime;
                    if (peer)
                    {
                        sample.peerContacts = peer.VehicleContactCount;
                        sample.peerForce = peer.CollisionState.force;
                        sample.peerCrashTime = peer.CollisionState.crashTime;
                        sample.bothForces = pilot.CollisionState.force.sqrMagnitude > 0.001f
                            && peer.CollisionState.force.sqrMagnitude > 0.001f
                            && (pilot.CollisionState.force + peer.CollisionState.force).magnitude < 0.001f;
                    }
                    if (sample.contacts == 0) caseErrors.Add("No controller contact callback");
                    if (name == "grass" && !sample.grass) caseErrors.Add("Grass contact state missing");
                    if (name == "water" && (!sample.water || !sample.grass))
                        caseErrors.Add("Water/offroad contact state missing");
                    if (name == "wall" && (!sample.sawSides || sample.hitNormal.sqrMagnitude < 0.1f))
                        caseErrors.Add("Wall side contact or hit normal missing");
                    if (name == "player" && (!sample.sawPeer || !sample.bothForces))
                        caseErrors.Add("Player contact or paired force missing");
                    if (name == "player")
                    {
                        Time.timeScale = 0f;
                        int pausedSteps = pilot.FixedStepCount;
                        var paused = pilot.CollisionState;
                        for (int frame = 0; frame < 5; frame++) yield return null;
                        report.pauseFreezesCollision = pilot.FixedStepCount == pausedSteps
                            && pilot.CollisionState.force == paused.force
                            && pilot.CollisionState.crashTime == paused.crashTime;
                        if (!report.pauseFreezesCollision) caseErrors.Add("Paused collision force or cooldown changed");
                        pilot.ResetAt(origin + Vector3.up * 2f, Quaternion.identity, false);
                        report.resetClearsCollision = pilot.CollisionState.force == Vector3.zero
                            && pilot.CollisionState.crashTime == 0f && pilot.CollisionContactCount == 0
                            && pilot.VehicleContactCount == 0 && pilot.LastCollisionFlags == CollisionFlags.None;
                        if (!report.resetClearsCollision) caseErrors.Add("Reset retained collision state");
                        Time.timeScale = 1f;
                    }
                }
                finally
                {
                    UnityEngine.Object.Destroy(scene);
                }
                sample.problems = caseErrors.ToArray();
                samples.Add(sample);
                foreach (var problem in caseErrors) errors.Add(name + ": " + problem);
                yield return null;
            }

            report.samples = samples.ToArray();
            report.problems = errors.ToArray();
            report.passed = errors.Count == 0;
            Time.timeScale = previousTimeScale;
            File.WriteAllText(Path.Combine(directory, "collision-live-report.json"),
                JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_COLLISION_RESULT passed=" + report.passed);
            Application.Quit(report.passed ? 0 : 2);
        }

        static NativeKartMotor CreateMotor(NativeKartMotor source, Transform parent, Vector3 position)
        {
            var vehicle = new GameObject("collision_fixture_motor");
            vehicle.tag = "Player";
            vehicle.transform.SetParent(parent);
            vehicle.transform.SetPositionAndRotation(position, Quaternion.identity);
            var reference = source.GetComponent<CharacterController>();
            var controller = vehicle.AddComponent<CharacterController>();
            controller.center = reference.center;
            controller.radius = reference.radius;
            controller.height = reference.height;
            controller.skinWidth = reference.skinWidth;
            controller.slopeLimit = reference.slopeLimit;
            controller.stepOffset = reference.stepOffset;
            controller.minMoveDistance = reference.minMoveDistance;
            var motor = vehicle.AddComponent<NativeKartMotor>();
            motor.stats = source.stats;
            motor.UseFixedVerificationClock = true;
            motor.ResetAt(position, Quaternion.identity, false);
            return motor;
        }
    }
}
