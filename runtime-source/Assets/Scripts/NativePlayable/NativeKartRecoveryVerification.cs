using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartRecoveryVerification
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Sample
        {
            public string phase;
            public int frame, requests, teleports, resets;
            public double clock, deadline;
            public float alpha, timeScale;
            public bool pending, invulnerable, healthFrozen, itemFrozen;
            public Vector3 position;
        }
        [Serializable] sealed class Report
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
            var motor = race.racers[0];
            var recovery = motor.GetComponent<NativeKartRecovery>();
            var hit = motor.GetComponent<NativeKartHit>();
            var items = motor.GetComponent<NativeKartItemInventory>();
            var ai = motor.GetComponent<NativeKartOriginalAI>();
            var motorEnabled = race.racers.Select(r => r.enabled).ToArray();
            var aiComponents = race.racers.Select(r => r.GetComponent<NativeKartOriginalAI>()).ToArray();
            var aiEnabled = aiComponents.Select(a => a.enabled).ToArray();
            float savedTimeScale = Time.timeScale;
            bool savedAllow = race.AllowCameraVerificationUpdates;
            try
            {
                Add("ten-original-recovery-rules", NativeKartRecoveryRulesVerification.Run(directory), "Original 0.5-second Invoke cancellation and scaled clock");
                if (!recovery || !hit || !items || !ai) throw new InvalidOperationException("Recovery bindings missing");
                race.ResetRace(false);
                race.SetVisualCapture("race", true);
                race.AllowCameraVerificationUpdates = true;
                Time.timeScale = 1f;
                foreach (var value in aiComponents) value.enabled = false;
                foreach (var value in race.racers) value.enabled = false;
                motor.ResetAt(race.gates[0].position + Vector3.up * 8f, Quaternion.Euler(20f, 60f, 15f), false);
                Vector3 originalPosition = motor.transform.position;
                recovery.Request(true);
                Add("fall-is-delayed", recovery.Pending && recovery.State.Teleports == 0
                    && motor.transform.position == originalPosition, "Manual Fall schedules teleport while retaining current pose");
                for (int i = 0; i < 3; i++) { yield return null; Record("waiting"); }
                Add("live-fade-starts", recovery.FadeAlpha > 0f && recovery.Pending, "Original HUD fade begins before teleport");
                Time.timeScale = 0f;
                yield return null;
                double clock = recovery.State.Clock;
                float alpha = recovery.FadeAlpha;
                for (int i = 0; i < 6; i++) { yield return null; Record("paused"); }
                Add("pause-stops-recovery-and-fade", recovery.State.Clock == clock && recovery.Pending
                    && recovery.FadeAlpha == alpha && motor.transform.position == originalPosition,
                    "Six real paused frames preserve Invoke wait, black alpha and location");
                Time.timeScale = 1f;
                for (int i = 0; i < 240 && recovery.Pending; i++) { yield return null; Record("resuming"); }
                Vector3 flatForward = Vector3.ProjectOnPlane(race.gates[0].forward, Vector3.up).normalized;
                Add("half-second-original-gate-teleport", recovery.State.Teleports == 1 && motor.Resets == 1
                    && Vector3.Distance(motor.transform.position, race.gates[0].position) < .0001f
                    && Quaternion.Angle(motor.transform.rotation, Quaternion.LookRotation(flatForward)) < .02f,
                    "Real delayed callback uses original last trigger position and flattened gate heading");
                Add("teleport-clears-original-motion-fields", motor.Speed == 0f && motor.CurrentTurn == 0f
                    && motor.DriveVelocity == Vector3.zero && motor.CurrentVerticalSpeed == 0f
                    && motor.SpinState.turned == 0f && motor.SpinState.flips == 0 && motor.gameObject.layer == 0,
                    "Freeze/UnFreeze plus Teleport clear original vectors, air turn count and gravity");
                int expectedWaypoint = Enumerable.Range(0, ai.waypoints.Length)
                    .OrderBy(i => Vector3.Distance(race.gates[0].position, ai.waypoints[i].position)).First();
                Add("ai-fall-end-restores-closest-waypoint", ai.WaypointIndex == expectedWaypoint,
                    "Original onFallEnd chooses waypoint nearest the recovered checkpoint");
                yield return new WaitForSeconds(.4f);
                Add("original-fade-finishes", recovery.FadeAlpha == 0f && !recovery.Pending,
                    "Original 0.33-second fade-out completes after teleport");
                items.TryGive(NativeKartItemKind.Power);
                yield return new WaitForSeconds(2.05f);
                items.UseItem();
                float topSpeed = motor.stats._topSpeed;
                recovery.Request(true);
                for (int i = 0; i < 240 && recovery.Pending; i++) yield return null;
                Add("recovery-retains-active-power", items.HasItem && items.State.used && items.State.powerRemaining > 2f
                    && motor.stats._topSpeed == topSpeed, "Original teleport keeps active item and its scaled timer");
                items.ResetItems();
                hit.TakeDamage(2f);
                recovery.Request(true);
                for (int i = 0; i < 240 && recovery.Pending; i++) { yield return null; Record("hit-recovery"); }
                Add("hit-recovery-keeps-health-and-items", hit.Invulnerable && hit.Frozen && items.Frozen
                    && hit.State.PendingCount == 1 && hit.damageFX.activeSelf && !hit.toHide.activeSelf
                    && motor.gameObject.layer == 0, "Original teleport unfreezes only Motor; existing Health Invoke and item freeze remain");
                for (int i = 0; i < 360 && hit.Frozen; i++) yield return null;
                Add("original-health-respawn-still-runs", !hit.Frozen && !hit.Invulnerable && !items.Frozen
                    && !hit.damageFX.activeSelf && hit.toHide.activeSelf && hit.State.Resumes == 1,
                    "Existing Health timer later restores effects and items without being cancelled by recovery");
                recovery.Request(true);
                race.ResetRace(false);
                Time.timeScale = 1f;
                foreach (var value in race.racers) value.enabled = false;
                yield return new WaitForSeconds(.7f);
                Record("race-reset");
                Add("race-reset-cancels-recovery", !recovery.Pending && recovery.State.Teleports == 0
                    && recovery.State.Requests == 0 && recovery.FadeAlpha == 0f && motor.Resets == 0,
                    "Race reset cancels pending teleport and all fade coroutines");
                Add("global-silence", AudioListener.volume == 0f, "Construction audio remains silent");
                Add("runtime-errors", race.VisualErrorCount == 0, "Runtime errors=" + race.VisualErrorCount);
            }
            finally
            {
                if (recovery) recovery.ResetState();
                if (hit) hit.ResetState();
                if (items) items.ResetItems();
                for (int i = 0; i < race.racers.Length; i++) { race.racers[i].enabled = motorEnabled[i]; aiComponents[i].enabled = aiEnabled[i]; }
                Time.timeScale = savedTimeScale;
                race.AllowCameraVerificationUpdates = savedAllow;
            }
            var report = new Report { version=Application.version, passed=checks.All(c => c.passed), checks=checks.ToArray(), samples=samples.ToArray(),
                boundary="Forest recovery uses original parameterless PlayerRacer.Fall delay and TeleportBack sequence with original HUD fade. Real frames cover pause, original checkpoint, AI callback, retained Power and Health timers, and race reset. Existing native R, below-track, water and stuck conditions feed this shared request path; other scenes' TriggerFall/FrozeFallType are outside this slice." };
            File.WriteAllText(Path.Combine(directory,"recovery-report.json"),JsonUtility.ToJson(report,true));
            Debug.Log("NATIVE_RECOVERY_RESULT passed="+report.passed+" checks="+checks.Count);
            Application.Quit(report.passed ? 0 : 2);
            void Add(string name,bool value,string detail) => checks.Add(new Check{name=name,passed=value,detail=detail});
            void Record(string phase) => samples.Add(new Sample{phase=phase,frame=Time.frameCount,clock=recovery.State.Clock,
                deadline=recovery.State.Deadline,requests=recovery.State.Requests,teleports=recovery.State.Teleports,
                resets=motor.Resets,alpha=recovery.FadeAlpha,timeScale=Time.timeScale,pending=recovery.Pending,
                invulnerable=hit.Invulnerable,healthFrozen=hit.Frozen,itemFrozen=items.Frozen,position=motor.transform.position});
        }
    }
}
