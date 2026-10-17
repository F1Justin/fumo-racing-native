using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [DefaultExecutionOrder(-100)]
    public sealed class NativeKartHit : MonoBehaviour, INativeKartDamageReceiver
    {
        public NativeKartMotor motor;
        public GameObject damageFX, toHide;
        public float defaultDuration = 1f;
        public bool working = true;
        public Func<bool> DamageAllowed;
        public Action<bool> ItemsFrozenChanged;
        public event Action<float> Damaged;
        public NativeKartHitState State { get; } = new NativeKartHitState();
        public bool Frozen => State.Frozen;
        public bool Invulnerable => State.Invulnerable;
        public int TickCount { get; private set; }
        AudioSource healthSource;
        bool telemetryFrozen;

        void Awake()
        {
            if (!motor) motor = GetComponent<NativeKartMotor>();
            ApplyVisualState();
        }

        void Update()
        {
            if (NativeRaceNetwork.ClientRace) return;
            if (Time.deltaTime <= 0f) return;
            TickCount++;
            State.Advance(Time.deltaTime, recovered: Respawn);
        }

        public void ApplyNetworkHit(bool frozen, bool invulnerable, bool visible, int damage)
        {
            ObserveFrozen(frozen, "network");
            State.ApplyNetworkState(frozen, invulnerable, visible, damage);
            motor.SetHitFrozen(frozen);
            ItemsFrozenChanged?.Invoke(frozen);
            if (damageFX) damageFX.SetActive(visible);
            if (toHide) toHide.SetActive(!visible);
        }

        public bool TakeDamage() => TakeDamage(defaultDuration);

        public bool TakeDamage(float duration)
        {
            if (NativeRaceNetwork.ClientRace) { NativeTelemetry.Count("vehicle.damage.non-authority"); return false; }
            bool allowed = working && (DamageAllowed == null || DamageAllowed());
            bool accepted = State.TakeDamage(duration, working, allowed, () =>
            {
                ApplyVisualState();
                SetFrozen(true);
                healthSource = NativeAudioRuntime.PlayEvent("health.damage", transform);
                NativeRaceNetwork.RecordSourceAudio(transform,"health.damage");
                Damaged?.Invoke(duration);
                motor.PublishHaptic(NativeHapticKind.Damage);
            });
            NativeTelemetry.Count(accepted ? "vehicle.damage.accepted" : "vehicle.damage.ignored");
            return accepted;
        }

        public bool FreezeFor(float duration) => !NativeRaceNetwork.ClientRace && State.FreezeFor(duration, () =>
        {
            SetFrozen(true);
            NativeAudioRuntime.StopEvent("health.damage", transform);
            NativeRaceNetwork.RecordSourceAudio(transform,"health.damage.stop");
            if (healthSource) healthSource.clip = null;
        });

        public void MakeInvulnerable(float duration) => State.MakeInvulnerable(duration);

        public void ResetState()
        {
            NativeAudioRuntime.StopEvent("health.damage", transform);
            NativeRaceNetwork.RecordSourceAudio(transform,"health.damage.stop");
            State.Reset();
            TickCount = 0;
            SetFrozen(false);
            ApplyVisualState();
        }

        void Respawn(bool damaged)
        {
            if (damaged) motor.PublishHaptic(NativeHapticKind.Respawn);
            if (damaged) { NativeAudioRuntime.PlayEvent("health.respawn", transform); NativeRaceNetwork.RecordSourceAudio(transform,"health.respawn"); }
            SetFrozen(false);
            ApplyVisualState();
        }

        void SetFrozen(bool frozen)
        {
            ObserveFrozen(frozen, "authority");
            if (motor) motor.SetHitFrozen(frozen);
            ItemsFrozenChanged?.Invoke(frozen);
        }

        void ObserveFrozen(bool frozen, string source)
        {
            if (telemetryFrozen == frozen) return;
            telemetryFrozen = frozen;
            NativeTelemetry.Event("vehicle.freeze.changed", "applied", detail: "entity=" + GetInstanceID() + ";frozen=" + frozen + ";source=" + source);
        }

        void ApplyVisualState()
        {
            if (damageFX && damageFX.activeSelf != State.DamageVisible) damageFX.SetActive(State.DamageVisible);
            if (toHide && toHide.activeSelf == State.DamageVisible) toHide.SetActive(!State.DamageVisible);
        }
    }
}
