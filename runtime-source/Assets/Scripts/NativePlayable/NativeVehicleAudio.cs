using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeVehicleAudio : MonoBehaviour
    {
        NativeKartMotor motor;
        NativeAudioCatalog.VehicleEntry entry;
        AudioSource idle, accel, sfx, drift, surface;
        bool previousGrounded, running;
        int lastJumpFrame;
        public static void Bind(NativeKartMotor motor, string character, string vehicle)
        {
            if (!motor || !NativeAudioRuntime.Catalog) return;
            var entry = NativeAudioRuntime.Catalog.FindVehicle(character, vehicle);
            if (entry == null) return;
            var audio = motor.GetComponent<NativeVehicleAudio>() ?? motor.gameObject.AddComponent<NativeVehicleAudio>();
            if (audio.entry == entry) return;
            audio.motor = motor; audio.entry = entry;
            if (!audio.idle) { audio.idle = audio.Source(NativeAudioRuntime.Catalog.idleTemplate); audio.accel = audio.Source(NativeAudioRuntime.Catalog.accelTemplate); audio.sfx = audio.Source(NativeAudioRuntime.Catalog.sfxTemplate); audio.drift = audio.Source(NativeAudioRuntime.Catalog.driftTemplate); audio.surface = audio.Source(NativeAudioRuntime.Catalog.surfaceTemplate); }
            audio.idle.clip = entry.idle; audio.accel.clip = entry.accel;
            audio.idle.loop = audio.accel.loop = true; audio.idle.pitch = audio.accel.pitch = 0;
            audio.running = false; audio.previousGrounded = motor.Grounded;
        }
        AudioSource Source(AudioSource template)
        {
            if (!template) throw new System.InvalidOperationException("Original vehicle audio template is missing");
            var source = Instantiate(template.gameObject, transform, false).GetComponent<AudioSource>();
            source.playOnAwake = false; source.Stop();
            if (NativeAudioRuntime.Muted) source.enabled = false;
            return source;
        }
        void LateUpdate()
        {
            if (!motor || entry == null) return;
            if (NativeAudioRuntime.Muted || !motor.driving)
            { if (running) { idle.Stop(); accel.Stop(); sfx.Stop(); drift.Stop(); surface.Stop(); running = false; } return; }
            idle.enabled = accel.enabled = sfx.enabled = drift.enabled = surface.enabled = true;
            if (!running)
            {
                idle.pitch = accel.pitch = 0; if (idle.clip) idle.Play(); if (accel.clip) accel.Play();
                running = true; previousGrounded = motor.Grounded;
            }
            var catalog = NativeAudioRuntime.Catalog;
            float topSpeed = motor.stats._topSpeed;
            int gearIndex = (int)(motor.Speed / topSpeed * catalog.gearsMax);
            float speedPerGear = topSpeed / catalog.gearsMax;
            float rev = catalog.maxRevPerGear * motor.Speed / ((gearIndex + 1) * speedPerGear)
                * (gearIndex < catalog.gearsMax ? 1 : catalog.max);
            accel.pitch = Mathf.Clamp(Mathf.Lerp(accel.pitch, rev, Mathf.Clamp01(Time.deltaTime * 4)), 0, 1.15f);
            accel.volume = entry.silentOnAirborne && !motor.Grounded ? 0 : 1;
            idle.pitch = Mathf.Lerp(idle.pitch, motor.Speed < catalog.minSpeed ? 1 : 0, Mathf.Clamp01(Time.deltaTime * 2));
            float rollingVolume = Mathf.Clamp01(motor.Speed / (topSpeed / 5));
            drift.volume = surface.volume = rollingVolume;
            if (motor.IsDrifting && motor.Grounded) { if (!drift.isPlaying && drift.clip) drift.Play(); } else drift.Stop();
            var surfaceEntry = System.Array.Find(catalog.surfaces, row => row.id == motor.CurrentSurfaceKey);
            if (motor.Grounded && motor.OnGrass && !motor.InWater && surfaceEntry != null && surfaceEntry.clip)
            {
                if (surface.clip != surfaceEntry.clip) { surface.Stop(); surface.clip = surfaceEntry.clip; }
                if (!surface.isPlaying) surface.Play();
            }
            else surface.Stop();
            if (motor.JumpedThisStep && lastJumpFrame != Time.frameCount)
            { lastJumpFrame = Time.frameCount; Play(entry.jump); }
            if (!previousGrounded && motor.Grounded) Play(entry.land);
            previousGrounded = motor.Grounded;
        }
        void Play(AudioClip clip) { if (clip) { sfx.clip = clip; sfx.Play(); } }
        void OnDisable() { if (idle) idle.Stop(); if (accel) accel.Stop(); if (sfx) sfx.Stop(); if (drift) drift.Stop(); if (surface) surface.Stop(); running = false; }
    }
}
