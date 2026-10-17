using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [DefaultExecutionOrder(-90)]
    public sealed class NativeItemLaunchVerification : MonoBehaviour
    {
        [Serializable] public sealed class Sample
        {
            public string vehicle, item;
            public float speed, deltaTime, requestedDeltaTime, followingDeltaTime;
            public int releaseFrame, damageFrame = -1;
            public bool passed, given, carried, released;
            public Vector3 ownerAtRequest, ownerAfterMovement, projectileAtSpawn, projectileBeforeMovement;
            public Vector3 ownerAtDamage, projectileAtDamage;
            public float speedAtRequest, spawnDistanceAfterMovement;
            public int useCountAfterRelease;
            public int spawnedCount, expectedCount, motorStepFrame;
            public bool selectedVehicle;
            public float actualFollowingDeltaTime;
            public float speedAfterMovement;
        }

        [Serializable] sealed class Report
        {
            public string version, track;
            public bool passed, opponentHit, returnHitsOwner, homingExcludesOwner, resetClearsPending;
            public bool movingCrossingHit, movingNearMiss;
            public bool teleportAvoidsSweep;
            public float crossingDeltaTime, crossingDisplacement, nearMissDeltaTime, nearMissDisplacement;
            public int checksPassed, checksTotal;
            public Sample[] samples;
        }

        static readonly FieldInfo SpeedField = typeof(NativeKartMotor).GetField(
            "<Speed>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo VelocityField = typeof(NativeKartMotor).GetField(
            "velocity", BindingFlags.Instance | BindingFlags.NonPublic);

        NativeRace race;
        NativeKartMotor motor;
        NativeKartItemInventory inventory;
        Sample sample;
        int step;
        bool run;
        NativeItemActor projectile;
        NativeKartMotor crossingMotor;
        bool crossingTeleport;
        Vector3 crossingStart;
        float crossingDeltaTime, crossingDisplacement;

        void Update()
        {
            if (crossingMotor)
            {
                if (crossingTeleport)
                {
                    crossingMotor.TeleportTrack(crossingStart + Vector3.right * 10f, crossingMotor.transform.rotation);
                    crossingMotor.enabled = false;
                }
                else
                {
                    SpeedField.SetValue(crossingMotor, 80f);
                    VelocityField.SetValue(crossingMotor, crossingMotor.transform.forward * 80f);
                    crossingMotor.driving = true;
                    crossingMotor.throttle = 80f / crossingMotor.stats._topSpeed;
                }
                crossingDeltaTime = Time.deltaTime;
            }
            if (!run || sample == null) return;
            SpeedField.SetValue(motor, sample.speed);
            VelocityField.SetValue(motor, motor.transform.forward * sample.speed);
            motor.driving = true;
            motor.throttle = sample.speed / motor.stats._topSpeed;
            if (step == 0)
            {
                sample.given = inventory.TryGive(sample.item, false);
                race.UsePlayerItem();
            }
            else if (step == 1)
            {
                sample.carried = inventory.State.carried;
                sample.ownerAtRequest = motor.transform.position;
                sample.speedAtRequest = motor.Speed;
                sample.releaseFrame = Time.frameCount;
                sample.deltaTime = Time.deltaTime;
                race.UsePlayerItem();
            }
            else if (step == 2) sample.actualFollowingDeltaTime = Time.deltaTime;
            step++;
        }

        void LateUpdate()
        {
            if (crossingMotor)
                crossingDisplacement = Vector3.ProjectOnPlane(crossingMotor.transform.position - crossingStart, Vector3.up).magnitude;
            if (!run || sample == null || sample.releaseFrame != Time.frameCount) return;
            projectile = FindShot(motor, sample.item);
            sample.ownerAfterMovement = motor.transform.position;
            sample.motorStepFrame = motor.LastStepFrame;
            sample.speedAfterMovement = motor.Speed;
            sample.useCountAfterRelease = inventory.UseCount;
            sample.spawnedCount = FindObjectsOfType<NativeItemActor>().Count(a => a && !a.Finished &&
                !a.Carried && a.Owner == motor && a.ItemId == sample.item);
            sample.released = sample.spawnedCount == sample.expectedCount && inventory.UseCount == 2;
            if (!projectile) return;
            sample.projectileAtSpawn = projectile.transform.position;
            sample.projectileBeforeMovement = projectile.transform.position;
            var body = motor.GetComponent<CharacterController>();
            sample.spawnDistanceAfterMovement = Vector3.Distance(projectile.transform.position,
                motor.transform.position + Vector3.up * body.center.y);
        }

        void Damaged(float duration)
        {
            if (sample == null || sample.damageFrame >= 0) return;
            sample.damageFrame = Time.frameCount;
            sample.ownerAtDamage = motor.transform.position;
            if (projectile) sample.projectileAtDamage = projectile.transform.position;
        }

        static NativeItemActor FindShot(NativeKartMotor motor, string id) =>
            FindObjectsOfType<NativeItemActor>().FirstOrDefault(a => a && !a.Finished &&
                !a.Carried && a.Owner == motor && a.ItemId == id);

        public static IEnumerator Run(NativeRace race, string directory)
        {
            Directory.CreateDirectory(directory);
            var player = race.racers[0];
            var hit = player.GetComponent<NativeKartHit>();
            var inventory = player.GetComponent<NativeKartItemInventory>();
            var world = inventory.World;
            var roster = race.vehicleRoster;
            int miko = Enumerable.Range(0, roster.CharacterCount).First(i => roster.CharacterNameAt(i) == "Miko");
            int bike = Enumerable.Range(0, roster.VehicleCount).First(i => roster.VehicleNameAt(i) == "Bike");
            race.ResetRace(false);
            race.SetVisualCapture("race", true);
            Time.timeScale = 1f;
            var start = player.transform.position;
            var heading = player.transform.rotation;
            foreach (var racer in race.racers)
            {
                racer.GetComponent<NativeKartOriginalAI>().enabled = false;
                racer.GetComponent<NativeKartItemInventory>().Configure(race, world, "Miko", false);
                if (racer != player) racer.enabled = false;
                if (racer != player) racer.ResetAt(start + heading * Vector3.right * (50f + Array.IndexOf(race.racers, racer) * 10f), heading, false);
            }
            var probe = race.gameObject.AddComponent<NativeItemLaunchVerification>();
            probe.race = race; probe.motor = player; probe.inventory = inventory;
            hit.Damaged += probe.Damaged;
            var samples = new List<Sample>();
            foreach (int fps in new[] { 30, 60, 120 })
                foreach (float speed in new[] { 0f, 80f, 145f })
                    samples.Add(new Sample { vehicle = "Bike", item = "YinYang", speed = speed,
                        deltaTime = 1f / fps, followingDeltaTime = 1f / fps });
            foreach (int vi in Enumerable.Range(0, roster.VehicleCount))
                samples.Add(new Sample { vehicle = roster.VehicleNameAt(vi), item = "YinYang2", speed = 80f,
                    deltaTime = 1f / 60f, followingDeltaTime = 1f / 60f });
            samples.Add(new Sample { vehicle = "Bike", item = "YinYang", speed = 80f,
                deltaTime = 1f / 60f, followingDeltaTime = .05f });

            foreach (var sample in samples)
            {
                sample.requestedDeltaTime = sample.deltaTime;
                probe.run = false; probe.sample = null;
                inventory.ResetItems(); world.ResetWorld(); hit.ResetState();
                int vi = Enumerable.Range(0, roster.VehicleCount).First(i => roster.VehicleNameAt(i) == sample.vehicle);
                roster.ApplySelection(miko, vi, player);
                sample.selectedVehicle = player.stats == roster.vehicles[vi].settings;
                sample.expectedCount = inventory.catalog.Find(sample.item).count;
                inventory.Configure(race, world, "Miko", false);
                player.ResetAt(start, heading, false);
                player.UseFixedVerificationClock = false;
                player.enabled = true;
                Time.captureDeltaTime = sample.deltaTime;
                for (int i = 0; i < 240 && !player.Grounded; i++) yield return null;
                for (int i = 0; i < 3; i++) yield return null;
                probe.sample = sample; probe.step = 0; probe.run = true; probe.projectile = null;
                yield return null;
                yield return null;
                Time.captureDeltaTime = sample.followingDeltaTime;
                for (int i = 0; i < 5; i++) yield return null;
                probe.run = false;
                sample.useCountAfterRelease = inventory.UseCount;
                sample.passed = sample.given && sample.carried && sample.released && sample.damageFrame < 0 &&
                    sample.selectedVehicle && sample.motorStepFrame == sample.releaseFrame &&
                    Mathf.Abs(sample.speedAtRequest - sample.speed) < .001f &&
                    Mathf.Abs(sample.deltaTime - sample.requestedDeltaTime) < .001f &&
                    Mathf.Abs(sample.actualFollowingDeltaTime - sample.followingDeltaTime) < .001f;
                Debug.Log("NATIVE_ITEM_LAUNCH " + JsonUtility.ToJson(sample));
            }

            probe.sample = null; probe.run = false;
            inventory.ResetItems(); world.ResetWorld(); hit.ResetState();
            Time.captureDeltaTime = 1f / 60f;
            player.ResetAt(start, heading, false); player.enabled = false;
            var target = race.racers[1];
            target.ResetAt(start + heading * Vector3.forward * 8f, heading, false);
            var targetHit = target.GetComponent<NativeKartHit>(); targetHit.ResetState();
            world.Spawn("YinYang", player, start + heading * Vector3.forward * 3f,
                heading, heading * Vector3.forward * 100f);
            for (int i = 0; i < 6; i++) yield return null;
            bool opponentHit = targetHit.Frozen || targetHit.Invulnerable;
            world.ResetWorld(); hit.ResetState();
            target.ResetAt(start + heading * Vector3.right * 50f, heading, false);
            world.Spawn("YinYang", player, start + heading * Vector3.forward * 8f,
                heading, -(heading * Vector3.forward) * 100f);
            for (int i = 0; i < 6; i++) yield return null;
            bool returnHitsOwner = hit.Frozen || hit.Invulnerable;
            world.ResetWorld(); hit.ResetState();
            var homing = world.Spawn("YinYangHoming", player, player.transform.position, heading, Vector3.zero);
            for (int i = 0; i < 2; i++) yield return null;
            bool homingExcludesOwner = homing && !homing.Finished && !hit.Frozen && !hit.Invulnerable;
            world.ResetWorld(); inventory.ResetItems();
            inventory.TryGive("YinYang", false);
            race.UsePlayerItem(); inventory.ResetItems();
            yield return null;
            bool resetClearsPending = world.ActiveActorCount == 0 && !inventory.HasItem;
            inventory.TryGive("YinYang", false); inventory.UseItem();
            race.UsePlayerItem(); inventory.ResetItems();
            yield return null;
            resetClearsPending &= world.ActiveActorCount == 0 && !inventory.HasItem;
            var fixture = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Vector3 crossingOrigin = new Vector3(20000f, 2000f, 20000f);
            fixture.transform.position = crossingOrigin + Vector3.down * .5f;
            fixture.transform.localScale = new Vector3(100f, 1f, 100f);
            fixture.layer = 9;
            bool movingCrossingHit = false, movingNearMiss = false, teleportAvoidsSweep = false;
            yield return VerifyCrossing(probe, target, world, hit, crossingOrigin, Quaternion.identity, false, false,
                passed => movingCrossingHit = passed);
            float crossingDt = probe.crossingDeltaTime, crossingMove = probe.crossingDisplacement;
            yield return VerifyCrossing(probe, target, world, hit, crossingOrigin, Quaternion.identity, true, false,
                passed => movingNearMiss = passed);
            float nearMissDt = probe.crossingDeltaTime, nearMissMove = probe.crossingDisplacement;
            yield return VerifyCrossing(probe, target, world, hit, crossingOrigin, Quaternion.identity, false, true,
                passed => teleportAvoidsSweep = passed);
            int passed = samples.Count(s => s.passed) + (opponentHit ? 1 : 0) + (returnHitsOwner ? 1 : 0)
                + (homingExcludesOwner ? 1 : 0) + (resetClearsPending ? 1 : 0)
                + (movingCrossingHit ? 1 : 0) + (movingNearMiss ? 1 : 0) + (teleportAvoidsSweep ? 1 : 0);
            var report = new Report { version = Application.version, track = race.trackId,
                checksPassed = passed, checksTotal = samples.Count + 7, samples = samples.ToArray(),
                opponentHit = opponentHit, returnHitsOwner = returnHitsOwner,
                homingExcludesOwner = homingExcludesOwner, resetClearsPending = resetClearsPending,
                movingCrossingHit = movingCrossingHit, movingNearMiss = movingNearMiss,
                teleportAvoidsSweep = teleportAvoidsSweep,
                crossingDeltaTime = crossingDt, crossingDisplacement = crossingMove,
                nearMissDeltaTime = nearMissDt, nearMissDisplacement = nearMissMove,
                passed = passed == samples.Count + 7 && race.VisualErrorCount == 0 };
            File.WriteAllText(Path.Combine(directory, "item-launch-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_ITEM_LAUNCH_RESULT passed=" + report.passed + " checks=" + passed + "/" + report.checksTotal);
            Application.Quit(report.passed ? 0 : 2);
        }

        static IEnumerator VerifyCrossing(NativeItemLaunchVerification probe, NativeKartMotor target,
            NativeItemWorld world, NativeKartHit ownerHit, Vector3 origin, Quaternion heading,
            bool nearMiss, bool teleport, Action<bool> complete)
        {
            world.ResetWorld(); ownerHit.ResetState();
            var targetHit = target.GetComponent<NativeKartHit>(); targetHit.ResetState();
            probe.motor.ResetAt(origin + heading * Vector3.right * 50f, heading, false);
            probe.motor.enabled = false;
            Time.captureDeltaTime = 1f / 60f;
            target.ResetAt(origin + heading * new Vector3(nearMiss ? 5f : -5f, 0f, 5f),
                heading * Quaternion.Euler(0f, 90f, 0f), false);
            target.UseFixedVerificationClock = false; target.enabled = true;
            for (int i = 0; i < 240 && !target.Grounded; i++) yield return null;
            yield return new WaitForEndOfFrame();
            probe.crossingStart = target.transform.position;
            probe.crossingMotor = target;
            probe.crossingTeleport = teleport;
            Time.captureDeltaTime = .1f;
            var shot = world.Spawn("YinYang", probe.motor, origin + Vector3.up * .24f,
                heading, heading * Vector3.forward * 100f);
            bool spawned = shot;
            yield return new WaitForEndOfFrame();
            bool hit = targetHit.Frozen || targetHit.Invulnerable;
            bool moved = (teleport ? Mathf.Abs(probe.crossingDisplacement - 10f) < .1f :
                probe.crossingDisplacement > 7f && probe.crossingDisplacement < 9f) &&
                Mathf.Abs(probe.crossingDeltaTime - .1f) < .001f;
            complete(spawned && moved && (nearMiss || teleport ? shot && !hit && !shot.Finished : hit));
            Debug.Log("NATIVE_ITEM_CROSSING nearMiss=" + nearMiss + " teleport=" + teleport + " hit=" + hit +
                " dt=" + probe.crossingDeltaTime + " displacement=" + probe.crossingDisplacement);
            probe.crossingMotor = null; target.enabled = false;
            world.ResetWorld(); Time.captureDeltaTime = 1f / 60f;
        }
    }
}
