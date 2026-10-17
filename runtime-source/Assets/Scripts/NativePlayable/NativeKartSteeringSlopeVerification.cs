using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartSteeringSlopeVerification : MonoBehaviour
    {
        [Serializable] sealed class Contact
        {
            public string phase, collider;
            public int step;
            public Vector3 normal, point;
            public float angle;
        }
        [Serializable] sealed class Sample
        {
            public string phase;
            public int step, contacts;
            public bool grounded, restored;
            public float turn, speed, yaw, alignmentError, normalError;
            public Vector3 position, groundNormal, up;
        }
        [Serializable] sealed class SlopeCase
        {
            public float input, settledAlignmentError, finalYaw, maxNormalError;
            public int groundedSteps, restoredSteps, contactCount;
            public bool passed;
        }
        [Serializable] sealed class Report
        {
            public string version, boundary;
            public bool slopePassed, steepPassed;
            public float slopeLimit, steepContactAngle, steepNormalChange;
            public int steepContacts;
            public Vector3 steepNormalBefore, steepNormalAfter, steepRequestedMove, steepActualMove;
            public SlopeCase[] cases;
            public Sample[] samples;
            public Contact[] contacts;
        }

        Collider watched;
        string phase;
        int step;
        readonly List<Contact> contacts = new List<Contact>();

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.collider != watched) return;
            contacts.Add(new Contact
            {
                phase = phase, step = step, collider = hit.collider.name,
                normal = hit.normal, point = hit.point, angle = Vector3.Angle(Vector3.up, hit.normal)
            });
        }

        public static IEnumerator Run(NativeKartMotor motor, string directory, Action<string, bool, string> add)
        {
            var body = motor.GetComponent<CharacterController>();
            var observer = motor.gameObject.AddComponent<NativeKartSteeringSlopeVerification>();
            var samples = new List<Sample>();
            var cases = new List<SlopeCase>();
            var slope = new GameObject("Steering verification 20 degree slope");
            var slopeBox = slope.AddComponent<BoxCollider>();
            slopeBox.size = new Vector3(120, 1, 120);
            slope.transform.rotation = Quaternion.Euler(0, 0, 20);
            Vector3 normal = slope.transform.up;
            Vector3 origin = new Vector3(0, 800, 0);
            slope.transform.position = origin - normal * 0.5f;
            var steep = new GameObject("Steering verification 68 degree wall");
            var steepBox = steep.AddComponent<BoxCollider>();
            steepBox.size = new Vector3(20, 1, 20);
            steep.transform.rotation = Quaternion.Euler(0, 0, 68);
            steep.transform.position = new Vector3(4, 900, 0) - steep.transform.up * 0.5f;
            Physics.SyncTransforms();
            var tick = new WaitForFixedUpdate();
            var report = new Report
            {
                version = Application.version, slopeLimit = body.slopeLimit,
                boundary = "Actual CharacterController contacts on a 20-degree banked plane and a 68-degree wall. "
                    + "The wall probe pauses motor stepping and performs one horizontal Move to isolate contact-normal selection; "
                    + "it does not claim restored steep-slope locomotion or original Player trajectory parity."
            };
            try
            {
                foreach (float input in new[] { -0.7f, 0.7f })
                {
                    motor.ResetAt(origin + Vector3.up * 0.45f, Quaternion.identity, false);
                    motor.driving = true;
                    motor.throttle = motor.steering = 0f;
                    motor.braking = motor.drifting = false;
                    observer.watched = slopeBox;
                    observer.phase = input < 0f ? "slope-left-settle" : "slope-right-settle";
                    Time.timeScale = 1f;
                    for (int i = 0; i < 120; i++)
                    {
                        observer.step = i;
                        yield return tick;
                    }
                    var result = new SlopeCase { input = input,
                        settledAlignmentError = Vector3.Angle(motor.transform.up, normal) };
                    Vector3 startForward = Vector3.ProjectOnPlane(motor.transform.forward, normal).normalized;
                    int firstContact = observer.contacts.Count;
                    motor.steering = input;
                    motor.throttle = 0.35f;
                    observer.phase = input < 0f ? "slope-left-drive" : "slope-right-drive";
                    for (int i = 0; i < 60; i++)
                    {
                        observer.step = i;
                        int beforeContacts = observer.contacts.Count;
                        yield return tick;
                        if (motor.Grounded) result.groundedSteps++;
                        if (motor.UsesRestoredSteering) result.restoredSteps++;
                        float normalError = Vector3.Angle(motor.GroundNormal, normal);
                        if (motor.Grounded) result.maxNormalError = Mathf.Max(result.maxNormalError, normalError);
                        result.finalYaw = Vector3.SignedAngle(startForward,
                            Vector3.ProjectOnPlane(motor.transform.forward, normal), normal);
                        samples.Add(new Sample
                        {
                            phase = observer.phase, step = i, contacts = observer.contacts.Count - beforeContacts,
                            grounded = motor.Grounded, restored = motor.UsesRestoredSteering,
                            turn = motor.CurrentTurn, speed = motor.Speed, yaw = result.finalYaw,
                            alignmentError = Vector3.Angle(motor.transform.up, normal), normalError = normalError,
                            position = motor.transform.position, groundNormal = motor.GroundNormal, up = motor.transform.up
                        });
                    }
                    result.contactCount = observer.contacts.Count - firstContact;
                    result.passed = result.settledAlignmentError < 0.5f
                        && result.groundedSteps >= 55 && result.restoredSteps >= 55
                        && result.contactCount > 0 && result.maxNormalError < 0.2f
                        && result.finalYaw * Mathf.Sign(input) > 15f;
                    cases.Add(result);
                }
                report.slopePassed = cases.TrueForAll(c => c.passed);
                add("slope-contact-and-steering", report.slopePassed,
                    "Real 20-degree slope: left/right yaw=" + cases[0].finalYaw + "/" + cases[1].finalYaw
                    + "; settled up errors=" + cases[0].settledAlignmentError + "/" + cases[1].settledAlignmentError
                    + "; contact and per-step samples in steering-slope-report.json");

                // The sole contacted face isolates rejection from later floor callbacks that could mask a bad normal.
                Time.timeScale = 0f;
                motor.ResetAt(new Vector3(0, 900.3f, 0), Quaternion.identity, false);
                motor.throttle = motor.steering = 0f;
                motor.braking = motor.drifting = false;
                observer.watched = steepBox;
                observer.phase = "steep-wall-contact";
                observer.step = 0;
                int contactStart = observer.contacts.Count;
                report.steepNormalBefore = motor.GroundNormal;
                Vector3 before = motor.transform.position;
                report.steepRequestedMove = Vector3.right * 6f;
                body.Move(report.steepRequestedMove);
                report.steepActualMove = motor.transform.position - before;
                report.steepContacts = observer.contacts.Count - contactStart;
                report.steepNormalAfter = motor.GroundNormal;
                report.steepNormalChange = Vector3.Angle(report.steepNormalBefore, report.steepNormalAfter);
                bool rejectedFaceObserved = false;
                for (int i = contactStart; i < observer.contacts.Count; i++)
                {
                    float angle = observer.contacts[i].angle;
                    if (angle > body.slopeLimit && angle < Mathf.Acos(0.35f) * Mathf.Rad2Deg)
                    {
                        rejectedFaceObserved = true;
                        report.steepContactAngle = angle;
                    }
                }
                report.steepPassed = rejectedFaceObserved && report.steepNormalChange < 0.1f;
                add("slope-limit-rejects-steep-contact", report.steepPassed,
                    "Real wall hits=" + report.steepContacts + "; observed rejected normal=" + report.steepContactAngle
                    + " degrees; slopeLimit=" + body.slopeLimit + "; stored normal change=" + report.steepNormalChange);
            }
            finally
            {
                Time.timeScale = 0f;
                Destroy(slope);
                Destroy(steep);
                Destroy(observer);
            }
            report.cases = cases.ToArray();
            report.samples = samples.ToArray();
            report.contacts = observer.contacts.ToArray();
            File.WriteAllText(Path.Combine(directory, "steering-slope-report.json"), JsonUtility.ToJson(report, true));
        }
    }
}
