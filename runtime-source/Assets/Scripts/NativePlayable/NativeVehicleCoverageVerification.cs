using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeVehicleCoverageVerification
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class VehicleSample
        {
            public string classId;
            public bool grounded, drove, wallContact;
            public int steps, contacts;
            public float distance, topSpeed;
            public Vector3 position, hitNormal;
        }
        [Serializable] sealed class Report
        {
            public string version, trackId;
            public bool passed;
            public Check[] checks;
            public VehicleSample[] vehicles;
        }

        public static IEnumerator Run(NativeRace race, string directory)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            var samples = new List<VehicleSample>();
            void Add(string name, bool passed, string detail) =>
                checks.Add(new Check { name = name, passed = passed, detail = detail });

            var roster = race.vehicleRoster;
            if (!roster) throw new InvalidOperationException("Original vehicle roster is missing");
            var player = race.racers[0];
            var drivers = race.racers.Select(r => r.GetComponent<NativeKartOriginalAI>()).ToArray();
            var motorEnabled = race.racers.Select(r => r.enabled).ToArray();
            var driverEnabled = drivers.Select(d => d.enabled).ToArray();
            int defaultCharacter = roster.DefaultCharacterIndex;
            float savedScale = Time.timeScale;
            bool savedCamera = race.AllowCameraVerificationUpdates;
            try
            {
                race.ResetRace(false);
                race.SetVisualCapture("race", true);
                Time.timeScale = 1f;
                race.AllowCameraVerificationUpdates = true;
                foreach (var racer in race.racers) racer.enabled = false;
                foreach (var driver in drivers) driver.enabled = false;
                Add("original-roster-counts", roster.CharacterCount == 24 && roster.VehicleCount == 7
                    && roster.RealSkateSettings != null && roster.RealSkateModel,
                    "characters=" + roster.CharacterCount + " selectableClasses=" + roster.VehicleCount
                    + " supplementalRealSkate=" + (roster.RealSkateSettings != null));

                var kart = player.driftPivot.Find("AnimPivot/Kart");
                var fumo = player.driftPivot.Find("AnimPivot/AccelPivot/Accel2Pivot/FumoPivot");
                var originalKart = kart.GetChild(0).gameObject;
                var wheelVisual = player.GetComponent<NativeKartWheelVisual>();
                var kartFeedback = player.GetComponent<NativeKartFeedback>();
                for (int ci = 0; ci < roster.CharacterCount; ci++)
                {
                    string id = roster.CharacterNameAt(ci);
                    roster.ApplySelection(ci, 0, player);
                    yield return null;
                    var vehicleVisuals = kart.GetComponentsInChildren<Renderer>(true)
                        .Where(r => r.gameObject.activeInHierarchy).ToArray();
                    var driverVisuals = fumo.GetComponentsInChildren<Renderer>(true)
                        .Where(r => r.gameObject.activeInHierarchy).ToArray();
                    bool hasSelectedModel = originalKart.activeSelf && wheelVisual && wheelVisual.enabled &&
                        kartFeedback && kartFeedback.enabled;
                    bool hasSelectedDriver = ci == defaultCharacter ||
                        fumo.Cast<Transform>().Any(t => t.gameObject.activeSelf && t.name == "Driver-" + id);
                    Add("character-" + id, roster.CharacterIconAt(ci) && hasSelectedModel && hasSelectedDriver
                        && vehicleVisuals.Length > 0 && driverVisuals.Length > 0,
                        "vehicleRenderers=" + vehicleVisuals.Length + " driverRenderers=" + driverVisuals.Length);
                }

                for (int vi = 0; vi <= roster.VehicleCount; vi++)
                {
                    bool selectable = vi < roster.VehicleCount;
                    string id = selectable ? roster.VehicleNameAt(vi) : "RealSkate";
                    var settings = selectable ? roster.vehicles[vi].settings : roster.RealSkateSettings;
                    if (selectable)
                    {
                        roster.ApplySelection(defaultCharacter, vi, player);
                        yield return new WaitForSeconds(.2f);
                        yield return new WaitForEndOfFrame();
                        bool selected = player.stats == settings;
                        bool visual = vi == 0
                            ? originalKart.activeSelf && wheelVisual && wheelVisual.enabled &&
                                kartFeedback && kartFeedback.enabled
                            : !originalKart.activeSelf && kart.Cast<Transform>().Any(t => t.gameObject.activeSelf
                                && t.name == "Vehicle-" + roster.CharacterNameAt(defaultCharacter) + "-" + id);
                        Add("vehicle-selection-" + id, selected && visual,
                            "settings=" + selected + " selectedModel=" + visual);
                        string imagePath = Path.Combine(directory, "vehicle-" + id + ".png");
                        CaptureVehicleImage(race.raceCamera, player.transform, imagePath);
                        Add("vehicle-image-" + id, File.Exists(imagePath) && new FileInfo(imagePath).Length > 10000,
                            "image=" + imagePath);
                    }
                    else Add("supplemental-real-skate-source", settings != null && roster.RealSkateModel,
                        "RealSkate is an original supplemental vehicle asset");

                    var fixture = new GameObject("VehicleCoverage_" + id);
                    var sample = new VehicleSample { classId = id };
                    try
                    {
                        Vector3 origin = new Vector3(10000f + vi * 500f, 1000f, 10000f);
                        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        ground.transform.SetParent(fixture.transform);
                        ground.transform.position = origin + new Vector3(0f, -0.5f, 0f);
                        ground.transform.localScale = new Vector3(100f, 1f, 100f);
                        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        wall.transform.SetParent(fixture.transform);
                        wall.transform.position = origin + new Vector3(0f, 2f, 5f);
                        wall.transform.localScale = new Vector3(8f, 4f, 1f);
                        var pilot = CreateMotor(player, settings,
                            fixture.transform, origin + Vector3.up * 2f);
                        Physics.SyncTransforms();
                        for (int step = 0; step < 120 && !pilot.Grounded; step++)
                        {
                            yield return new WaitForFixedUpdate();
                            sample.steps++;
                        }
                        sample.grounded = pilot.Grounded;
                        pilot.driving = true;
                        pilot.throttle = id == "CleanerBot" ? 0f : .5f;
                        for (int step = 0; step < 360; step++)
                        {
                            yield return new WaitForFixedUpdate();
                            sample.steps++;
                            if ((pilot.LastCollisionFlags & CollisionFlags.Sides) != 0 &&
                                pilot.LastStepSideContactNormal.sqrMagnitude > 0.1f)
                            { sample.wallContact = true; break; }
                        }
                        sample.distance = pilot.Distance;
                        sample.drove = pilot.Distance > 1f;
                        sample.contacts = pilot.CollisionContactCount;
                        sample.topSpeed = pilot.stats._topSpeed;
                        sample.position = pilot.transform.position;
                        sample.hitNormal = pilot.LastStepSideContactNormal;
                        Add("vehicle-drive-wall-" + id, sample.grounded && sample.drove &&
                            sample.wallContact && sample.contacts > 0,
                            "grounded=" + sample.grounded + " distance=" + sample.distance +
                            " contacts=" + sample.contacts + " wall=" + sample.wallContact +
                            " steps=" + sample.steps);
                        if (id == "CleanerBot") Add("cleaner-auto-acceleration", settings._autoAccel &&
                            pilot.throttle == 0f && sample.drove,
                            "auto=" + settings._autoAccel + " throttle=" + pilot.throttle +
                            " distance=" + sample.distance);
                    }
                    finally { UnityEngine.Object.Destroy(fixture); }
                    samples.Add(sample);
                    yield return null;
                }
                yield return VerifySpecialModules(roster, player, Add);
                Add("construction-muted", AudioListener.volume == 0f, "volume=" + AudioListener.volume);
                Add("runtime-errors", race.VisualErrorCount == 0, "errors=" + race.VisualErrorCount);
            }
            finally
            {
                roster.ApplySelection(defaultCharacter, 0, player);
                for (int i = 0; i < race.racers.Length; i++)
                {
                    race.racers[i].enabled = motorEnabled[i];
                    drivers[i].enabled = driverEnabled[i];
                }
                Time.timeScale = savedScale;
                race.AllowCameraVerificationUpdates = savedCamera;
            }
            var report = new Report
            {
                version = Application.version, trackId = race.trackId,
                passed = checks.All(c => c.passed), checks = checks.ToArray(), vehicles = samples.ToArray()
            };
            File.WriteAllText(Path.Combine(directory, "vehicle-coverage-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_VEHICLE_COVERAGE passed=" + report.passed + " checks=" + checks.Count);
            Application.Quit(report.passed ? 0 : 2);
        }

        static IEnumerator VerifySpecialModules(NativeVehicleRoster roster, NativeKartMotor reference,
            Action<string, bool, string> add)
        {
            NativeKartSettings Settings(string id) => roster.vehicles.Single(v => v.id == id).settings;
            var bike = Settings("Bike");
            add("bike-source-wheelie", bike._canWheelie &&
                Mathf.Approximately(bike._wheelieSpeedAdd, 25f) &&
                Mathf.Approximately(bike._wheelieSteerAdd, -14f) && !Settings("Kart")._canWheelie,
                "enabled=" + bike._canWheelie + " speedAdd=" + bike._wheelieSpeedAdd +
                " steerAdd=" + bike._wheelieSteerAdd);
            var bikeRoot = NewModuleGround("Bike", 6, false, out _);
            try
            {
                var pilot = CreateMotor(reference, bike, bikeRoot.transform,
                    new Vector3(23000f, 1002f, 10000f));
                Physics.SyncTransforms();
                for (int i = 0; i < 120 && !pilot.Grounded; i++) yield return new WaitForFixedUpdate();
                pilot.driving = true;
                pilot.WheelieInputVertical(1f);
                for (int i = 0; i < 25; i++) yield return new WaitForFixedUpdate();
                pilot.WheelieInputVertical(0f);
                pilot.WheelieInputVertical(-1f);
                yield return new WaitForFixedUpdate();
                pilot.WheelieInputVertical(0f);
                pilot.WheelieInputVertical(1f);
                bool timedOut = !pilot.IsWheelie;
                for (int i = 0; i < 25; i++) yield return new WaitForFixedUpdate();
                pilot.WheelieInputVertical(1f);
                yield return new WaitForFixedUpdate();
                pilot.WheelieInputVertical(0f);
                pilot.WheelieInputVertical(-1f);
                yield return new WaitForFixedUpdate();
                pilot.WheelieInputVertical(0f);
                pilot.WheelieInputVertical(1f);
                bool started = pilot.IsWheelie && Mathf.Approximately(pilot.WheelieBalance, .5f);
                pilot.braking = true;
                yield return new WaitForFixedUpdate();
                bool brakeStops = !pilot.IsWheelie;
                add("bike-wheelie-timing-and-brake", pilot.Grounded && timedOut && started && brakeStops,
                    "timedOut=" + timedOut + " started=" + started + " brakeStops=" + brakeStops);
                pilot.SetHitFrozen(true);
                add("bike-wheelie-freeze", !pilot.IsWheelie, "frozen=" + pilot.HitFrozen);
            }
            finally { UnityEngine.Object.Destroy(bikeRoot); }
            yield return null;

            var broom = Settings("Broom");
            add("broom-source-module", broom._moduleKind == NativeVehicleModuleKind.BroomAirBoost &&
                Mathf.Approximately(broom._moduleLimit, 4f) && Mathf.Approximately(broom._moduleSpeedBoost, 25f),
                "kind=" + broom._moduleKind + " limit=" + broom._moduleLimit + " boost=" + broom._moduleSpeedBoost);
            var broomRoot = NewModuleGround("Broom", 0, false, out _);
            try
            {
                var pilot = CreateMotor(reference, broom, broomRoot.transform,
                    new Vector3(20000f, 1002f, 10000f));
                Physics.SyncTransforms();
                for (int i = 0; i < 120 && !pilot.Grounded; i++) yield return new WaitForFixedUpdate();
                pilot.driving = true;
                pilot.throttle = .5f;
                for (int i = 0; i < 300 && !pilot.ModuleReady; i++) yield return new WaitForFixedUpdate();
                bool ready = pilot.Grounded && pilot.ModuleReady && pilot.ModuleCharge >= 3.99f;
                pilot.jump = true;
                yield return new WaitForFixedUpdate();
                float airSpeedBefore = Vector3.ProjectOnPlane(pilot.PhysicsVelocity, Vector3.up).magnitude;
                for (int i = 0; i < 60 && pilot.ModuleTriggerCount == 0; i++) yield return new WaitForFixedUpdate();
                float airSpeedAfter = Vector3.ProjectOnPlane(pilot.PhysicsVelocity, Vector3.up).magnitude;
                add("broom-ground-charge-air-trigger", ready && pilot.ModuleTriggerCount == 1 &&
                    pilot.ModuleBoostRemaining > 0f && pilot.ModuleCharge < .01f &&
                    airSpeedAfter > airSpeedBefore + 5f,
                    "ready=" + ready + " count=" + pilot.ModuleTriggerCount +
                    " remaining=" + pilot.ModuleBoostRemaining + " airSpeed=" +
                    airSpeedBefore + "->" + airSpeedAfter);
            }
            finally { UnityEngine.Object.Destroy(broomRoot); }
            yield return null;

            var genji = Settings("Genji");
            add("genji-source-module", genji._moduleKind == NativeVehicleModuleKind.GenjiGrassBoost &&
                genji._moduleSteps == 3 && genji._moduleSpeedBoosts != null &&
                genji._moduleSpeedBoosts.SequenceEqual(new[] { 35f, 50f, 65f }),
                "kind=" + genji._moduleKind + " steps=" + genji._moduleSteps);
            var partialRoot = NewModuleGround("Genji-partial", 7, true, out var partialGround);
            try
            {
                var pilot = CreateMotor(reference, genji, partialRoot.transform,
                    new Vector3(23500f, 1002f, 10000f));
                Physics.SyncTransforms();
                for (int i = 0; i < 120 && (!pilot.Grounded || !pilot.OnGrass); i++)
                    yield return new WaitForFixedUpdate();
                pilot.driving = true;
                for (int i = 0; i < 25; i++) yield return new WaitForFixedUpdate();
                bool partial = pilot.ModuleCharge > 0f && pilot.ModuleStage == 0;
                partialGround.tag = "Untagged";
                Physics.SyncTransforms();
                for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                add("genji-partial-charge-road-reset", partial && pilot.ModuleCharge == 0f &&
                    pilot.ModuleStage == 0 && pilot.ModuleTriggerCount == 0,
                    "partial=" + partial + " charge=" + pilot.ModuleCharge +
                    " trigger=" + pilot.ModuleTriggerCount);
            }
            finally { UnityEngine.Object.Destroy(partialRoot); }
            yield return null;
            for (int stage = 1; stage <= 3; stage++)
            {
                var root = NewModuleGround("Genji-" + stage, stage, true, out var ground);
                try
                {
                    var origin = new Vector3(20000f + stage * 500f, 1000f, 10000f);
                    var pilot = CreateMotor(reference, genji, root.transform, origin + Vector3.up * 2f);
                    Physics.SyncTransforms();
                    for (int i = 0; i < 120 && (!pilot.Grounded || !pilot.OnGrass); i++)
                        yield return new WaitForFixedUpdate();
                    pilot.driving = true;
                    for (int i = 0; i < 220 && pilot.ModuleStage < stage; i++)
                        yield return new WaitForFixedUpdate();
                    bool charged = pilot.Grounded && pilot.OnGrass && pilot.ModuleStage == stage;
                    ground.tag = "Untagged";
                    Physics.SyncTransforms();
                    for (int i = 0; i < 20 && pilot.ModuleTriggerCount == 0; i++)
                        yield return new WaitForFixedUpdate();
                    float expected = genji._moduleSpeedBoosts[stage - 1];
                    add("genji-grass-stage-" + stage + "-exit", charged && pilot.ModuleTriggerCount == 1
                        && pilot.ModuleStage == 0 && pilot.Speed >= expected - 3f,
                        "charged=" + charged + " trigger=" + pilot.ModuleTriggerCount +
                        " expectedBoost=" + expected + " actualSpeed=" + pilot.Speed);
                }
                finally { UnityEngine.Object.Destroy(root); }
                yield return null;
            }

            var shopping = Settings("ShoppingCart");
            add("shopping-source-module", shopping._moduleKind == NativeVehicleModuleKind.ShoppingCartDriftCap &&
                Mathf.Approximately(shopping._moduleLimit, 3f) && Mathf.Approximately(shopping._moduleHurtTime, 1.5f),
                "kind=" + shopping._moduleKind + " limit=" + shopping._moduleLimit +
                " hurt=" + shopping._moduleHurtTime);
            var shoppingRoot = NewModuleGround("ShoppingCart", 5, false, out _);
            try
            {
                var pilot = CreateMotor(reference, shopping, shoppingRoot.transform,
                    new Vector3(22500f, 1002f, 10000f));
                var hit = pilot.gameObject.AddComponent<NativeKartHit>();
                float observedDuration = 0f;
                hit.Damaged += duration => observedDuration = duration;
                Physics.SyncTransforms();
                for (int i = 0; i < 120 && !pilot.Grounded; i++) yield return new WaitForFixedUpdate();
                pilot.driving = true;
                pilot.throttle = 1f;
                pilot.steering = 1f;
                pilot.drifting = true;
                pilot.braking = true;
                for (int i = 0; i < 600 && pilot.ModuleTriggerCount == 0; i++) yield return new WaitForFixedUpdate();
                add("shopping-drift-cap-damage", pilot.ModuleTriggerCount == 1 && hit.State.AcceptedDamage == 1
                    && hit.Frozen && Mathf.Abs(observedDuration - 1.5f) < .001f,
                    "trigger=" + pilot.ModuleTriggerCount + " hit=" + hit.State.AcceptedDamage +
                    " duration=" + observedDuration + " drifting=" + pilot.IsDrifting);
            }
            finally { UnityEngine.Object.Destroy(shoppingRoot); }
            yield return null;
        }

        static GameObject NewModuleGround(string id, int index, bool grass, out GameObject ground)
        {
            var root = new GameObject("ModuleCoverage_" + id);
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.SetParent(root.transform);
            ground.transform.position = new Vector3(20000f + index * 500f, 999.5f, 10000f);
            ground.transform.localScale = new Vector3(1000f, 1f, 1000f);
            if (grass) ground.tag = "Grass";
            return root;
        }

        static void CaptureVehicleImage(Camera camera, Transform player, string path)
        {
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(960, 540, 24);
            var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                camera.transform.position = player.TransformPoint(3f, 2.1f, -3.5f);
                camera.transform.LookAt(player.position + Vector3.up * 1.2f);
                camera.fieldOfView = 40f;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView;
                RenderTexture.active = previousActive;
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(image);
            }
        }

        static NativeKartMotor CreateMotor(NativeKartMotor referenceMotor, NativeKartSettings settings,
            Transform parent, Vector3 position)
        {
            var go = new GameObject("coverage_motor");
            go.tag = "Player";
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(position, Quaternion.identity);
            var reference = referenceMotor.GetComponent<CharacterController>();
            var body = go.AddComponent<CharacterController>();
            body.center = reference.center;
            body.radius = reference.radius;
            body.height = reference.height;
            body.skinWidth = reference.skinWidth;
            body.slopeLimit = Mathf.Max(reference.slopeLimit, settings._slopeLimit);
            body.stepOffset = reference.stepOffset;
            body.minMoveDistance = reference.minMoveDistance;
            var motor = go.AddComponent<NativeKartMotor>();
            motor.stats = settings;
            motor.UseFixedVerificationClock = true;
            motor.ResetAt(position, Quaternion.identity, false);
            return motor;
        }
    }
}
