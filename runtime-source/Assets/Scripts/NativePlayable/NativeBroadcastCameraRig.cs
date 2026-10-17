using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace Fumo.NativePlayable
{
    // Camera states are prepared independently; Program only takes the selected pose.
    public sealed class NativeBroadcastCameraRig
    {
        sealed class Pose
        {
            public NativeBroadcastShotCandidate candidate;
            public NativeBroadcastCameraMotion motion;
            public Vector3 aim;
            public float targetHeight;
            public bool fixedAim;
            public Camera authoredState;
        }
        readonly NativeBroadcastShotPlanner planner = new NativeBroadcastShotPlanner();
        readonly List<Pose> poses = new List<Pose>();
        readonly Dictionary<string, Camera> authoredStates = new Dictionary<string, Camera>();
        readonly Dictionary<string, NativeBroadcastCameraMotion> states = new Dictionary<string, NativeBroadcastCameraMotion>();
        readonly Dictionary<NativeKartMotor, Renderer[]> renderers = new Dictionary<NativeKartMotor, Renderer[]>();
        readonly Dictionary<NativeKartMotor, Bounds> bounds = new Dictionary<NativeKartMotor, Bounds>();
        Camera camera, initialLens;
        PostProcessLayer postProcess;
        readonly Dictionary<string, float> readySince = new Dictionary<string, float>();
        float emergencyUntil;
        NativeBroadcastCameraProfile profile;
        float originalFov, originalNear, nextDecision, previousTime = -1, lastCutAt;
        string appliedKey;
        int appliedFocus = -1, appliedOther = -1, routeSegment = -1;
        NativeBroadcastShotCandidate selected;
        NativeBroadcastShotKind? selectedManual;
        string telemetryStepState;
        public NativeBroadcastShotKind? ManualKind { get; set; }
        public string PreferredCameraId { get; set; }
        public bool LastStepCut { get; private set; }
        public int HistoryResetCount { get; private set; }
        public bool Active => camera && profile;
        public bool Fallback { get; private set; }
        public NativeBroadcastShotCandidate Current => selected;
        public int CutCount { get; private set; }
        public string Profile => profile ? profile.track : null;
        public NativeBroadcastShotCandidate[] Candidates => poses.Select(p => p.candidate).ToArray();
        public string CutReason { get; private set; }

        public void Begin(Camera view, string track)
        {
            if (Active && camera == view && profile.track == track) return;
            End();
            string operation = NativeTelemetry.Begin("camera.broadcast.begin.requested", track);
            try
            {
                profile = NativeContent.Load<NativeBroadcastCameraProfile>("NativeOnline/Broadcast/" + track);
                if (!profile || profile.track != track) throw new InvalidOperationException("Missing broadcast camera profile: " + track);
                camera = view; initialLens=new GameObject("Director initial lens"){hideFlags=HideFlags.HideAndDontSave}.AddComponent<Camera>();initialLens.enabled=false;NativeBroadcastManualCamera.CopyLens(view,initialLens); originalFov = view.fieldOfView; originalNear = view.nearClipPlane;
                postProcess = view.GetComponent<PostProcessLayer>(); HistoryResetCount = 0; CutCount = 0;
                readySince.Clear(); emergencyUntil = 0;
                planner.Reset(); previousTime = -1; nextDecision = 0;
                NativeTelemetry.End(operation, "camera.broadcast.begin.result", "completed", "profileBound", track);
            }
            catch (Exception error)
            {
                NativeTelemetry.End(operation, "camera.broadcast.begin.result", "failed", !profile || profile.track != track ? "requiredProfileMissing" : "initializationException", error.GetType().Name);
                throw;
            }
        }
        public void End()
        {
            bool changed = camera || profile;
            if (camera) { ResetHistory(); if(initialLens)NativeBroadcastManualCamera.CopyLens(initialLens,camera); }
            if(initialLens)UnityEngine.Object.Destroy(initialLens.gameObject);initialLens=null;
            foreach (var c in authoredStates.Values) if (c) UnityEngine.Object.Destroy(c.gameObject); authoredStates.Clear();
            camera = null; postProcess = null; profile = null; planner.Reset(); poses.Clear(); states.Clear(); renderers.Clear(); bounds.Clear();
            appliedKey = null; appliedFocus = appliedOther = -1; selected = null; selectedManual = null;
            Fallback = LastStepCut = false;
            telemetryStepState = null;
            if (changed) NativeTelemetry.Event("camera.broadcast.ended", "completed");
        }
        public bool Step(NativeKartMotor[] racers, Vector3[] route, NativeBroadcastStory story, int focus, float now, float dt)
        {
            LastStepCut = false;
            if (camera && NativeBroadcastOutput.For(camera).Owner != "Automatic") { ObserveStepState("externalOwner", "ignored"); return true; }
            if (!Active || racers == null || focus < 0 || focus >= racers.Length || !racers[focus])
            { ObserveStepState(!Active ? "inactive" : "targetUnavailable", !Active ? "ignored" : "failed"); return false; }
            ObserveStepState("ready", "applied");
            if (now < previousTime) { states.Clear(); planner.Reset(); selected = null; nextDecision = 0; NativeTelemetry.Event("camera.broadcast.timeline.reset", "applied", "timeMovedBackward"); }
            previousTime = now;
            if (appliedFocus != focus) { states.Clear(); routeSegment = -1; }
            bounds.Clear(); foreach (var motor in racers) if (motor) bounds[motor] = ReadBounds(motor);
            BuildPoses(racers, route, story, focus, dt);
            bool decision = !string.IsNullOrEmpty(PreferredCameraId) || now >= nextDecision || selected == null || selected.focus != focus || selectedManual != ManualKind;
            var chosen = selected == null ? null : poses.FirstOrDefault(p => Same(p.candidate, selected));
            if (chosen != null) Evaluate(chosen, racers, story, false, now);
            if (chosen == null || !chosen.candidate.visible)
            {
                if (selected != null) emergencyUntil = now + 2;
                decision = true;
            }
            if (decision)
            {
                foreach (var pose in poses) Evaluate(pose, racers, story, true, now);
                if (!string.IsNullOrEmpty(PreferredCameraId))
                    selected = poses.Where(p=>p.candidate.key==PreferredCameraId && p.candidate.visible).Select(p=>p.candidate).FirstOrDefault();
                else if (ManualKind.HasValue && selectedManual == ManualKind && chosen != null && chosen.candidate.visible)
                    selected = chosen.candidate;
                else if (ManualKind.HasValue)
                    selected = poses.Where(p => p.candidate.kind == ManualKind && p.candidate.visible)
                        .OrderByDescending(p => p.candidate.score).Select(p => p.candidate).FirstOrDefault();
                else
                {
                    if (selectedManual.HasValue) planner.Reset();
                    foreach (var p in poses)
                    {
                        string id = StateKey(p.candidate.key, p.candidate.focus, p.candidate.other);
                        if (!p.candidate.visible || p.candidate.usableSeconds < .8f) readySince.Remove(id);
                        else if (!readySince.ContainsKey(id)) readySince[id] = now;
                    }
                    var available = poses.Select(p => p.candidate).Where(c => c.kind == NativeBroadcastShotKind.Follow
                        || (selected != null && Same(c, selected) && c.visible)
                        || (now >= emergencyUntil && readySince.TryGetValue(StateKey(c.key, c.focus, c.other), out float since) && now - since >= .5f)).ToArray();
                    selected = planner.Update(available, focus, now);
                }
                selectedManual = ManualKind; nextDecision = now + .1f;
                chosen = selected == null ? null : poses.FirstOrDefault(p => Same(p.candidate, selected));
            }
            else if (chosen != null) selected = chosen.candidate;
            bool wasFallback = Fallback; Fallback = chosen == null;
            if (Fallback)
            {
                LastStepCut = !wasFallback || appliedFocus != focus;
                if (LastStepCut)
                {
                    ResetHistory(); CutReason = string.IsNullOrEmpty(PreferredCameraId)?"no visible camera; original follow fallback":"requested camera outside coverage or clipped / occluded: "+PreferredCameraId;
                    NativeTelemetry.Event("camera.broadcast.fallback", "applied", string.IsNullOrEmpty(PreferredCameraId) ? "noVisibleCamera" : "preferredCameraUnavailable", "focus=" + focus + " camera=" + PreferredCameraId);
                }
                NativeBroadcastOutput.For(camera).Fallback(CutReason);
                camera.usePhysicalProperties = false; camera.fieldOfView = originalFov; camera.nearClipPlane = originalNear;
                appliedKey = null; appliedFocus = focus; appliedOther = -1; return false;
            }
            LastStepCut = appliedKey != selected.key || appliedFocus != focus || appliedOther != selected.other || wasFallback;
            // No blend between cameras. Motion belongs to the incoming, already updated camera state.
            if (chosen.authoredState) NativeBroadcastOutput.For(camera).CommitAutomatic(selected.key, chosen.authoredState);
            else NativeBroadcastOutput.For(camera).CommitAutomatic(selected.key, chosen.motion.position, chosen.motion.rotation, chosen.motion.fieldOfView);
            if (LastStepCut)
            {
                CutReason = ManualKind.HasValue ? "manual camera" : appliedFocus != focus ? "story subject" : wasFallback ? "coverage recovered" : "quality / coverage";
                if (appliedKey != null) CutCount++;
                ResetHistory(); lastCutAt = now;
                NativeTelemetry.Event("camera.broadcast.cut", "applied", ManualKind.HasValue ? "manualCamera" : appliedFocus != focus ? "storySubject" : wasFallback ? "coverageRecovered" : "qualityCoverage",
                    "camera=" + selected.key + " focus=" + focus + " other=" + selected.other);
            }
            appliedKey = selected.key; appliedFocus = focus; appliedOther = selected.other;
            return true;
        }
        static bool Same(NativeBroadcastShotCandidate a, NativeBroadcastShotCandidate b) => a.key == b.key && a.focus == b.focus && a.other == b.other;
        void ObserveStepState(string state, string outcome)
        {
            if (telemetryStepState == state) return;
            telemetryStepState = state;
            NativeTelemetry.Event("camera.broadcast.readiness.changed", outcome, state);
        }
        void ResetHistory()
        {
            if (!postProcess || !postProcess.enabled || !postProcess.haveBundlesBeenInited) return;
            postProcess.ResetHistory(); HistoryResetCount++;
            NativeTelemetry.Count("camera.history.reset");
        }
        void BuildPoses(NativeKartMotor[] racers, Vector3[] route, NativeBroadcastStory story, int focus, float dt)
        {
            poses.Clear(); var motor = racers[focus]; Vector3 center = Center(motor), heading = Heading(motor);
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            int other = story != null && story.focus == focus ? story.other : -1;
            if (other < 0 || other >= racers.Length || !racers[other] || Vector3.Distance(center, Center(racers[other])) > 45) other = -1;
            Vector3 group = other >= 0 ? (center + Center(racers[other])) * .5f : center;
            foreach (int side in new[] { -1, 1 })
                Add("follow-" + side, NativeBroadcastShotKind.Follow, focus, -1, center - heading * profile.followDistance + right * side * 3 + Vector3.up * profile.followHeight,
                    center + heading * 2, 55, .23f, dt);
            if (other >= 0 && Mathf.Abs(center.y - Center(racers[other]).y) <= 8)
            {
                float span = Vector3.Distance(center, Center(racers[other]));
                foreach (int side in new[] { -1, 1 })
                    Add("pair-" + side, NativeBroadcastShotKind.Pair, focus, other,
                        group - heading * Mathf.Max(profile.pairDistance, span * 1.25f) + right * side * (4 + span * .3f) + Vector3.up * (profile.pairHeight + span * .15f),
                        group, 60, .2f, dt);
            }
            foreach (var definition in NativeBroadcastCameraRegistry.Definitions)
            {
                if (!definition.autoEligible || Vector3.Distance(center, definition.transform.position) > definition.coverageRadius) continue;
                if (!authoredStates.TryGetValue(definition.cameraId, out var instance) || !instance)
                    authoredStates[definition.cameraId] = instance = NativeBroadcastCameraRegistry.CreateInstance(definition, "director");
                int[] bindings=(definition.targetIds??Array.Empty<string>()).Select(id=>NativeBroadcastCameraRegistry.BindingSeat(id,racers.Length)).Where(seat=>seat>=0&&racers[seat]).Distinct().Take(4).ToArray();
                int first=bindings.Length>0?bindings[0]:focus, second=bindings.Length>1?bindings[1]:other;
                if(bindings.Length>0 && !bindings.Contains(focus))continue;
                definition.EvaluateGroupInto(instance,bindings.Length>0?bindings.Select(seat=>racers[seat].transform).ToArray():second>=0?new[]{racers[first].transform,racers[second].transform}:new[]{racers[first].transform},Time.unscaledTime);
                var lab=NativeCameraLab.Active;if(lab&&lab.AutomaticCameraId==definition.cameraId){instance.usePhysicalProperties=true;instance.focalLength=lab.focalLength;lab.ApplyAutomaticFraming(instance,dt);}
                Add(definition.cameraId, definition.kind == NativeBroadcastAuthoredCameraKind.FixedUltraWide ? NativeBroadcastShotKind.FixedWide : NativeBroadcastShotKind.Trackside,
                    focus, bindings.Length>1?second:bindings.Length==1?-1:other, instance.transform.position, instance.transform.position + instance.transform.forward * 30, instance.fieldOfView, .25f, dt, false, false, 100, 0, true);
                if (poses.Count > 0) { var p = poses[poses.Count - 1]; p.motion.position = instance.transform.position; p.motion.rotation = instance.transform.rotation; p.authoredState = instance; }
            }
            if (route == null || route.Length < 2) return;
            RouteFrame(route, motor.transform.position, out Vector3 track, out Vector3 tangent);
            Vector3 sideTrack = Vector3.Cross(Vector3.up, tangent);
            Add("aerial-tele", NativeBroadcastShotKind.AerialTele, focus, other, track + tangent * profile.aerialTeleLead + sideTrack * profile.aerialTeleSide + Vector3.up * profile.aerialTeleHeight,
                group + tangent * 3, Zoom(track + tangent * profile.aerialTeleLead + sideTrack * profile.aerialTeleSide + Vector3.up * profile.aerialTeleHeight, group, focus, other, racers, .25f, 6, 45), .25f, dt, true, true, 75);
            Add("aerial-wide", NativeBroadcastShotKind.AerialWide, focus, other, track - tangent * profile.aerialWideBehind + sideTrack * profile.aerialWideSide + Vector3.up * profile.aerialWideHeight,
                group + tangent * 12, 55, .09f, dt, true, true, 60);
            string flightKey = StateKey("fpv", focus, other);
            float age = states.TryGetValue(flightKey, out var flight) ? flight.age : 0;
            float phase = Mathf.Sin(age * .3f), bank = Mathf.Clamp(Vector3.Dot(Vector3.Cross(heading, tangent), Vector3.up) * 12, -12, 12);
            Add("fpv", NativeBroadcastShotKind.Fpv, focus, other, track - tangent * (10 + 6 * phase) + sideTrack * (profile.fpvSide + 2 * phase) + Vector3.up * (profile.fpvHeight + phase),
                group + tangent * 3, 72, .23f, dt, true, true, 115, bank);
            Add("mounted", NativeBroadcastShotKind.Mounted, focus, -1, motor.transform.TransformPoint(new Vector3(-1.8f, 2.8f, -3.5f)),
                motor.transform.position + motor.transform.forward * 18 + Vector3.up * 1.5f, 85, .4f, dt);
            foreach (var station in profile.stations)
            {
                if (station == null || (station.finishOnly && story?.kind != NativeBroadcastStoryKind.Finish)) continue;
                int index = Mathf.Min(route.Length - 1, Mathf.FloorToInt(Mathf.Clamp01(station.routeFraction) * route.Length));
                Vector3 anchor = route[index]; if (Vector3.Distance(center, anchor) > station.coverageRadius) continue;
                Vector3 direction = route[(index + 1) % route.Length] - anchor; direction.y = 0;
                if (direction.sqrMagnitude < .001f) continue;
                Vector3 position = station.useWorldPose ? station.worldPosition
                    : anchor + Vector3.Cross(Vector3.up, direction.normalized) * station.sideOffset + Vector3.up * station.height;
                bool fixedAim = station.kind == NativeBroadcastShotKind.FixedWide;
                int aimIndex = (index + station.aimSampleOffset + route.Length) % route.Length;
                Vector3 aim = fixedAim ? station.useWorldPose ? position + Quaternion.Euler(station.worldEuler) * Vector3.forward * 30 : route[aimIndex] + Vector3.up : group;
                float fov = fixedAim ? station.fieldOfView : Zoom(position, group, focus, other, racers, station.screenHeight, station.minimumFov, station.maximumFov);
                Add("station-" + station.id, station.kind, focus, other, position, aim, fov,
                    fixedAim ? .12f : station.screenHeight, dt, false, !fixedAim, 100, 0, fixedAim);
            }
        }
        static string StateKey(string key, int focus, int other) => key + ":" + focus + ":" + other;
        void Add(string key, NativeBroadcastShotKind kind, int focus, int other, Vector3 position, Vector3 aim, float fov, float targetHeight,
            float dt, bool moving = false, bool gimbal = false, float acceleration = 90, float bank = 0, bool fixedAim = false)
        {
            if (!Finite(position) || !Finite(aim) || (aim - position).sqrMagnitude < .01f) return;
            string stateKey = StateKey(key, focus, other);
            if (!states.TryGetValue(stateKey, out var motion)) states[stateKey] = motion = new NativeBroadcastCameraMotion();
            motion.Step(position, aim, fov, dt, moving, gimbal, 110, acceleration, kind == NativeBroadcastShotKind.Trackside ? 110 : 80, bank);
            poses.Add(new Pose { motion = motion, aim = aim, targetHeight = targetHeight, fixedAim = fixedAim,
                candidate = new NativeBroadcastShotCandidate { key = key, kind = kind, focus = focus, other = other } });
        }
        float Zoom(Vector3 position, Vector3 aim, int focus, int other, NativeKartMotor[] racers, float height, float minimum, float maximum)
        {
            Bounds group = bounds[racers[focus]];
            if (other >= 0) group.Encapsulate(bounds[racers[other]]);
            Quaternion view = Quaternion.Inverse(Quaternion.LookRotation(aim - position));
            Vector3 low = group.min, high = group.max;
            Vector2 min = Vector2.one * float.MaxValue, max = Vector2.one * float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = view * (new Vector3((i & 1) == 0 ? low.x : high.x, (i & 2) == 0 ? low.y : high.y, (i & 4) == 0 ? low.z : high.z) - position);
                Vector2 projected = new Vector2(p.x, p.y) / Mathf.Max(.5f, p.z);
                min = Vector2.Min(min, projected); max = Vector2.Max(max, projected);
            }
            float tan = Mathf.Max((max.y - min.y) / (2 * Mathf.Max(.05f, height)), (max.x - min.x) / (2 * .78f * camera.aspect));
            return Mathf.Clamp(2 * Mathf.Atan(tan) * Mathf.Rad2Deg, minimum, maximum);
        }
        void Evaluate(Pose pose, NativeKartMotor[] racers, NativeBroadcastStory story, bool forecast, float now)
        {
            var c = pose.candidate; var m = pose.motion;
            bool mounted = c.kind == NativeBroadcastShotKind.Mounted;
            bool visible = Subject(pose, racers[c.focus], 0, out Rect first, out float visibility);
            Rect second = first;
            var d=NativeBroadcastCameraRegistry.Find(c.key);
            if(d)foreach(int seat in (d.targetIds??Array.Empty<string>()).Select(id=>NativeBroadcastCameraRegistry.BindingSeat(id,racers.Length)).Where(n=>n>=0)) visible &= Subject(pose,racers[seat],0,out _,out _);
            if (c.other >= 0) visible &= Subject(pose, racers[c.other], 0, out second, out _);
            if (mounted) visible = !Physics.CheckSphere(m.position, .35f, profile.obstructionMask, QueryTriggerInteraction.Ignore);
            c.visible = visible; c.rejection = visible ? "" : "clipped / occluded / gimbal not ready";
            c.readableSize = first.height;
            float size = Mathf.Clamp01(1 - Mathf.Abs(first.height - pose.targetHeight) / Mathf.Max(.07f, pose.targetHeight));
            float separation = c.other < 0 ? 1 : Mathf.Clamp01(Mathf.Abs(first.center.x - second.center.x) / Mathf.Max(.04f, Mathf.Min(first.width, second.width)));
            float pair = story?.other >= 0 ? c.other == story.other ? 1 : 0 : 1;
            if (forecast)
            {
                float duration = 0;
                for (int i = 1; i <= 3; i++)
                {
                    float t = i * .4f;
                    if (!Subject(pose, racers[c.focus], t, out _, out _) || (c.other >= 0 && !Subject(pose, racers[c.other], t, out _, out _))) break;
                    duration = t;
                }
                m.usableSeconds = duration;
            }
            c.usableSeconds = m.usableSeconds;
            c.coverage = Mathf.Clamp01(c.usableSeconds / 1.2f);
            float composition = Mathf.Clamp01(1 - Mathf.Abs(first.center.x - .5f) * 1.6f - Mathf.Abs(first.center.y - .5f));
            c.score = 20 * size + 15 * composition + 15 * visibility + 15 * separation + 20 * pair + 15 * c.coverage;
            if (mounted) c.score = 48 + 10 * c.coverage;
            // Gentle redundancy cost; no camera family is excluded by a fixed base-score hierarchy.
            if (selected != null && selected.kind == c.kind) c.score -= Mathf.Clamp((now - lastCutAt - 6) * 1.5f, 0, 10);
            if (!visible) c.score = 0;
        }
        bool Subject(Pose pose, NativeKartMotor motor, float ahead, out Rect rectangle, out float visibility)
        {
            var m = pose.motion; Vector3 offset = motor.DriveVelocity * ahead;
            Vector3 position = m.position + m.velocity * ahead; Quaternion rotation = m.rotation;
            if (ahead > 0 && !pose.fixedAim)
                rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(pose.aim + offset - position), 80 * ahead);
            Bounds b = bounds[motor]; b.center += offset;
            Vector3 low = b.min, high = b.max; Vector2 min = Vector2.one * float.MaxValue, max = Vector2.one * float.MinValue;
            float tan = Mathf.Tan(m.fieldOfView * Mathf.Deg2Rad * .5f); bool front = true;
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = new Vector3((i & 1) == 0 ? low.x : high.x, (i & 2) == 0 ? low.y : high.y, (i & 4) == 0 ? low.z : high.z);
                Vector3 local = Quaternion.Inverse(rotation) * (point - position); front &= local.z > .3f;
                float z = Mathf.Max(.3f, local.z);
                Vector2 projected = new Vector2(.5f + local.x / (2 * z * tan * camera.aspect), .5f + local.y / (2 * z * tan));
                min = Vector2.Min(min, projected); max = Vector2.Max(max, projected);
            }
            rectangle = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            visibility = 0;
            if (!front || min.x < .03f || max.x > .97f || min.y < .12f || max.y > .96f || rectangle.height < .025f) return false;
            if (Physics.CheckSphere(position, .35f, profile.obstructionMask, QueryTriggerInteraction.Ignore)) return false;
            Vector3[] samples = { b.center, b.center + Vector3.up * b.extents.y * .7f,
                b.center + Vector3.right * b.extents.x * .7f, b.center - Vector3.right * b.extents.x * .7f };
            foreach (var point in samples) if (!Physics.Linecast(position, point, profile.obstructionMask, QueryTriggerInteraction.Ignore)) visibility += .25f;
            return visibility >= .75f;
        }
        Bounds ReadBounds(NativeKartMotor motor)
        {
            if (!renderers.TryGetValue(motor, out var parts)) renderers[motor] = parts = motor.GetComponentsInChildren<Renderer>(true);
            Bounds result = new Bounds(Center(motor), new Vector3(2.4f, 2.6f, 3.2f));
            foreach (var part in parts)
                if (part && part.enabled && part.gameObject.activeInHierarchy && !(part is ParticleSystemRenderer) && !(part is TrailRenderer)
                    && !(part is LineRenderer) && part.bounds.size.magnitude < 12 && Vector3.Distance(part.bounds.center, motor.transform.position) < 8)
                    result.Encapsulate(part.bounds);
            return result;
        }
        void RouteFrame(Vector3[] route, Vector3 position, out Vector3 point, out Vector3 tangent)
        {
            point = route[0]; tangent = Vector3.forward; float closest = float.MaxValue;
            int previousSegment = routeSegment;
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1 && (previousSegment < 0 || closest < 2500)) break;
                for (int i = 0; i < route.Length; i++)
                {
                    int offset = Mathf.Abs(i - previousSegment);
                    if (pass == 0 && previousSegment >= 0 && Mathf.Min(offset, route.Length - offset) > 4) continue;
                    Vector3 start = route[i], delta = route[(i + 1) % route.Length] - start;
                    Vector3 projected = start + delta * Mathf.Clamp01(Vector3.Dot(position - start, delta) / Mathf.Max(.001f, delta.sqrMagnitude));
                    float distance = (projected - position).sqrMagnitude;
                    if (distance >= closest || delta.sqrMagnitude < .001f) continue;
                    closest = distance; point = projected; tangent = delta.normalized; routeSegment = i;
                }
            }
            tangent.y = 0; tangent.Normalize();
        }
        static Vector3 Center(NativeKartMotor motor) => motor.transform.position + Vector3.up * 1.3f;
        static Vector3 Heading(NativeKartMotor motor)
        {
            Vector3 forward = motor.DriveVelocity.sqrMagnitude > 4 ? motor.DriveVelocity : motor.transform.forward;
            forward.y = 0; return forward.sqrMagnitude > .001f ? forward.normalized : Vector3.forward;
        }
        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y)
            && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    }
}
