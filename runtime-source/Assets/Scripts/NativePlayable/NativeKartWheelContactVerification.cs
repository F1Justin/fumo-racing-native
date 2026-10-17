using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartWheelContactVerification
    {
        [Serializable]
        public sealed class Result
        {
            public string version, boundary, mappingMethod, signConvention, roadCollider;
            public bool passed, roadFound;
            public Vector3 roadPoint, roadNormal;
            public int vehicleCount, measurementCount, forcedMeasurements, liveStableMeasurements;
            public int landingMeasurements, airborneVehicles, landedVehicles;
            public VehicleResult[] vehicles;
            public Measurement[] measurements;
            public string[] errors;
        }

        [Serializable]
        public sealed class VehicleResult
        {
            public string vehicle;
            public bool meshMappingValid, stableGrounded, sourceSkinWidthStableGrounded, observedAirborne, observedLanding;
            public Vector3 originalKartLocalPosition, settledBodyPosition, sourceSkinWidthSettledBodyPosition, controllerCenter;
            public float controllerHeight, controllerRadius, controllerSkinWidth, controllerMinMoveDistance;
            public int stableGroundFrames;
            public RendererMapping[] renderers;
        }

        [Serializable]
        public sealed class RendererMapping
        {
            public string rendererPath, meshName;
            public bool originalMeshReadable;
            public int vertexCount, triangleIndexCount, lodIndex;
            public WheelBinding[] wheels;
        }

        [Serializable]
        public sealed class WheelBinding
        {
            public int wheelIndex, vertexCount;
            public string torquePath;
            public float minTranslationInfluence, maxTranslationInfluence, maxOffAxisError, restoredPositionError;
            public int[] vertexIndices;
        }

        [Serializable]
        public sealed class Measurement
        {
            public string vehicle, scenario, anchorMode, heightMode;
            public bool forcedVisualFixture, motorEnabled, motorGrounded, controllerGrounded;
            public int frame, fixedStep, landings, stableGroundFrames;
            public float elapsed, bodyPoseY, speed, turn, controllerSkinWidth, controllerMinMoveDistance;
            public Vector3 bodyPosition, visualPosition, kartLocalPosition;
            public Quaternion bodyRotation;
            public NativeKartWheelState visualState;
            public WheelMinimum[] wheels;
        }

        [Serializable]
        public sealed class WheelMinimum
        {
            public int wheelIndex, vertexCount, meshParts, lowestVertexIndex;
            public string torquePath, lowestRendererPath;
            public bool valid, roadHit;
            public Vector3 lowestWorldPoint, roadPoint, roadNormal, meshBoundsMin, meshBoundsMax;
            public float signedVerticalGap, signedNormalGap, suspensionRadiusEstimate;
            public PartMinimum[] parts;
        }

        [Serializable]
        public sealed class PartMinimum
        {
            public string rendererPath;
            public int vertexCount, lowestVertexIndex;
            public Vector3 lowestWorldPoint;
        }

        sealed class Binding
        {
            public SkinnedMeshRenderer renderer;
            public Mesh mesh;
            public RendererMapping mapping;
        }

        sealed class Saved
        {
            public NativeKartMotor motor;
            public NativeKartWheelVisual wheels;
            public Transform kart;
            public Vector3 position, kartLocalPosition;
            public Quaternion rotation;
            public bool enabled, driving, braking, drifting, jump, frozen;
            public float throttle, steering, skinWidth;
            public LODGroup[] lods;
        }

        struct Road
        {
            public Collider collider;
            public Vector3 point, normal;
            public Quaternion heading;
        }

        public static IEnumerator Run(NativeRace race, string directory, Result result,
            bool includeSourceHeightFixture = true)
        {
            Directory.CreateDirectory(directory);
            result.version = Application.version;
            result.boundary = "CPU-skinned LOD0 wheel geometry. Forced visual poses include the historical road+0.15 anchor and a real CharacterController-settled anchor. "
                + "Live stationary and jump/landing samples use the real controller. Optional kart local Y=0.035 and a separate skinWidth=0.015 settling observation are named fixture patches. "
                + "Pass means valid mesh coverage and collection; signed penetration and clearance remain measured outcomes. GPU alpha coverage is outside this geometry measurement.";
            result.mappingMethod = "BakeMesh(false), followed by renderer.transform.localToWorldMatrix. Indexed vertices with translation influence >0.001 under a temporary 0.02-world-unit torque-bone translation are assigned to that wheel; influence ranges and vertex indices are retained. Bones are restored before measurement. LOD0 is forced during this test.";
            result.signConvention = "Lowest indexed wheel vertex minus the road hit directly below it: positive means clearance, negative means penetration. Both world-vertical and road-normal projections are recorded.";
            var errors = new List<string>();
            var vehicles = new List<VehicleResult>();
            var samples = new List<Measurement>();
            var allBindings = new List<Binding>();
            var saved = new List<Saved>();
            float previousTimeScale = Time.timeScale;
            Road road = FindRoad(race);
            result.roadFound = road.collider;
            if (!road.collider)
            {
                errors.Add("No flat Forest road collider was found.");
                Finish();
                yield break;
            }
            result.roadCollider = road.collider.name;
            result.roadPoint = road.point;
            result.roadNormal = road.normal;
            foreach (var motor in race.racers)
            {
                var wheels = motor.GetComponent<NativeKartWheelVisual>();
                if (!wheels || !wheels.root) { errors.Add("Wheel visual binding missing: " + motor.name); continue; }
                Transform kart = wheels.root;
                while (kart.parent && kart.parent != motor.visual) kart = kart.parent;
                saved.Add(new Saved
                {
                    motor = motor, wheels = wheels, kart = kart,
                    position = motor.transform.position, rotation = motor.transform.rotation,
                    kartLocalPosition = kart.localPosition, enabled = motor.enabled,
                    driving = motor.driving, braking = motor.braking, drifting = motor.drifting,
                    jump = motor.jump, frozen = wheels.frozen, throttle = motor.throttle, steering = motor.steering,
                    skinWidth = motor.GetComponent<CharacterController>().skinWidth,
                    lods = kart.GetComponentsInChildren<LODGroup>(true)
                });
            }

            try
            {
                Time.timeScale = 0f;
                race.SetVisualCapture("race", false);
                foreach (Saved item in saved)
                {
                    item.motor.enabled = false;
                    item.wheels.frozen = true;
                    foreach (var lod in item.lods) lod.ForceLOD(0);
                }
                var tick = new WaitForFixedUpdate();
                var endFrame = new WaitForEndOfFrame();
                foreach (Saved item in saved)
                {
                    var motor = item.motor;
                    var wheelVisual = item.wheels;
                    var controller = motor.GetComponent<CharacterController>();
                    var vehicle = new VehicleResult
                    {
                        vehicle = motor.name, originalKartLocalPosition = item.kartLocalPosition,
                        controllerCenter = controller.center, controllerHeight = controller.height,
                        controllerRadius = controller.radius, controllerSkinWidth = controller.skinWidth,
                        controllerMinMoveDistance = controller.minMoveDistance
                    };
                    vehicles.Add(vehicle);
                    Reset(item, road.point + Vector3.up * 0.6f, road.heading, true);
                    Time.timeScale = 1f;
                    int consecutiveGrounded = 0;
                    for (int step = 0; step < 120; step++)
                    {
                        yield return tick;
                        motor.PrepareRenderPose(false);
                        yield return endFrame;
                        consecutiveGrounded = motor.Grounded && controller.isGrounded ? consecutiveGrounded + 1 : 0;
                    }
                    vehicle.stableGroundFrames = consecutiveGrounded;
                    vehicle.stableGrounded = consecutiveGrounded >= 30 && Mathf.Abs(motor.Speed) < 0.001f;
                    vehicle.settledBodyPosition = motor.transform.position;
                    if (!vehicle.stableGrounded) errors.Add("Stable controller contact missing: " + motor.name);
                    Time.timeScale = 0f;
                    motor.enabled = false;
                    wheelVisual.frozen = true;

                    var bindings = CreateBindings(item, errors);
                    allBindings.AddRange(bindings);
                    var maps = new List<RendererMapping>();
                    foreach (var binding in bindings) maps.Add(binding.mapping);
                    vehicle.renderers = maps.ToArray();
                    vehicle.meshMappingValid = ValidateBindings(bindings, wheelVisual.torques.Length);
                    if (!vehicle.meshMappingValid)
                    {
                        errors.Add("Complete torque-linked wheel vertex mapping failed: " + motor.name);
                        continue;
                    }

                    // This snapshot retains the settled live visual state; no synthetic spring input is applied.
                    Record(item, bindings, "stable-grounded", "controller-settled", "as-built", false,
                        0, 0f, vehicle.stableGroundFrames);
                    for (int anchorIndex = 0; anchorIndex < 2; anchorIndex++)
                    {
                        Vector3 anchor = anchorIndex == 0 ? road.point + Vector3.up * 0.15f : vehicle.settledBodyPosition;
                        string anchorMode = anchorIndex == 0 ? "historical-road-plus-0.15" : "controller-settled";
                        int heightCases = includeSourceHeightFixture ? 2 : 1;
                        for (int heightIndex = 0; heightIndex < heightCases; heightIndex++)
                        {
                            item.kart.localPosition = item.kartLocalPosition;
                            if (heightIndex == 1)
                                item.kart.localPosition = new Vector3(item.kartLocalPosition.x, 0.035f, item.kartLocalPosition.z);
                            string heightMode = heightIndex == 0 ? "as-built" : "fixture-kart-local-y-0.035";
                            foreach (float steer in new[] { 0f, -1f, 1f })
                            {
                                Reset(item, anchor, road.heading, false);
                                var input = new NativeKartWheelInput
                                {
                                    speed = motor.stats._topSpeed * 0.6f,
                                    topSpeed = motor.stats._topSpeed, steering = steer, grounded = true
                                };
                                for (int step = 0; step < 120; step++) wheelVisual.StepForTest(input, 1f / 60f);
                                motor.PrepareRenderPose(false);
                                Physics.SyncTransforms();
                                Record(item, bindings, steer == 0f ? "forced-straight" : steer < 0f ? "forced-left" : "forced-right",
                                    anchorMode, heightMode, true, 120, 2f, 0);
                            }
                        }
                    }
                    item.kart.localPosition = item.kartLocalPosition;

                    controller.skinWidth = 0.015f;
                    Reset(item, road.point + Vector3.up * 0.6f, road.heading, true);
                    Time.timeScale = 1f;
                    consecutiveGrounded = 0;
                    for (int step = 0; step < 90; step++)
                    {
                        yield return tick;
                        motor.PrepareRenderPose(false);
                        yield return endFrame;
                        consecutiveGrounded = motor.Grounded && controller.isGrounded ? consecutiveGrounded + 1 : 0;
                    }
                    vehicle.sourceSkinWidthStableGrounded = consecutiveGrounded >= 30;
                    vehicle.sourceSkinWidthSettledBodyPosition = motor.transform.position;
                    Record(item, bindings, "source-skinwidth-stable-grounded", "fixture-controller-skinwidth-0.015",
                        "as-built", false, 90, 1.5f, consecutiveGrounded);
                    if (!vehicle.sourceSkinWidthStableGrounded) errors.Add("Source skinWidth fixture failed to settle: " + motor.name);
                    controller.skinWidth = item.skinWidth;

                    Reset(item, vehicle.settledBodyPosition, road.heading, true);
                    Time.timeScale = 1f;
                    for (int step = 0; step < 90; step++)
                    {
                        yield return tick;
                        motor.PrepareRenderPose(false);
                        yield return endFrame;
                    }
                    int landingsBefore = motor.Landings;
                    float started = Time.time;
                    motor.jump = true;
                    consecutiveGrounded = 0;
                    for (int step = 0; step < 100; step++)
                    {
                        yield return tick;
                        motor.PrepareRenderPose(false);
                        yield return endFrame;
                        vehicle.observedAirborne |= !motor.Grounded;
                        vehicle.observedLanding |= vehicle.observedAirborne && motor.Landings > landingsBefore;
                        consecutiveGrounded = motor.Grounded && controller.isGrounded ? consecutiveGrounded + 1 : 0;
                        Record(item, bindings,
                            !vehicle.observedAirborne ? "jump-start" : !motor.Grounded ? "jump-airborne" : "landing-transient",
                            "live-character-controller", "as-built", false, step, Time.time - started, consecutiveGrounded);
                    }
                    if (!vehicle.observedAirborne || !vehicle.observedLanding)
                        errors.Add("Actual jump/landing transition missing: " + motor.name);
                    Time.timeScale = 0f;
                    motor.enabled = false;
                    wheelVisual.frozen = true;
                }
            }
            finally
            {
                Time.timeScale = 0f;
                foreach (Saved item in saved)
                {
                    item.kart.localPosition = item.kartLocalPosition;
                    item.motor.GetComponent<CharacterController>().skinWidth = item.skinWidth;
                    item.motor.ResetAt(item.position, item.rotation, false);
                    item.wheels.ResetState();
                    item.motor.enabled = item.enabled;
                    item.motor.driving = item.driving;
                    item.motor.throttle = item.throttle;
                    item.motor.steering = item.steering;
                    item.motor.braking = item.braking;
                    item.motor.drifting = item.drifting;
                    item.motor.jump = item.jump;
                    item.wheels.frozen = item.frozen;
                    foreach (var lod in item.lods) lod.ForceLOD(-1);
                }
                foreach (Binding binding in allBindings) UnityEngine.Object.Destroy(binding.mesh);
                Time.timeScale = previousTimeScale;
                Finish();
            }

            void Record(Saved item, List<Binding> bindings, string scenario, string anchorMode,
                string heightMode, bool forced, int frame, float elapsed, int stableFrames)
            {
                try
                {
                    Measurement measurement = Measure(item, bindings, road, scenario, anchorMode, heightMode,
                        forced, frame, elapsed, stableFrames);
                    samples.Add(measurement);
                    foreach (WheelMinimum wheel in measurement.wheels)
                        if (!wheel.valid) errors.Add(item.motor.name + ": incomplete mesh/road sample " + scenario + " wheel " + wheel.wheelIndex);
                }
                catch (Exception exception)
                {
                    errors.Add(item.motor.name + ": " + scenario + ": " + exception.Message);
                }
            }

            void Finish()
            {
                result.vehicles = vehicles.ToArray();
                result.measurements = samples.ToArray();
                result.errors = errors.ToArray();
                result.vehicleCount = vehicles.Count;
                result.measurementCount = samples.Count;
                result.forcedMeasurements = result.liveStableMeasurements = result.landingMeasurements = 0;
                result.airborneVehicles = result.landedVehicles = 0;
                foreach (var sample in samples)
                {
                    if (sample.forcedVisualFixture) result.forcedMeasurements++;
                    if (sample.scenario == "stable-grounded") result.liveStableMeasurements++;
                    if (sample.scenario == "landing-transient") result.landingMeasurements++;
                }
                foreach (var vehicle in vehicles)
                {
                    if (vehicle.observedAirborne) result.airborneVehicles++;
                    if (vehicle.observedLanding) result.landedVehicles++;
                }
                result.passed = errors.Count == 0 && vehicles.Count == race.racers.Length
                    && result.liveStableMeasurements == vehicles.Count && result.landedVehicles == vehicles.Count
                    && result.forcedMeasurements == vehicles.Count * (includeSourceHeightFixture ? 12 : 6);
                File.WriteAllText(Path.Combine(directory, "wheel-contact-report.json"), JsonUtility.ToJson(result, true));
            }
        }

        static void Reset(Saved item, Vector3 position, Quaternion rotation, bool live)
        {
            item.motor.enabled = false;
            item.wheels.frozen = true;
            item.motor.ResetAt(position, rotation, false);
            item.wheels.ResetState();
            item.motor.driving = true;
            item.motor.throttle = item.motor.steering = 0f;
            item.motor.braking = item.motor.drifting = item.motor.jump = false;
            item.motor.enabled = live;
            item.wheels.frozen = !live;
            Physics.SyncTransforms();
        }

        static List<Binding> CreateBindings(Saved item, List<string> errors)
        {
            var result = new List<Binding>();
            foreach (var renderer in item.kart.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!renderer.sharedMesh || !renderer.gameObject.activeInHierarchy) continue;
                int lodIndex = FindLod(renderer, item.lods);
                if (lodIndex > 0) continue;
                if (lodIndex < 0 && !renderer.enabled) continue;
                bool hasTorque = false;
                foreach (Transform bone in renderer.bones)
                    foreach (Transform torque in item.wheels.torques)
                        hasTorque |= bone && torque && (bone == torque || bone.IsChildOf(torque));
                if (!hasTorque) continue;
                var mesh = new Mesh { name = "Wheel contact CPU snapshot" };
                try
                {
                    renderer.BakeMesh(mesh, false);
                    Vector3[] baseline = WorldVertices(renderer, mesh);
                    int[] triangles = mesh.triangles;
                    var indexed = new HashSet<int>(triangles);
                    var mapping = new RendererMapping
                    {
                        rendererPath = PathOf(renderer.transform, item.motor.transform),
                        meshName = renderer.sharedMesh.name, originalMeshReadable = renderer.sharedMesh.isReadable,
                        vertexCount = baseline.Length, triangleIndexCount = triangles.Length, lodIndex = lodIndex,
                        wheels = new WheelBinding[item.wheels.torques.Length]
                    };
                    bool contributes = false;
                    Vector3 probe = new Vector3(1f, 2f, 3f).normalized * 0.02f;
                    for (int wheel = 0; wheel < item.wheels.torques.Length; wheel++)
                    {
                        Transform torque = item.wheels.torques[wheel];
                        Vector3 rest = torque.localPosition;
                        Vector3[] shifted;
                        try
                        {
                            torque.position += probe;
                            renderer.BakeMesh(mesh, false);
                            shifted = WorldVertices(renderer, mesh);
                        }
                        finally { torque.localPosition = rest; }
                        renderer.BakeMesh(mesh, false);
                        Vector3[] restored = WorldVertices(renderer, mesh);
                        if (shifted.Length != baseline.Length || restored.Length != baseline.Length)
                            throw new InvalidOperationException("BakeMesh changed vertex indexing.");
                        var indices = new List<int>();
                        float minWeight = 1f, maxWeight = 0f, offAxis = 0f, restoreError = 0f;
                        foreach (int vertex in indexed)
                        {
                            Vector3 difference = shifted[vertex] - baseline[vertex];
                            float influence = Vector3.Dot(difference, probe) / probe.sqrMagnitude;
                            restoreError = Mathf.Max(restoreError, Vector3.Distance(restored[vertex], baseline[vertex]));
                            if (influence <= 0.001f) continue;
                            indices.Add(vertex);
                            minWeight = Mathf.Min(minWeight, influence);
                            maxWeight = Mathf.Max(maxWeight, influence);
                            offAxis = Mathf.Max(offAxis, (difference - probe * influence).magnitude);
                        }
                        indices.Sort();
                        mapping.wheels[wheel] = new WheelBinding
                        {
                            wheelIndex = wheel, torquePath = PathOf(torque, item.motor.transform),
                            vertexCount = indices.Count, vertexIndices = indices.ToArray(),
                            minTranslationInfluence = indices.Count > 0 ? minWeight : 0f,
                            maxTranslationInfluence = maxWeight, maxOffAxisError = offAxis,
                            restoredPositionError = restoreError
                        };
                        contributes |= indices.Count > 0;
                    }
                    if (contributes) result.Add(new Binding { renderer = renderer, mesh = mesh, mapping = mapping });
                    else UnityEngine.Object.Destroy(mesh);
                }
                catch (Exception exception)
                {
                    UnityEngine.Object.Destroy(mesh);
                    errors.Add(item.motor.name + ": mesh binding " + renderer.name + ": " + exception.Message);
                }
            }
            return result;
        }

        static bool ValidateBindings(List<Binding> bindings, int wheels)
        {
            if (wheels != 4) return false;
            for (int wheel = 0; wheel < wheels; wheel++)
            {
                int vertices = 0;
                float maxInfluence = 0f;
                foreach (Binding binding in bindings)
                {
                    WheelBinding mapping = binding.mapping.wheels[wheel];
                    vertices += mapping.vertexCount;
                    maxInfluence = Mathf.Max(maxInfluence, mapping.maxTranslationInfluence);
                    if (mapping.restoredPositionError > 0.0001f || mapping.maxOffAxisError > 0.0001f) return false;
                }
                if (vertices < 3 || maxInfluence < 0.99f || maxInfluence > 1.01f) return false;
            }
            return true;
        }

        static Measurement Measure(Saved item, List<Binding> bindings, Road road, string scenario,
            string anchorMode, string heightMode, bool forced, int frame, float elapsed, int stableFrames)
        {
            var motor = item.motor;
            var result = new Measurement
            {
                vehicle = motor.name, scenario = scenario, anchorMode = anchorMode, heightMode = heightMode,
                forcedVisualFixture = forced, motorEnabled = motor.enabled, motorGrounded = motor.Grounded,
                controllerGrounded = motor.GetComponent<CharacterController>().isGrounded,
                frame = frame, fixedStep = motor.FixedStepCount, landings = motor.Landings,
                stableGroundFrames = stableFrames, elapsed = elapsed, bodyPoseY = motor.transform.position.y,
                speed = motor.Speed, turn = motor.CurrentTurn, bodyPosition = motor.transform.position,
                controllerSkinWidth = motor.GetComponent<CharacterController>().skinWidth,
                controllerMinMoveDistance = motor.GetComponent<CharacterController>().minMoveDistance,
                bodyRotation = motor.transform.rotation, visualPosition = motor.visual.position,
                kartLocalPosition = item.kart.localPosition, visualState = item.wheels.State,
                wheels = new WheelMinimum[item.wheels.torques.Length]
            };
            var vertices = new List<Vector3[]>();
            foreach (Binding binding in bindings)
            {
                binding.renderer.BakeMesh(binding.mesh, false);
                vertices.Add(WorldVertices(binding.renderer, binding.mesh));
            }
            for (int wheel = 0; wheel < result.wheels.Length; wheel++)
            {
                var minimum = new WheelMinimum
                {
                    wheelIndex = wheel, torquePath = PathOf(item.wheels.torques[wheel], motor.transform),
                    lowestVertexIndex = -1
                };
                var parts = new List<PartMinimum>();
                bool first = true;
                for (int part = 0; part < bindings.Count; part++)
                {
                    Binding binding = bindings[part];
                    int[] indices = binding.mapping.wheels[wheel].vertexIndices;
                    if (indices.Length == 0) continue;
                    var partMinimum = new PartMinimum
                    {
                        rendererPath = binding.mapping.rendererPath, vertexCount = indices.Length,
                        lowestVertexIndex = indices[0], lowestWorldPoint = vertices[part][indices[0]]
                    };
                    foreach (int vertex in indices)
                    {
                        Vector3 world = vertices[part][vertex];
                        if (first)
                        {
                            minimum.meshBoundsMin = minimum.meshBoundsMax = world;
                            minimum.lowestWorldPoint = world;
                            minimum.lowestRendererPath = binding.mapping.rendererPath;
                            minimum.lowestVertexIndex = vertex;
                            first = false;
                        }
                        minimum.meshBoundsMin = Vector3.Min(minimum.meshBoundsMin, world);
                        minimum.meshBoundsMax = Vector3.Max(minimum.meshBoundsMax, world);
                        if (world.y < partMinimum.lowestWorldPoint.y)
                        { partMinimum.lowestWorldPoint = world; partMinimum.lowestVertexIndex = vertex; }
                        if (world.y < minimum.lowestWorldPoint.y)
                        {
                            minimum.lowestWorldPoint = world;
                            minimum.lowestRendererPath = binding.mapping.rendererPath;
                            minimum.lowestVertexIndex = vertex;
                        }
                    }
                    parts.Add(partMinimum);
                    minimum.vertexCount += indices.Length;
                }
                minimum.parts = parts.ToArray();
                minimum.meshParts = parts.Count;
                if (!first && road.collider.Raycast(new Ray(minimum.lowestWorldPoint + Vector3.up * 4f, Vector3.down), out RaycastHit hit, 10f))
                {
                    minimum.roadHit = true;
                    minimum.roadPoint = hit.point;
                    minimum.roadNormal = hit.normal;
                    minimum.signedVerticalGap = minimum.lowestWorldPoint.y - hit.point.y;
                    minimum.signedNormalGap = Vector3.Dot(minimum.lowestWorldPoint - hit.point, hit.normal);
                    minimum.valid = true;
                }
                Vector3 node = item.wheels.suspensions[wheel].position;
                if (road.collider.Raycast(new Ray(node + Vector3.up * 4f, Vector3.down), out RaycastHit nodeHit, 10f))
                    minimum.suspensionRadiusEstimate = node.y - nodeHit.point.y - item.wheels.settings._wheels[wheel]._wheelRadius;
                result.wheels[wheel] = minimum;
            }
            return result;
        }

        static Vector3[] WorldVertices(SkinnedMeshRenderer renderer, Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
            for (int index = 0; index < vertices.Length; index++) vertices[index] = matrix.MultiplyPoint3x4(vertices[index]);
            return vertices;
        }

        static int FindLod(Renderer renderer, LODGroup[] groups)
        {
            foreach (var group in groups)
            {
                LOD[] lods = group.GetLODs();
                for (int index = 0; index < lods.Length; index++)
                    foreach (var candidate in lods[index].renderers)
                        if (candidate == renderer) return index;
            }
            return -1;
        }

        static string PathOf(Transform value, Transform root)
        {
            string path = value.name;
            while (value.parent && value.parent != root)
            { value = value.parent; path = value.name + "/" + path; }
            return path;
        }

        static Road FindRoad(NativeRace race)
        {
            for (int index = 0; index < race.route.Length - 1; index++)
            {
                Vector3 forward = Vector3.ProjectOnPlane(race.route[index + 1] - race.route[index], Vector3.up);
                if (forward.sqrMagnitude < 0.01f) continue;
                RaycastHit[] hits = Physics.RaycastAll(race.route[index] + Vector3.up * 20f, Vector3.down, 50f);
                Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
                foreach (RaycastHit hit in hits)
                {
                    if (!hit.collider || hit.collider.isTrigger || hit.collider.GetComponentInParent<NativeKartMotor>()) continue;
                    if (hit.normal.y < 0.999f || hit.collider.CompareTag("Grass") || hit.collider.CompareTag("Water")) break;
                    return new Road { collider = hit.collider, point = hit.point, normal = hit.normal,
                        heading = Quaternion.LookRotation(forward.normalized) };
                }
            }
            return default;
        }
    }
}
