using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartHitVerification
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Sample
        {
            public string phase;
            public int frame, ticks, layer, accepted, rejected, pending;
            public double clock;
            public float timeScale, speed;
            public bool frozen, invulnerable, model, effect, inventoryFrozen;
            public Vector3 position;
        }
        [Serializable] public sealed class Result
        {
            public string version, boundary;
            public bool passed;
            public Check[] checks;
            public Sample[] samples;
        }

        public static IEnumerator Run(NativeRace race, string directory)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            var samples = new List<Sample>();
            var target = race.racers[0];
            var owner = race.racers[1];
            var hit = target.GetComponent<NativeKartHit>();
            var inventory = target.GetComponent<NativeKartItemInventory>();
            var bodies = race.racers.Select(r => r.GetComponent<CharacterController>()).ToArray();
            var motorEnabled = race.racers.Select(r => r.enabled).ToArray();
            var bodyEnabled = bodies.Select(b => b && b.enabled).ToArray();
            var ais = race.racers.Select(r => r.GetComponent<NativeKartOriginalAI>()).Where(a => a).ToArray();
            var aiEnabled = ais.Select(a => a.enabled).ToArray();
            float savedTimeScale = Time.timeScale;
            bool savedAllow = race.AllowCameraVerificationUpdates;
            NativeKartItemProjectile projectile = null;
            try
            {
                Add("nineteen-native-health-rules", NativeKartHitRulesVerification.Run(directory), "Original Invoke and freeze rules");
                Add("original-health-bindings", hit && inventory && inventory.projectilePrefab && hit.damageFX && hit.toHide
                    && hit.toHide == target.visual.gameObject && Mathf.Abs(hit.defaultDuration - 1f) < .00001f,
                    "PlayerKart serialized duration=1, complete Character hide, original powerfx and YinYang prefab");
                if (!hit || !inventory || !inventory.projectilePrefab || !hit.damageFX || !hit.toHide)
                    throw new InvalidOperationException("Hit verification bindings missing");
                race.ResetRace(false);
                race.SetVisualCapture("race", true);
                Time.timeScale = 1f;
                race.AllowCameraVerificationUpdates = true;
                foreach (var ai in ais) ai.enabled = false;
                for (int i = 1; i < race.racers.Length; i++) { race.racers[i].enabled = false; bodies[i].enabled = false; }
                target.enabled = true;
                target.driving = true;
                target.throttle = .5f;
                for (int i = 0; i < 25; i++) yield return new WaitForFixedUpdate();
                Add("pre-hit-moving-car", target.Speed > 1f, "Actual Motor frames establish nonzero speed before hit");
                target.driving = false;
                target.throttle = 0f;
                target.enabled = false;
                projectile = Spawn(owner, false);
                int collisionStart = Time.frameCount;
                for (int i = 0; i < 120 && hit.State.AcceptedDamage == 0; i++) yield return null;
                Record("hit");
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, "hit.png"));
                Add("real-yinyang-trigger-hit", hit.State.AcceptedDamage == 1 && Time.frameCount > collisionStart,
                    "Original projectile hurt trigger reaches Player collider through native Physics callbacks");
                Add("freeze-clears-motion-and-items", hit.Frozen && inventory.Frozen && target.gameObject.layer == 4
                    && target.Speed == 0f && target.CurrentTurn == 0f && target.DriveVelocity == Vector3.zero
                    && target.PhysicsVelocity == Vector3.zero, "Health freezes items and vehicle, clears vectors, and assigns Water layer");
                Add("damage-hides-whole-character", hit.damageFX.activeSelf && !hit.toHide.activeSelf && hit.Invulnerable,
                    "Original damage effect visible while kart and Fumo hierarchy is hidden");
                yield return null;
                Add("accepted-projectile-destroyed", !projectile, "TriggerHurt invokes DestroyNow after accepted damage");
                target.enabled = true;
                Vector3 frozenPosition = target.transform.position;
                for (int i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
                Add("frozen-motor-stops-simulation", hit.Frozen && Vector3.Distance(target.transform.position, frozenPosition) < .0001f,
                    "Real Motor updates preserve frozen position");
                Time.timeScale = 0f;
                yield return null;
                double frozenClock = hit.State.Clock;
                int frozenTicks = hit.TickCount;
                for (int i = 0; i < 8; i++) { yield return null; Record("paused"); }
                Add("paused-health-deadline-frozen", hit.Frozen && hit.State.Clock == frozenClock && hit.TickCount == frozenTicks,
                    "Eight real paused frames retain health clock and pending respawn");
                Time.timeScale = 1f;
                target.enabled = false;
                for (int i = 0; i < 240 && hit.Frozen; i++) { yield return null; Record("resuming"); }
                Add("scaled-one-second-respawn", !hit.Frozen && !hit.Invulnerable && !inventory.Frozen && target.gameObject.layer == 0
                    && !hit.damageFX.activeSelf && hit.toHide.activeSelf && hit.State.Resumes == 1,
                    "Scaled Update resumes all health effects without additional immunity");
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, "respawn.png"));
                Add("live-clock-advanced", hit.State.Clock >= 1 && hit.TickCount > frozenTicks,
                    "Health deadline advances only during running frames");
                hit.MakeInvulnerable(1f);
                projectile = Spawn(owner, false);
                for (int i = 0; i < 120 && hit.State.RejectedDamage == 0; i++) yield return null;
                yield return null;
                Record("immune-contact");
                Add("immune-projectile-still-destroyed", !projectile && hit.State.RejectedDamage == 1 && !hit.Frozen
                    && hit.State.AcceptedDamage == 1, "Rejected damage still invokes projectile destruction");
                hit.ResetState();
                projectile = Spawn(target, true);
                Collider playerCollider = target.GetComponent<Collider>();
                var trigger = projectile.GetComponentsInChildren<Collider>(true).FirstOrDefault(c => c.isTrigger);
                Add("carried-projectile-ignores-owner", trigger && Physics.GetIgnoreCollision(trigger, playerCollider),
                    "Original doubleaction carrying ignores the owner collision pair");
                UnityEngine.Object.Destroy(projectile.gameObject);
                yield return null;
                projectile = Spawn(target, false);
                for (int i = 0; i < 120 && hit.State.AcceptedDamage == 0; i++) yield return null;
                Add("launched-projectile-can-hit-owner", hit.State.AcceptedDamage == 1 && hit.Frozen,
                    "Original YinYang collidesOwner=1 applies to launched projectile");
                race.ResetRace(false);
                Record("restart");
                Add("restart-clears-health-and-effects", !hit.Frozen && !hit.Invulnerable && hit.State.PendingCount == 0
                    && hit.State.AcceptedDamage == 0 && !inventory.Frozen && !hit.damageFX.activeSelf && hit.toHide.activeSelf
                    && target.gameObject.layer == 0, "Race reset cancels delayed health callbacks before resetting Motor");
                Time.timeScale = 1f;
                target.enabled = false;
                for (int i = 0; i < 90; i++) yield return new WaitForFixedUpdate();
                Add("restart-has-no-stale-respawn", hit.State.Resumes == 0 && hit.State.PendingCount == 0 && !hit.Frozen,
                    "Old pending respawn remains cancelled after its original deadline");
                hit.ResetState();
                Action<bool> savedItemsFrozen = hit.ItemsFrozenChanged;
                bool acceptedOrder = true, resumeOrder = false;
                float speedAtSecondResume = -1f;
                int deliveredResumes = 0;
                var scheduledCountsAtDamage = new List<int>();
                Action<float> damageObserver = _ => scheduledCountsAtDamage.Add(hit.State.PendingCount);
                hit.Damaged += damageObserver;
                hit.ItemsFrozenChanged = frozen =>
                {
                    savedItemsFrozen?.Invoke(frozen);
                    if (frozen)
                        acceptedOrder &= hit.damageFX.activeSelf && !hit.toHide.activeSelf
                            && target.HitFrozen && target.Speed == 0f;
                    else if (hit.State.Resumes > 0)
                    {
                        deliveredResumes++;
                        if (hit.State.Resumes == 1)
                            resumeOrder = !target.HitFrozen && !inventory.Frozen && !hit.Invulnerable
                                && hit.damageFX.activeSelf && !hit.toHide.activeSelf;
                        if (hit.State.Resumes == 2) speedAtSecondResume = target.Speed;
                    }
                };
                try
                {
                    hit.TakeDamage(1.25f);
                    hit.MakeInvulnerable(.1f);
                    for (int i = 0; i < 120 && hit.Invulnerable; i++) yield return null;
                    // A recovery can unfreeze Motor while Health still has its original pending callback.
                    target.SetHitFrozen(false);
                    target.enabled = true;
                    target.driving = true;
                    target.throttle = .5f;
                    for (int i = 0; i < 45 && target.Speed <= 1f; i++) yield return new WaitForFixedUpdate();
                    bool movingWithPendingHealth = hit.Frozen && !hit.Invulnerable && target.Speed > 1f;
                    Record("before-overlap-hit");
                    bool secondAccepted = hit.TakeDamage(3f);
                    Add("accepted-overlap-refreezes-moving-motor", movingWithPendingHealth && secondAccepted
                        && target.HitFrozen && target.Speed == 0f,
                        "An accepted hit repeats native Freeze even when Health was already marked frozen; moving=" + movingWithPendingHealth + "; accepted=" + secondAccepted + "; motorFrozen=" + target.HitFrozen + "; speed=" + target.Speed);
                    Add("accepted-hit-feedback-order", acceptedOrder && scheduledCountsAtDamage.SequenceEqual(new[] { 0, 1 }),
                        "FX and Character change before Motor and items freeze; Damaged runs before the new Respawn is scheduled");
                    for (int i = 0; i < 300 && hit.State.Resumes < 1; i++) yield return null;
                    Add("respawn-callback-order", resumeOrder && deliveredResumes == 1,
                        "Respawn clears invulnerability, unfreezes Motor/items, then restores model and effect");
                    for (int i = 0; i < 15; i++) yield return new WaitForFixedUpdate();
                    bool movingBeforeSecondResume = target.Speed > 1f && !target.HitFrozen;
                    for (int i = 0; i < 480 && hit.State.Resumes < 2; i++) yield return null;
                    Record("second-respawn");
                    Add("second-respawn-clears-speed-again", movingBeforeSecondResume && hit.State.Resumes == 2
                        && deliveredResumes == 2 && speedAtSecondResume == 0f,
                        "After real driving between callbacks, the second Respawn repeats UnFreeze and zeroes speed again");
                }
                finally
                {
                    hit.Damaged -= damageObserver;
                    hit.ItemsFrozenChanged = savedItemsFrozen;
                    target.driving = false;
                    target.throttle = 0f;
                    target.enabled = false;
                    hit.ResetState();
                }
                Add("global-silence", AudioListener.volume == 0f && !hit.GetComponentsInChildren<AudioSource>(true).Any(a => a.isPlaying && !a.mute),
                    "Construction remains silent including hit effect hierarchy");
                Add("runtime-errors", race.VisualErrorCount == 0, "Runtime errors=" + race.VisualErrorCount);
            }
            finally
            {
                if (projectile) UnityEngine.Object.Destroy(projectile.gameObject);
                if (hit) hit.ResetState();
                for (int i = 0; i < race.racers.Length; i++) { race.racers[i].enabled = motorEnabled[i]; if (bodies[i]) bodies[i].enabled = bodyEnabled[i]; }
                for (int i = 0; i < ais.Length; i++) ais[i].enabled = aiEnabled[i];
                race.AllowCameraVerificationUpdates = savedAllow;
                Time.timeScale = savedTimeScale;
            }
            var result = new Result { version = Application.version, passed = checks.All(c => c.passed), checks = checks.ToArray(), samples = samples.ToArray(),
                boundary = "Real original YinYang trigger collisions drive accepted, invulnerable and owner hits; real Update frames cover freeze, pause, resume and race restart. Nineteen pure fixtures cover original Invoke overlap rules. Fall and manual recovery interaction is outside this slice." };
            File.WriteAllText(Path.Combine(directory, "hit-report.json"), JsonUtility.ToJson(result, true));
            Debug.Log("NATIVE_HIT_RESULT passed=" + result.passed + " checks=" + checks.Count);
            Application.Quit(result.passed ? 0 : 2);

            void Add(string name, bool passed, string detail) => checks.Add(new Check { name = name, passed = passed, detail = detail });
            void Record(string phase) => samples.Add(new Sample { phase=phase, frame=Time.frameCount, clock=hit.State.Clock,
                ticks=hit.TickCount, layer=target.gameObject.layer, accepted=hit.State.AcceptedDamage, rejected=hit.State.RejectedDamage,
                pending=hit.State.PendingCount, timeScale=Time.timeScale, speed=target.Speed, frozen=hit.Frozen,
                invulnerable=hit.Invulnerable, model=hit.toHide.activeSelf, effect=hit.damageFX.activeSelf,
                inventoryFrozen=inventory.Frozen, position=target.transform.position });
            NativeKartItemProjectile Spawn(NativeKartMotor source, bool carried)
            {
                Vector3 position = target.transform.position + target.transform.forward * 3f + Vector3.up * bodies[0].center.y;
                var instance = UnityEngine.Object.Instantiate(inventory.projectilePrefab, position, Quaternion.identity);
                instance.SetActive(true);
                var value = instance.GetComponent<NativeKartItemProjectile>();
                value.Init(source, -target.transform.forward * 30f, carried);
                Physics.SyncTransforms();
                return value;
            }
        }
    }
}
