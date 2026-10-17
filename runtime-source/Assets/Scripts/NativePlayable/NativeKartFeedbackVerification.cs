using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartFeedbackVerification
    {
        [Serializable]
        sealed class Check
        {
            public string name, detail;
            public bool passed;
        }

        [Serializable]
        sealed class Trace
        {
            public string schedule;
            public int steps;
            public NativeKartFeedbackState afterGrass, afterRoad, afterWater;
        }

        [Serializable]
        sealed class GrassContact
        {
            public string evidence, collider, material;
            public Vector3 raycastPoint, startPosition;
            public int candidates, attempts, grassFrames, surfaceFrames, contactFrames;
            public float maxDirt, maxWheelDirt, maxSpeed;
            public bool passed;
        }

        [Serializable]
        sealed class Report
        {
            public string version, platform, graphics, evidence;
            public bool passed, liveBindingChecked;
            public Check[] checks;
            public Trace[] traces;
            public string[] captures;
            public GrassContact grassContact;
        }

        const float StateTolerance = 0.006f;
        const float MaterialTolerance = 0.0001f;

        public static IEnumerator Run(NativeRace race, string reportDirectory)
        {
            Directory.CreateDirectory(reportDirectory);
            var checks = new List<Check>();
            var traces = new List<Trace>();
            var captures = new List<string>();
            NativeKartFeedback live = null;
            var report = new Report
            {
                version = Application.version,
                platform = SystemInfo.operatingSystem,
                graphics = SystemInfo.graphicsDeviceName,
                evidence = "Deterministic source-formula fixtures in the native Player; live renderer/property-block binding. No original-game trajectory replay."
            };

            try
            {
                live = race.racers[0].GetComponentInChildren<NativeKartFeedback>(true);
                CheckThat(checks, "live-component", live && live.settings != null && live.targets != null && live.targets.Length > 0,
                    live ? "Player feedback component and renderer targets" : "Player feedback component missing");
                var settings = live && live.settings != null ? live.settings : new NativeKartFeedbackSettings();
                bool originalConfiguration = race.racers.Length == 4;
                bool mappedSurfaces = race.racers.Length == 4;
                for (int i = 0; i < race.racers.Length; i++)
                {
                    var mapped = race.racers[i].GetComponent<NativeKartFeedback>();
                    originalConfiguration &= mapped && OriginalDefaults(mapped.settings);
                    mappedSurfaces &= race.racers[i].feedbackSurfaces != null
                        && race.racers[i].feedbackSurfaces.Length == 4;
                    if (race.racers[i].feedbackSurfaces != null)
                        foreach (var surface in race.racers[i].feedbackSurfaces)
                            mappedSurfaces &= surface != null && surface.material != null;
                }
                CheckThat(checks, "four-original-settings", originalConfiguration,
                    "All four racers use the numeric ColoredKartComponent values in the original Kart/Bike prefabs");
                CheckThat(checks, "four-surface-mappings", mappedSurfaces,
                    "Each racer has four linked original Physics surface materials");
                var clean = NativeKartFeedbackState.Initial(settings);
                var stopped = new NativeKartFeedbackInput { speedRatio = 0f, topSpeedModifier = 1f };
                var idle = Integrate(clean, stopped, settings, 3f, 1f / 60f);
                CheckThat(checks, "stationary-clean", SameState(clean, idle, MaterialTolerance),
                    "Three seconds stationary on road retain clean zero state");

                var speeding = new NativeKartFeedbackInput { speedRatio = 1f, topSpeedModifier = 1f };
                var warm = Integrate(clean, speeding, settings, 4f, 1f / 60f);
                var cool = Integrate(warm, stopped, settings, 2f, 1f / 60f);
                CheckThat(checks, "speed-rise-cooldown", warm.heat > clean.heat && warm.wheelHeat > clean.wheelHeat
                    && Near(warm.heat, 0.1f, MaterialTolerance) && Near(warm.wheelHeat, 0.6f, MaterialTolerance)
                    && Near(cool.heat, 0.04f, MaterialTolerance) && Near(cool.wheelHeat, 0.4f, MaterialTolerance),
                    "Original defaults: 4 s speed=1 gives heat .1 / wheel .6; 2 s stopped gives .04 / .4");
                var modified = Integrate(clean, new NativeKartFeedbackInput { speedRatio = 1f, topSpeedModifier = 2f }, settings, 4f, 1f / 60f);
                CheckThat(checks, "heat-speed-modifier", Near(modified.heat, 0.2f, MaterialTolerance),
                    "Top-speed modifier 2 doubles the original heat gain");
                var below = Integrate(clean, new NativeKartFeedbackInput { speedRatio = Mathf.Max(0f, settings._wheelHeatMinSpd - 0.01f), topSpeedModifier = 1f }, settings, 1f, 1f / 60f);
                var above = Integrate(clean, new NativeKartFeedbackInput { speedRatio = Mathf.Min(1f, settings._wheelHeatMinSpd + 0.01f), topSpeedModifier = 1f }, settings, 1f, 1f / 60f);
                CheckThat(checks, "wheel-heat-threshold", below.wheelHeat <= MaterialTolerance
                    && Near(above.wheelHeat, 0.15f, MaterialTolerance),
                    "Below .7 wheel heat remains zero; above .7 it gains .15 in one second");

                var grass = new NativeKartFeedbackInput
                {
                    speedRatio = 0.8f, topSpeedModifier = 1f, onGrass = true, hasSurface = true,
                    surfaceColor = new Color(0.34f, 0.22f, 0.08f, 1f), surfaceColorEnabled = 1f
                };
                var road = new NativeKartFeedbackInput { speedRatio = 0.8f, topSpeedModifier = 1f };
                var water = grass;
                water.inWater = true;
                var dirty = Integrate(clean, grass, settings, 2f, 1f / 60f);
                var fading = Integrate(dirty, road, settings, 2f, 1f / 60f);
                var washed = Integrate(fading, water, settings, 1f, 1f / 60f);
                CheckThat(checks, "grass-road-water", Near(dirty.dirt, 0.28f, MaterialTolerance)
                    && Near(dirty.wheelDirt, 0.75f, MaterialTolerance)
                    && Near(fading.dirt, 0.2544f, MaterialTolerance)
                    && Near(fading.wheelDirt, 0.47f, MaterialTolerance)
                    && Near(washed.dirt, 0f, MaterialTolerance)
                    && Near(washed.wheelDirt, 0f, MaterialTolerance)
                    && dirty.dirtColorEnabled > 0f,
                    "Original defaults: grass 2 s .28/.75, road 2 s .2544/.47, water 1 s zero");
                var saturated = Integrate(clean, grass, settings, 20f, 1f / 60f);
                CheckThat(checks, "state-clamps", InBounds(saturated) && Near(saturated.dirt, 1f, MaterialTolerance)
                    && Near(saturated.wheelDirt, 0.75f, MaterialTolerance)
                    && saturated.wheelHeat <= settings._wheelHeatMax + MaterialTolerance,
                    "Dirt, wheel dirt and wheel heat obey the source caps");
                var paused = NativeKartFeedbackState.Step(dirty, water, settings, 0f);
                CheckThat(checks, "paused-zero-dt", SameState(dirty, paused, 0f), "Zero game-time step leaves every state field unchanged");
                var reset = NativeKartFeedbackState.Initial(settings);
                CheckThat(checks, "new-race-reset", SameState(reset, clean, 0f), "A new race initializes clean state and configured color");

                float[] fixedSteps = { 1f / 30f, 1f / 60f, 1f / 120f };
                Trace reference = null;
                foreach (float dt in fixedSteps)
                {
                    Trace trace = Scenario(clean, settings, grass, road, water, dt, "fixed-" + Mathf.RoundToInt(1f / dt));
                    traces.Add(trace);
                    if (reference == null) reference = trace;
                    else CheckThat(checks, "frame-rate-" + trace.schedule,
                        SameState(reference.afterGrass, trace.afterGrass, StateTolerance)
                        && SameState(reference.afterRoad, trace.afterRoad, StateTolerance)
                        && SameState(reference.afterWater, trace.afterWater, StateTolerance),
                        "Three phase boundaries agree with 30 FPS source-formula fixture");
                }
                var variable = VariableScenario(clean, settings, grass, road, water);
                traces.Add(variable);
                CheckThat(checks, "frame-rate-variable",
                    SameState(reference.afterGrass, variable.afterGrass, StateTolerance)
                    && SameState(reference.afterRoad, variable.afterRoad, StateTolerance)
                    && SameState(reference.afterWater, variable.afterWater, StateTolerance),
                    "Variable frame times agree with fixed-step phase boundaries");

                if (live)
                {
                    bool wasEnabled = live.enabled;
                    live.enabled = false;
                    try
                    {
                        var paint = SnapshotPaint(live);
                        live.RebindTargets();
                        CheckThat(checks, "explicit-rebind", CheckMaterialBlocks(live, paint, null),
                            "Explicit target refresh retains active material slots and paint");
                        live.Reset();
                        live.Step(grass, 2f);
                        report.liveBindingChecked = CheckMaterialBlocks(live, paint, checks);
                        live.Reset();
                        CheckThat(checks, "live-reset", SameState(live.State, clean, MaterialTolerance)
                            && CheckMaterialBlocks(live, paint, null),
                            "Player material state returns to clean without changing character paint");
                    }
                    finally { live.enabled = wasEnabled; }
                }
            }
            catch (Exception error)
            {
                CheckThat(checks, "verification-exception", false, error.ToString());
            }

            report.grassContact = new GrassContact
            {
                evidence = "Player CharacterController relocated to a raycast-confirmed Forest Grass collider; 1.5 s live FixedUpdate/Update with throttle=1."
            };
            if (live) yield return VerifyGrassContact(race, live, report.grassContact);
            CheckThat(checks, "live-grass-contact", report.grassContact.passed,
                "Real Forest collider and mapped PhysicMaterial produced OnGrass, surface lookup and positive dirt");
            if (live) yield return CaptureFixtures(race, live, reportDirectory, captures, checks);
            CheckThat(checks, "visual-captures", captures.Count == 3,
                "clean, heated and dirty Player close-up screenshots from forced fixture inputs");

            report.checks = checks.ToArray();
            report.traces = traces.ToArray();
            report.captures = captures.ToArray();
            report.passed = report.liveBindingChecked && checks.TrueForAll(check => check.passed);
            File.WriteAllText(Path.Combine(reportDirectory, "feedback-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_FEEDBACK_RESULT passed=" + report.passed + " checks=" + checks.Count);
            yield return null;
            Application.Quit(report.passed ? 0 : 2);
        }

        static Trace Scenario(NativeKartFeedbackState clean, NativeKartFeedbackSettings settings,
            NativeKartFeedbackInput grass, NativeKartFeedbackInput road, NativeKartFeedbackInput water,
            float dt, string name)
        {
            var trace = new Trace { schedule = name };
            trace.afterGrass = Integrate(clean, grass, settings, 2f, dt);
            trace.afterRoad = Integrate(trace.afterGrass, road, settings, 2f, dt);
            trace.afterWater = Integrate(trace.afterRoad, water, settings, 1f, dt);
            trace.steps = Mathf.RoundToInt(5f / dt);
            return trace;
        }

        static IEnumerator CaptureFixtures(NativeRace race, NativeKartFeedback feedback, string directory,
            List<string> captures, List<Check> checks)
        {
            var camera = race.raceCamera;
            var previousPosition = camera.transform.position;
            var previousRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            bool wasEnabled = feedback.enabled;
            feedback.enabled = false;
            race.SetVisualCapture("race", false);
            var car = race.racers[0].transform;
            camera.transform.position = car.TransformPoint(new Vector3(3f, 2f, -4f));
            camera.transform.LookAt(car.position + Vector3.up * 0.65f);
            camera.fieldOfView = 45f;
            try
            {
                feedback.Reset();
                yield return Capture("clean.png");
                feedback.Step(new NativeKartFeedbackInput { speedRatio = 1f, topSpeedModifier = 1f }, 8f);
                yield return Capture("heated.png");
                feedback.Reset();
                feedback.Step(new NativeKartFeedbackInput
                {
                    speedRatio = 0.8f, topSpeedModifier = 1f, onGrass = true, hasSurface = true,
                    surfaceColor = Color.white, surfaceColorEnabled = 0f
                }, 8f);
                yield return Capture("dirty.png");
            }
            finally
            {
                feedback.Reset();
                feedback.enabled = wasEnabled;
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                camera.fieldOfView = previousFov;
            }

            IEnumerator Capture(string name)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                yield return new WaitForEndOfFrame();
                try
                {
                    var image = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(directory, name), image.EncodeToPNG());
                    UnityEngine.Object.Destroy(image);
                    captures.Add(name);
                    Debug.Log("NATIVE_FEEDBACK_CAPTURE " + name + " fixture=forced-state");
                }
                catch (Exception error)
                {
                    CheckThat(checks, "capture-" + name, false, error.ToString());
                }
            }
        }

        static IEnumerator VerifyGrassContact(NativeRace race, NativeKartFeedback feedback, GrassContact result)
        {
            var motor = race.racers[0];
            Vector3 originalPosition = motor.transform.position;
            Quaternion originalRotation = motor.transform.rotation;
            float originalTimeScale = Time.timeScale;
            bool originalDriving = motor.driving;
            float originalThrottle = motor.throttle;
            float originalSteering = motor.steering;
            bool originalBraking = motor.braking;
            bool originalDrifting = motor.drifting;
            bool originalFeedbackEnabled = feedback.enabled;
            var candidatePoints = new List<RaycastHit>();
            var candidateRotations = new List<Quaternion>();
            float[] offsets = { 8f, -8f, 12f, -12f, 20f, -20f, 30f, -30f };
            for (int i = 0; i < race.route.Length; i++)
            {
                Vector3 tangent = race.route[(i + 1) % race.route.Length] - race.route[i];
                tangent.y = 0;
                if (tangent.sqrMagnitude < 0.01f) continue;
                tangent.Normalize();
                Vector3 across = Vector3.Cross(Vector3.up, tangent);
                foreach (float offset in offsets)
                {
                    Vector3 origin = race.route[i] + across * offset + Vector3.up * 20f;
                    RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 60f);
                    Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                    foreach (RaycastHit hit in hits)
                    {
                        if (!hit.collider || !hit.collider.CompareTag("Grass") || hit.normal.y <= 0.7f) continue;
                        bool mapped = false;
                        foreach (NativeFeedbackSurface surface in motor.feedbackSurfaces)
                            if (surface != null && surface.material && surface.material == hit.collider.sharedMaterial)
                            { mapped = true; break; }
                        if (!mapped) continue;
                        candidatePoints.Add(hit);
                        candidateRotations.Add(Quaternion.LookRotation(tangent));
                        break;
                    }
                }
            }
            result.candidates = candidatePoints.Count;
            try
            {
                feedback.enabled = true;
                Time.timeScale = 1f;
                int limit = Mathf.Min(candidatePoints.Count, 8);
                for (int attempt = 0; attempt < limit && !result.passed; attempt++)
                {
                    int i = attempt * candidatePoints.Count / limit;
                    RaycastHit hit = candidatePoints[i];
                    result.attempts++;
                    result.collider = hit.collider.name;
                    result.material = hit.collider.sharedMaterial.name;
                    result.raycastPoint = hit.point;
                    result.startPosition = hit.point + Vector3.up * 0.15f;
                    result.grassFrames = result.surfaceFrames = result.contactFrames = 0;
                    result.maxDirt = result.maxWheelDirt = result.maxSpeed = 0f;
                    motor.ResetAt(result.startPosition, candidateRotations[i], false);
                    motor.driving = true;
                    motor.throttle = 1f;
                    motor.steering = 0f;
                    motor.braking = motor.drifting = false;
                    Physics.SyncTransforms();
                    float until = Time.realtimeSinceStartup + 1.5f;
                    while (Time.realtimeSinceStartup < until)
                    {
                        yield return null;
                        bool grass = motor.OnGrass;
                        bool surface = motor.FeedbackInput.hasSurface;
                        if (grass) result.grassFrames++;
                        if (surface) result.surfaceFrames++;
                        if (grass && surface) result.contactFrames++;
                        result.maxDirt = Mathf.Max(result.maxDirt, feedback.State.dirt);
                        result.maxWheelDirt = Mathf.Max(result.maxWheelDirt, feedback.State.wheelDirt);
                        result.maxSpeed = Mathf.Max(result.maxSpeed, motor.Speed);
                    }
                    result.passed = result.contactFrames > 0 && result.maxDirt > 0f && result.maxWheelDirt > 0f;
                }
            }
            finally
            {
                motor.driving = false;
                motor.throttle = motor.steering = 0f;
                motor.braking = motor.drifting = false;
                motor.ResetAt(originalPosition, originalRotation, false);
                motor.driving = originalDriving;
                motor.throttle = originalThrottle;
                motor.steering = originalSteering;
                motor.braking = originalBraking;
                motor.drifting = originalDrifting;
                feedback.enabled = originalFeedbackEnabled;
                Time.timeScale = originalTimeScale;
            }
        }

        static Trace VariableScenario(NativeKartFeedbackState clean, NativeKartFeedbackSettings settings,
            NativeKartFeedbackInput grass, NativeKartFeedbackInput road, NativeKartFeedbackInput water)
        {
            float[] steps = { 1f / 30f, 1f / 120f, 1f / 45f, 1f / 90f };
            int count = 0;
            var trace = new Trace { schedule = "variable" };
            trace.afterGrass = IntegrateVariable(clean, grass, settings, 2f, steps, ref count);
            trace.afterRoad = IntegrateVariable(trace.afterGrass, road, settings, 2f, steps, ref count);
            trace.afterWater = IntegrateVariable(trace.afterRoad, water, settings, 1f, steps, ref count);
            trace.steps = count;
            return trace;
        }

        static NativeKartFeedbackState Integrate(NativeKartFeedbackState state, NativeKartFeedbackInput input,
            NativeKartFeedbackSettings settings, float seconds, float step)
        {
            int count = Mathf.RoundToInt(seconds / step);
            for (int i = 0; i < count; i++) state = NativeKartFeedbackState.Step(state, input, settings, seconds / count);
            return state;
        }

        static NativeKartFeedbackState IntegrateVariable(NativeKartFeedbackState state, NativeKartFeedbackInput input,
            NativeKartFeedbackSettings settings, float seconds, float[] steps, ref int totalSteps)
        {
            float elapsed = 0f;
            int index = 0;
            while (elapsed < seconds - 0.000001f)
            {
                float dt = Mathf.Min(steps[index++ % steps.Length], seconds - elapsed);
                state = NativeKartFeedbackState.Step(state, input, settings, dt);
                elapsed += dt;
                totalSteps++;
            }
            return state;
        }

        static bool InBounds(NativeKartFeedbackState state) => state.dirt >= 0f && state.dirt <= 1f
            && state.wheelDirt >= 0f && state.wheelDirt <= 0.75f
            && state.wheelHeat >= 0f && state.wheelHeat <= 1f;

        static bool OriginalDefaults(NativeKartFeedbackSettings s) => s != null
            && Near(s._heatDecay, 0.03f, MaterialTolerance) && Near(s._heatGain, 0.025f, MaterialTolerance)
            && Near(s._dirtGain, 0.175f, MaterialTolerance) && Near(s._dirtLose, 0.016f, MaterialTolerance)
            && Near(s._dirtWheelGain, 1.5f, MaterialTolerance) && Near(s._dirtWheelLose, 0.175f, MaterialTolerance)
            && Near(s._dirtWaterLose, 4f, MaterialTolerance) && Near(s._dirtWaterLoseStill, 0.3f, MaterialTolerance)
            && Near(s._wheelHeatMinSpd, 0.7f, MaterialTolerance) && Near(s._wheelHeatMax, 0.9f, MaterialTolerance)
            && Near(s._wheelHeatGain, 0.15f, MaterialTolerance) && Near(s._wheelHeatLose, 0.1f, MaterialTolerance)
            && Near(s._dirtColorBlend, 0.1f, MaterialTolerance);

        static bool Near(float a, float b, float tolerance) => Mathf.Abs(a - b) <= tolerance;

        static bool SameColor(Color a, Color b, float tolerance) => Near(a.r, b.r, tolerance)
            && Near(a.g, b.g, tolerance) && Near(a.b, b.b, tolerance) && Near(a.a, b.a, tolerance);

        static bool SameState(NativeKartFeedbackState a, NativeKartFeedbackState b, float tolerance)
            => Near(a.heat, b.heat, tolerance) && Near(a.dirt, b.dirt, tolerance)
            && Near(a.wheelDirt, b.wheelDirt, tolerance) && Near(a.wheelHeat, b.wheelHeat, tolerance)
            && SameColor(a.dirtColor, b.dirtColor, tolerance)
            && Near(a.dirtColorEnabled, b.dirtColorEnabled, tolerance);

        sealed class Paint
        {
            public Renderer renderer;
            public int index;
            public Color primary, secondary;
            public Color materialPrimary, materialSecondary;
            public float sentinel;
        }

        static List<Paint> SnapshotPaint(NativeKartFeedback feedback)
        {
            var paint = new List<Paint>();
            var block = new MaterialPropertyBlock();
            foreach (Renderer target in feedback.targets)
            {
                if (!target) continue;
                var materials = target.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    if (!materials[index] || !materials[index].shader || materials[index].shader.name != "Fumo/RestoredKart") continue;
                    target.GetPropertyBlock(block, index);
                    float sentinel = 17.125f + index;
                    block.SetFloat("_Border", sentinel);
                    target.SetPropertyBlock(block, index);
                    paint.Add(new Paint { renderer = target, index = index,
                        primary = block.GetColor("_Color"), secondary = block.GetColor("_Color2"),
                        materialPrimary = materials[index].GetColor("_Color"),
                        materialSecondary = materials[index].GetColor("_Color2"), sentinel = sentinel });
                }
            }
            return paint;
        }

        static bool CheckMaterialBlocks(NativeKartFeedback feedback, List<Paint> paint, List<Check> checks)
        {
            bool any = paint.Count > 0;
            bool values = any;
            bool colors = any;
            var state = feedback.State;
            var block = new MaterialPropertyBlock();
            foreach (Paint item in paint)
            {
                item.renderer.GetPropertyBlock(block, item.index);
                values &= Near(block.GetFloat("_SpeedIntensity"), state.heat * 5f, MaterialTolerance)
                    && Near(block.GetFloat("_DirtAmount"), state.dirt, MaterialTolerance)
                    && Near(block.GetFloat("_WheelDirtAmount"), state.wheelDirt, MaterialTolerance)
                    && Near(block.GetFloat("_WheelHeatAmount"), state.wheelHeat, MaterialTolerance)
                    && SameColor(block.GetColor("_DirtColor"), state.dirtColor, MaterialTolerance)
                    && Near(block.GetFloat("_DirtColorEnabled"), state.dirtColorEnabled, MaterialTolerance);
                colors &= SameColor(block.GetColor("_Color"), item.primary, MaterialTolerance)
                    && SameColor(block.GetColor("_Color2"), item.secondary, MaterialTolerance)
                    && Near(block.GetFloat("_Border"), item.sentinel, MaterialTolerance);
                var material = item.renderer.sharedMaterials[item.index];
                colors &= SameColor(material.GetColor("_Color"), item.materialPrimary, MaterialTolerance)
                    && SameColor(material.GetColor("_Color2"), item.materialSecondary, MaterialTolerance);
            }
            if (checks != null)
            {
                CheckThat(checks, "live-property-block", values, "All Player Kart materials expose six feedback properties");
                CheckThat(checks, "character-paint-preserved", colors, "Primary and secondary paint remain unchanged in every material slot");
            }
            return values && colors;
        }

        static void CheckThat(List<Check> checks, string name, bool passed, string detail)
            => checks.Add(new Check { name = name, passed = passed, detail = detail });
    }
}
