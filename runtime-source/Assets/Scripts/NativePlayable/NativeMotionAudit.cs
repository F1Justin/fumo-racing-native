using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Profiling;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [DefaultExecutionOrder(1000)]
    public sealed class NativeMotionAudit : MonoBehaviour
    {
        const double LongFrameThresholdMs = 25.0;
        const string Header = "frame,realtime,frame_dt,fixed_steps,raw_x,raw_y,raw_z,render_x,render_y,render_z,render_forward_x,render_forward_y,render_forward_z,velocity_x,velocity_y,velocity_z,speed,throttle,steering,grounded,driving,resets,camera_x,camera_y,camera_z,view_x,view_y,view_z,viewport_x,viewport_y,gc0,gc1,gc2,focused,screen_width,screen_height,fullscreen_mode,phase,capture_event,ft_cpu_frame_ms,ft_cpu_main_ms,ft_cpu_render_ms,ft_gpu_ms,prof_main_ms,prof_render_ms,ft_cpu_present_wait_ms,ft_frame_start_timestamp,probe_loop_ms,probe_render_ms,probe_wait_ms,gc_alloc_bytes\n";
        static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        static readonly string[] Phases = { "menu", "countdown", "driving", "finished", "paused" };
        struct FrameSample
        {
            public int frame, fixedSteps, grounded, driving, resets, gc0, gc1, gc2, focused;
            public float realtime, frameDt, speed, throttle, steering, viewportX, viewportY;
            public Vector3 raw, render, forward, velocity, camera, view;
            public int screenWidth, screenHeight;
            public string fullScreenMode, phase, captureEvent;
            public double ftCpuFrame, ftCpuMain, ftCpuRender, ftGpu, profMain, profRender, ftCpuPresentWait;
            public double probeLoop, probeRender, probeWait;
            public ulong ftFrameStartTimestamp;
            public long gcAllocBytes;
        }

        readonly List<FrameSample> rows = new List<FrameSample>(32768);
        readonly List<string> longFrames = new List<string>();
        long previousAllocatedBytes = -1;
        NativeRace race;
        string directory;
        bool written;
        bool timingEnabled;
        bool productionRecording, detailedLongFrames;
        const int MaximumSamples = 120000;
        ulong lastFrameStartTimestamp;
        int gpuTimingSamples;
        string startedUtc;
        FullScreenMode lastFullScreenMode;
        string fullScreenMode;
        FrameTiming[] timings = new FrameTiming[1];
        ProfilerRecorder mainRecorder, renderRecorder, allocationRecorder;

        [Serializable]
        sealed class EnvironmentReport
        {
            public string unityVersion, operatingSystem, deviceModel, processor, graphicsDevice, graphicsApi;
            public string startedUtc, fullScreenMode, timingSemantics;
            public float simulationCaptureDeltaTime;
            public int screenWidth, screenHeight, targetFrameRate, vSyncCount, qualityLevel, antiAliasing;
            public bool frameTimingEnabled, mainRecorderValid, renderRecorderValid, developmentBuild, gcAllocationRecorderValid;
            public int gpuTimingSamples;
        }

        public void Configure(NativeRace target, string reportDirectory, bool production = false)
        {
            productionRecording = production;
            detailedLongFrames = !production || Array.IndexOf(Environment.GetCommandLineArgs(), "--performance-detailed-long-frames") >= 0;
            if (production) rows.Capacity = MaximumSamples;
            race = target;
            directory = reportDirectory;
            startedUtc = DateTime.UtcNow.ToString("O", Culture);
            lastFullScreenMode = Screen.fullScreenMode;
            fullScreenMode = lastFullScreenMode.ToString();
            timingEnabled = FrameTimingManager.IsFeatureEnabled();
            mainRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            renderRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 1);
            allocationRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        }

        static double Milliseconds(double value) { return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value) ? value : double.NaN; }
        static double NonnegativeMilliseconds(double value) { return value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value) ? value : double.NaN; }
        static double RecorderMilliseconds(ProfilerRecorder recorder)
        {
            return recorder.Valid && recorder.Count > 0 ? Milliseconds(recorder.LastValue * 1e-6) : double.NaN;
        }

        void LateUpdate()
        {
            if (!race || race.racers == null || race.racers.Length == 0 || !race.racers[0] || !race.raceCamera)
                return;
            if (rows.Count >= MaximumSamples && productionRecording) { Flush(); enabled = false; return; }
            var motor = race.racers[0];
            var camera = race.raceCamera;
            Vector3 raw = motor.transform.position;
            Vector3 render = motor.RenderPosition;
            Vector3 forward = motor.RenderRotation * Vector3.forward;
            Vector3 velocity = motor.PhysicsVelocity;
            Vector3 cameraPosition = camera.transform.position;
            Vector3 view = camera.transform.InverseTransformPoint(render);
            Vector3 viewport = camera.WorldToViewportPoint(render);
            double cpuFrame = double.NaN, cpuMain = double.NaN, cpuRender = double.NaN, gpu = double.NaN;
            double presentWait = double.NaN;
            ulong frameStartTimestamp = 0;
            if (timingEnabled)
            {
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 &&
                    timings[0].frameStartTimestamp != 0 && timings[0].frameStartTimestamp != lastFrameStartTimestamp)
                {
                    lastFrameStartTimestamp = timings[0].frameStartTimestamp;
                    cpuFrame = Milliseconds(timings[0].cpuFrameTime);
                    cpuMain = Milliseconds(timings[0].cpuMainThreadFrameTime);
                    cpuRender = Milliseconds(timings[0].cpuRenderThreadFrameTime);
                    gpu = Milliseconds(timings[0].gpuFrameTime);
                    if (!double.IsNaN(gpu)) gpuTimingSamples++;
                    presentWait = NonnegativeMilliseconds(timings[0].cpuMainThreadPresentWaitTime);
                    frameStartTimestamp = timings[0].frameStartTimestamp;
                }
                FrameTimingManager.CaptureFrameTimings();
            }
            int phaseIndex = race.DiagnosticPhase;
            string phase = phaseIndex >= 0 && phaseIndex < Phases.Length ? Phases[phaseIndex] : "unknown";
            if (Screen.fullScreenMode != lastFullScreenMode)
            {
                lastFullScreenMode = Screen.fullScreenMode;
                fullScreenMode = lastFullScreenMode.ToString();
            }

            rows.Add(new FrameSample {
                frame = Time.frameCount, realtime = Time.realtimeSinceStartup,
                frameDt = Time.unscaledDeltaTime, fixedSteps = motor.FixedStepCount,
                raw = raw, render = render, forward = forward, velocity = velocity,
                speed = motor.Speed, throttle = motor.throttle, steering = motor.steering,
                grounded = motor.Grounded ? 1 : 0, driving = motor.driving ? 1 : 0,
                resets = motor.Resets, camera = cameraPosition, view = view,
                viewportX = viewport.x, viewportY = viewport.y,
                gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2),
                focused = Application.isFocused ? 1 : 0,
                screenWidth = Screen.width, screenHeight = Screen.height,
                fullScreenMode = fullScreenMode, phase = phase,
                captureEvent = Time.frameCount - race.DiagnosticCaptureFrame <= 1 &&
                    Time.frameCount - race.DiagnosticCaptureFrame >= 0 ? "screenshot" : null,
                ftCpuFrame = cpuFrame, ftCpuMain = cpuMain, ftCpuRender = cpuRender, ftGpu = gpu,
                profMain = RecorderMilliseconds(mainRecorder), profRender = RecorderMilliseconds(renderRecorder),
                ftCpuPresentWait = presentWait, ftFrameStartTimestamp = frameStartTimestamp,
                gcAllocBytes = allocationRecorder.Valid && allocationRecorder.Count > 0 ? allocationRecorder.LastValue : -1,
                probeLoop = NativeFrameProbe.PreviousLoopMilliseconds,
                probeRender = NativeFrameProbe.PreviousRenderMilliseconds,
                probeWait = NativeFrameProbe.WaitMilliseconds()
            });
            if (detailedLongFrames) RecordLongFrame(rows[rows.Count - 1], cpuMain, gpu, presentWait);
        }

        /// <summary>
        /// Keeps a compact snapshot of every frame over the threshold. The CSV can
        /// only say that a frame was long; this records the state needed to say
        /// which part of it was long and whether anything was allocated.
        /// </summary>
        void RecordLongFrame(FrameSample row, double cpuMain, double gpu, double presentWait)
        {
            double frameMs = row.frameDt * 1000.0;
            long allocated = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            long allocatedDelta = previousAllocatedBytes < 0 ? 0 : allocated - previousAllocatedBytes;
            previousAllocatedBytes = allocated;
            if (frameMs < LongFrameThresholdMs || longFrames.Count >= 20000) return;
            longFrames.Add("{\"frame\":" + row.frame
                + ",\"realtime\":" + row.realtime.ToString("R", Culture)
                + ",\"frame_ms\":" + frameMs.ToString("R", Culture)
                + ",\"scaled_ms\":" + (Time.deltaTime * 1000.0).ToString("R", Culture)
                + ",\"time_scale\":" + Time.timeScale.ToString("R", Culture)
                + ",\"fixed_dt_ms\":" + (Time.fixedDeltaTime * 1000.0).ToString("R", Culture)
                + ",\"capture_dt_ms\":" + (Time.captureDeltaTime * 1000.0).ToString("R", Culture)
                + ",\"maximum_dt_ms\":" + (Time.maximumDeltaTime * 1000.0).ToString("R", Culture)
                + ",\"probe_loop_ms\":" + row.probeLoop.ToString("R", Culture)
                + ",\"probe_render_ms\":" + row.probeRender.ToString("R", Culture)
                + ",\"probe_wait_ms\":" + row.probeWait.ToString("R", Culture)
                + ",\"allocated_delta_mb\":" + (allocatedDelta / 1048576.0).ToString("R", Culture)
                + ",\"allocated_mb\":" + (allocated / 1048576.0).ToString("R", Culture)
                + ",\"gc0\":" + row.gc0 + ",\"gc1\":" + row.gc1 + ",\"gc2\":" + row.gc2
                + ",\"focused\":" + row.focused
                + ",\"phase\":\"" + row.phase + "\""
                + ",\"ft_cpu_main_ms\":" + (double.IsNaN(cpuMain) ? "null" : cpuMain.ToString("R", Culture))
                + ",\"ft_gpu_ms\":" + (double.IsNaN(gpu) ? "null" : gpu.ToString("R", Culture))
                + ",\"ft_present_wait_ms\":" + (double.IsNaN(presentWait) ? "null" : presentWait.ToString("R", Culture))
                + "}");
        }

        static void Float(TextWriter writer, float value) { writer.Write(value.ToString("R", Culture)); writer.Write(','); }
        static void Integer(TextWriter writer, int value) { writer.Write(value); writer.Write(','); }
        static void Vector(TextWriter writer, Vector3 value)
        {
            Float(writer, value.x); Float(writer, value.y); Float(writer, value.z);
        }
        static void Metric(TextWriter writer, double value)
        {
            if (!double.IsNaN(value)) writer.Write(value.ToString("R", Culture));
            writer.Write(',');
        }

        void OnApplicationQuit() { Write(); }
        void OnDestroy()
        {
            Write();
            mainRecorder.Dispose();
            renderRecorder.Dispose();
            allocationRecorder.Dispose();
        }

        public void Flush() { Write(); }

        void Write()
        {
            if (written || string.IsNullOrEmpty(directory)) return;
            written = true;
            try
            {
                Directory.CreateDirectory(directory);
                var environment = new EnvironmentReport {
                    simulationCaptureDeltaTime = Time.captureDeltaTime,
                    unityVersion = Application.unityVersion, operatingSystem = SystemInfo.operatingSystem,
                    deviceModel = SystemInfo.deviceModel, processor = SystemInfo.processorType,
                    graphicsDevice = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                    startedUtc = startedUtc,
                    screenWidth = Screen.width, screenHeight = Screen.height, fullScreenMode = Screen.fullScreenMode.ToString(),
                    targetFrameRate = Application.targetFrameRate, vSyncCount = QualitySettings.vSyncCount,
                    qualityLevel = QualitySettings.GetQualityLevel(), antiAliasing = QualitySettings.antiAliasing,
                    frameTimingEnabled = timingEnabled, mainRecorderValid = mainRecorder.Valid,
                    renderRecorderValid = renderRecorder.Valid, developmentBuild = Debug.isDebugBuild,
                    gcAllocationRecorderValid = allocationRecorder.Valid, gpuTimingSamples = gpuTimingSamples,
                    timingSemantics = "FrameTimingManager and ProfilerRecorder return latest completed samples; values are diagnostic context, not guaranteed to align with frame_dt on this CSV row. Missing or zero timings are empty. NativeFrameProbe render is a CPU camera-callback wall-clock span, not GPU time; its wait is a callback gap, not pure timer wait. GC Allocated In Frame is a latest-completed profiler counter, not engine allocated-memory delta."
                };
                File.WriteAllText(Path.Combine(directory, productionRecording ? "performance-motion-environment.json" : "motion-environment.json"), JsonUtility.ToJson(environment, true));
                using (var writer = new StreamWriter(Path.Combine(directory, productionRecording ? "performance-frames.csv" : "motion-frames.csv")))
                {
                    writer.Write(Header);
                    foreach (var row in rows)
                    {
                        Integer(writer, row.frame);
                        Float(writer, row.realtime);
                        Float(writer, row.frameDt);
                        Integer(writer, row.fixedSteps);
                        Vector(writer, row.raw);
                        Vector(writer, row.render);
                        Vector(writer, row.forward);
                        Vector(writer, row.velocity);
                        Float(writer, row.speed);
                        Float(writer, row.throttle);
                        Float(writer, row.steering);
                        Integer(writer, row.grounded);
                        Integer(writer, row.driving);
                        Integer(writer, row.resets);
                        Vector(writer, row.camera);
                        Vector(writer, row.view);
                        Float(writer, row.viewportX);
                        Float(writer, row.viewportY);
                        Integer(writer, row.gc0);
                        Integer(writer, row.gc1);
                        Integer(writer, row.gc2);
                        Integer(writer, row.focused);
                        Integer(writer, row.screenWidth);
                        Integer(writer, row.screenHeight);
                        writer.Write(row.fullScreenMode); writer.Write(',');
                        writer.Write(row.phase); writer.Write(',');
                        writer.Write(row.captureEvent); writer.Write(',');
                        Metric(writer, row.ftCpuFrame); Metric(writer, row.ftCpuMain);
                        Metric(writer, row.ftCpuRender); Metric(writer, row.ftGpu);
                        Metric(writer, row.profMain);
                        if (!double.IsNaN(row.profRender)) writer.Write(row.profRender.ToString("R", Culture));
                        writer.Write(',');
                        if (!double.IsNaN(row.ftCpuPresentWait)) writer.Write(row.ftCpuPresentWait.ToString("R", Culture));
                        writer.Write(',');
                        if (row.ftFrameStartTimestamp != 0) writer.Write(row.ftFrameStartTimestamp);
                        writer.Write(',');
                        Metric(writer, row.probeLoop); Metric(writer, row.probeRender);
                        if (!double.IsNaN(row.probeWait)) writer.Write(row.probeWait.ToString("R", Culture));
                        writer.Write(',');
                        if (row.gcAllocBytes >= 0) writer.Write(row.gcAllocBytes);
                        writer.WriteLine();
                    }
                }
                File.WriteAllLines(Path.Combine(directory, productionRecording ? "performance-long-frames.jsonl" : "long-frames.jsonl"), longFrames);
                Debug.Log("NATIVE_MOTION_AUDIT samples=" + rows.Count + " long_frames=" + longFrames.Count
                    + " directory=" + directory);
            }
            catch (Exception exception)
            {
                Debug.LogError("NATIVE_MOTION_AUDIT_WRITE_FAILED " + exception);
            }
        }
    }
}
