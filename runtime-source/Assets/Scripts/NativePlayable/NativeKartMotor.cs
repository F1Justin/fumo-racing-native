#if UNITY_6000_0_OR_NEWER
using PhysicMaterial = UnityEngine.PhysicsMaterial;
#endif
using UnityEngine;

namespace Fumo.NativePlayable
{
    public enum NativeVehicleModuleKind { None, BroomAirBoost, GenjiGrassBoost, ShoppingCartDriftCap }

    [System.Serializable]
    public sealed class NativeKartSettings
    {
        public float _acceleration, _topSpeed, _topSpeedGrass, _brakeForce, _jumpForce, _stickForce;
        public float _turnForce, _turnMax, _turnLowMult, _turnFriction, _turnLoseSpeed, _smooth;
        public float _turnAirMult, _turnVelAirMult, _gravity, _flipSpeedBoost;
        public bool _convertsAirVelocity;
        public float _driftAutoIntensity, _driftSteerGain;
        public float _driftSpeedGain, _driftSpeedDrain, _driftDrainAngle, _driftGrassTopSpeed, _driftGrassFriction;
        public float _driftSmooth, _driftAngle;
        public float _friction, _frictionGrass, _slopeSpeedGain, _slopeSpeedLoss, _slopeAccelGain, _slopeAccelLoss;
        public float _topSpeedCap, _topSpeedAdd, _driftSpeedMaxGain;
        public float _slopeLimit;
        public bool _isWaterProof;
        public bool _autoAccel, _bikeAnim, _canWheelie;
        public float _wheelieSpeedAdd, _wheelieSteerAdd, _wheelieBalance, _wheelieGravity;
        public NativeVehicleModuleKind _moduleKind;
        public float _moduleUpRate, _moduleDecayRate, _moduleGrassRate, _moduleGrassDriftRate;
        public float _moduleLimit, _moduleSpeedBoost, _moduleActiveTime, _moduleHurtTime;
        public int _moduleSteps;
        public float[] _moduleSpeedBoosts;
        public float _crashFactor = 20f, _crashMaxRate = 0.1f, _forceFriction = 6f;
        public AudioClip _accel;
        public NativeKartSettings Copy() => (NativeKartSettings)MemberwiseClone();
    }

    [System.Serializable]
    public sealed class NativeFeedbackSurface
    {
        public PhysicMaterial material;
        public string key;
        public Color dirtColor = Color.white;
        public float dirtColorEnabled;
    }

    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(CharacterController))]
    public sealed partial class NativeKartMotor : MonoBehaviour
    {
        public NativeKartSettings stats;
        public float OriginalHudBaseTopSpeed { get; internal set; }
        public NativeFeedbackSurface[] feedbackSurfaces = new NativeFeedbackSurface[0];
        public Transform visual;
        public Transform driftPivot;
        public bool driving;
        public bool HitFrozen { get; private set; }
        public float throttle, steering;
        public bool AIDriftActive;
        public int LastResetFrame { get; private set; } = -100;
        public float AIDriftDirection;
        public float SpeedBeforeStep { get; private set; }
        public bool braking, drifting, jump, jumpHeld;
        public bool? VerificationSpinRequested { get; set; }
        public bool? AISpinRequested { get; set; }
        public bool SpinRequested => VerificationSpinRequested ?? AISpinRequested
            ?? (stats != null && stats._flipSpeedBoost > 0f && (braking || jumpHeld));
        public NativeKartSpinState SpinState { get; private set; }
        public int LastLandingFlips { get; private set; }
        public float LastLandingBonus { get; private set; }
        public float CurrentAirVelocityTurn { get; private set; }
        public float Speed { get; private set; }
        NativeKartCollisionState collision;
        public NativeKartCollisionState CollisionState => collision;
        public CollisionFlags LastCollisionFlags { get; private set; }
        public int CollisionContactCount { get; private set; }
        public int VehicleContactCount { get; private set; }
        public float Distance { get; private set; }
        public bool Grounded { get; private set; }
        public bool OnGrass { get; private set; }
        public int Resets { get; private set; }
        public bool InWater { get; private set; }
        public bool Waterproof => stats != null && stats._isWaterProof;
        public int ControlReverseCount { get; private set; }
        public bool ControlsReversed => ControlReverseCount > 0;
        public float ModuleCharge { get; private set; }
        public float ModuleBoostRemaining { get; private set; }
        public int ModuleTriggerCount { get; private set; }
        public int ModuleStage => moduleSteps;
        public bool ModuleReady => moduleReady;
        public bool IsWheelie { get; private set; }
        public float WheelieBalance { get; private set; }
        public string CurrentSurfaceKey => particleSurfaceKey;
        public Vector3 RenderPosition { get; private set; }
        public Quaternion RenderRotation { get; private set; }
        public Vector3 PhysicsVelocity { get; private set; }
        public int FixedStepCount { get; private set; }
        public bool UseFixedVerificationClock;
        public float LastStepDeltaTime { get; private set; }
        public int LastStepFrame { get; private set; }
        public int Landings { get; private set; }
        public float CurrentSteering => stats != null && stats._turnMax > 0f ? steeringAngle / stats._turnMax : 0f;
        public float CurrentTurn => steeringAngle;
        public bool UsesRestoredSteering { get; private set; }
        public bool UsesRestoredAcceleration { get; private set; }
        public bool UsesRestoredDrift { get; private set; }
        public bool UsesRestoredAirborne { get; private set; }
        public float AirTime { get; private set; }
        public float CurrentAirTurn { get; private set; }
        public Vector3 DriveVelocity => velocity;
        public float VerticalSpeed => verticalSpeed;
        public bool JumpedThisStep { get; private set; }
        public bool LeftGroundThisStep { get; private set; }
        public bool LandedThisStep { get; private set; }
        public NativeKartDriftState DriftState { get; private set; }
        public bool IsDrifting => DriftState.direction != 0f;
        public float DriftDirection => DriftState.direction;
        public float DriftTime => DriftState.elapsed;
        public Quaternion DriftVisualRotation => driftRotation;
        public float GroundAngle { get; private set; }
        public Vector3 LastStepSideContactNormal { get; private set; }
        public float CurrentVerticalSpeed { get; private set; }
        public NativeKartAccelerationState AccelerationState { get; private set; }
        internal Vector3 GroundNormal => normal;
        public NativeKartFeedbackInput FeedbackInput => new NativeKartFeedbackInput
        {
            speedRatio = stats != null && stats._topSpeed > 0 ? Speed / stats._topSpeed : 0,
            topSpeedModifier = 1,
            onGrass = OnGrass || InWater,
            inWater = InWater,
            hasSurface = feedbackSurface != null,
            surfaceColor = feedbackSurface != null ? feedbackSurface.dirtColor : Color.white,
            surfaceColorEnabled = feedbackSurface != null ? feedbackSurface.dirtColorEnabled : 0
        };
        NativeFeedbackSurface feedbackSurface;
        string particleSurfaceKey;
        NativeKartFeedback feedback;
        NativeKartParticles particles;
        NativeKartAnimationActions animationActions;
        NativeKartSurfaceParticles surfaceParticles;
        CharacterController body;
        AudioSource engine;
        float verticalSpeed, steeringAngle;
        float pendingModuleSpeed, pendingModuleAirSpeed;
        int moduleSteps;
        bool moduleReady;
        int wheelieSequence;
        float wheelieSequenceAge, lastWheelieInput, wheelieInput, wheelieTime, wheelieGravity;
        bool validGround;
        Vector3 velocity, normal = Vector3.up;
        Vector3 previousPosition, currentPosition, modelOffset;
        Quaternion previousRotation, currentRotation;
        Quaternion previousDriftRotation = Quaternion.identity, driftRotation = Quaternion.identity;

        void Awake()
        {
            if (stats != null) OriginalHudBaseTopSpeed = stats._topSpeed;
            body = GetComponent<CharacterController>();
            particles = GetComponent<NativeKartParticles>();
            animationActions = GetComponent<NativeKartAnimationActions>();
            surfaceParticles = GetComponent<NativeKartSurfaceParticles>();
            engine = gameObject.AddComponent<AudioSource>();
            engine.loop = true;
            engine.playOnAwake = false;
            engine.enabled = false;
            engine.spatialBlend = 0.8f;
            engine.minDistance = 5;
            engine.maxDistance = 60;
            modelOffset = visual ? visual.localPosition : Vector3.zero;
            ResetRenderPose();
        }

        void Start()
        {
            var hit = GetComponent<NativeKartHit>();
            if (hit) hit.Damaged += OnModuleDamage;
            if (stats != null && stats._accel)
            {
                engine.clip = stats._accel;
                engine.volume = 0f;
            }
        }

        public void ResetAt(Vector3 position, Quaternion rotation, bool recovery)
        {
            LastResetFrame = Time.frameCount;
            if (!body) body = GetComponent<CharacterController>();
            body.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            body.enabled = true;
            VerificationSpinRequested = null;
            AISpinRequested = null;
            Speed = SpeedBeforeStep = verticalSpeed = steeringAngle = 0;
            AIDriftActive = false;
            AIDriftDirection = 0f;
            collision = new NativeKartCollisionState { upNormal = Vector3.up };
            LastStepSideContactNormal = Vector3.zero;
            LastCollisionFlags = CollisionFlags.None;
            CollisionContactCount = VehicleContactCount = 0;
            UsesRestoredSteering = UsesRestoredAcceleration = UsesRestoredDrift = UsesRestoredAirborne = false;
            JumpedThisStep = LeftGroundThisStep = LandedThisStep = validGround = false;
            AirTime = CurrentAirTurn = 0f;
            SpinState = default;
            LastLandingFlips = 0;
            LastLandingBonus = CurrentAirVelocityTurn = 0f;
            DriftState = default;
            previousDriftRotation = driftRotation = Quaternion.identity;
            GroundAngle = 0f;
            AccelerationState = default;
            CurrentVerticalSpeed = 0f;
            velocity = Vector3.zero;
            normal = Vector3.up;
            InWater = OnGrass = Grounded = false;
            ResetVehicleModule();
            StopWheelie();
            ResetWheelieInput();
            feedbackSurface = null;
            particleSurfaceKey = null;
            if (!feedback) feedback = GetComponent<NativeKartFeedback>();
            if (!recovery && feedback) feedback.Reset();
            if (!particles) particles = GetComponent<NativeKartParticles>();
            if (particles) particles.ResetState();
            if (!surfaceParticles) surfaceParticles = GetComponent<NativeKartSurfaceParticles>();
            if (surfaceParticles) surfaceParticles.ResetState();
            jump = jumpHeld = false;
            if (!recovery) ControlReverseCount = 0;
            if (recovery) Resets++;
            else { Resets = 0; Distance = 0; }
            PhysicsVelocity = Vector3.zero;
            ResetRenderPose();
        }

        public void ResetVehicleModule()
        {
            ModuleCharge = ModuleBoostRemaining = pendingModuleSpeed = pendingModuleAirSpeed = 0f;
            hapticContacts.Reset();hapticCartRisk=0;
            ModuleTriggerCount = moduleSteps = 0;
            moduleReady = false;
        }

        public void WheelieInputVertical(float value)
        {
            wheelieInput = Mathf.Clamp(value, -1f, 1f);
            if (Mathf.Abs(wheelieInput) < 0.75f) return;
            if (wheelieSequenceAge >= 0.4f) wheelieSequence = 0;
            if (wheelieSequence == 0 || Mathf.Abs(wheelieInput + lastWheelieInput) <= 0.25f)
            {
                wheelieSequence++;
                lastWheelieInput = wheelieInput;
                wheelieSequenceAge = 0f;
                if (wheelieSequence >= 3)
                {
                    wheelieSequence = 0;
                    StartWheelie();
                }
            }
        }

        void StartWheelie()
        {
            if (IsWheelie || stats == null || !stats._canWheelie || !driving || !Grounded ||
                HitFrozen || braking || IsDrifting) return;
            IsWheelie = true;
            PublishHaptic(NativeHapticKind.WheelieBegin);
            WheelieBalance = 0.5f;
            wheelieTime = 0f;
            wheelieGravity = stats._wheelieGravity;
        }

        public void StopWheelie()
        {
            if (IsWheelie && LastResetFrame != Time.frameCount) PublishHaptic(NativeHapticKind.WheelieEnd);
            IsWheelie = false;
            WheelieBalance = wheelieGravity = wheelieTime = 0f;
        }

        public void ResetWheelieInput()
        {
            wheelieSequence = 0;
            wheelieSequenceAge = lastWheelieInput = wheelieInput = 0f;
        }

        void StepWheelie(float dt)
        {
            if (wheelieSequence != 0 && (wheelieSequenceAge += dt) >= 0.4f) wheelieSequence = 0;
            if (!IsWheelie) return;
            if (!Grounded || braking || IsDrifting || HitFrozen) { StopWheelie(); return; }
            wheelieTime += dt;
            wheelieGravity += stats._wheelieBalance * wheelieInput * dt * Mathf.Clamp01(wheelieTime * 2f);
            WheelieBalance += wheelieGravity * dt;
            if (WheelieBalance <= 0f || WheelieBalance >= 1f) StopWheelie();
        }

        void OnModuleDamage(float duration)
        {
            ModuleCharge = 0f;
            moduleSteps = 0;
            moduleReady = false;
        }

        internal void RestoreVerificationGroundState(bool grounded, float gravity)
        {
            Grounded = grounded;
            verticalSpeed = CurrentVerticalSpeed = gravity;
        }

        public void TeleportRecovery(Vector3 position, Quaternion rotation)
        {
            SetHitFrozen(true);
            SetHitFrozen(false);
            LastResetFrame = Time.frameCount;
            if (!body) body = GetComponent<CharacterController>();
            body.enabled = false;
            SpinState = default;
            verticalSpeed = CurrentVerticalSpeed = 0f;
            transform.SetPositionAndRotation(position, rotation);
            body.enabled = true;
            Resets++;
            ResetRenderPose();
        }

        public void TeleportTrack(Vector3 position, Quaternion rotation)
        {
            LastResetFrame = Time.frameCount;
            if (!body) body = GetComponent<CharacterController>();
            body.enabled = false;
            SpinState = default;
            verticalSpeed = CurrentVerticalSpeed = 0f;
            transform.SetPositionAndRotation(position, rotation);
            body.enabled = true;
            ResetRenderPose();
        }

        public void SetHitFrozen(bool frozen)
        {
            if (frozen && !Grounded)
            {
                Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
                transform.rotation = horizontal.sqrMagnitude > 0f ? Quaternion.LookRotation(horizontal) : Quaternion.identity;
            }
            if (frozen)
            {
                StopWheelie();
                ResetWheelieInput();
            }
            HitFrozen = frozen;
            Speed = SpeedBeforeStep = steeringAngle = 0f;
            velocity = PhysicsVelocity = collision.force = Vector3.zero;
            gameObject.layer = frozen ? 4 : 0;
            ResetRenderPose();
        }

        public void StopCurrentSpeed()
        {
            Speed = SpeedBeforeStep = 0f;
        }

        public void AddControlReverse()
        {
            ControlReverseCount++;
        }

        public void RemoveControlReverse()
        {
            ControlReverseCount = Mathf.Max(0, ControlReverseCount - 1);
        }

        public void ClearControlReverse()
        {
            insanityReverseGeneration++;
            ControlReverseCount = 0;
        }

        public void ApplyNetworkPose(Vector3 position, Quaternion rotation, Vector3 speedVector, float speed, float distance, bool hard)
        {
            float error = Vector3.Distance(transform.position, position);
            var next = hard || error > 8f ? position : Vector3.Lerp(transform.position, position, 0.35f);
            transform.SetPositionAndRotation(next, hard ? rotation : Quaternion.Slerp(transform.rotation, rotation, 0.4f));
            currentPosition = previousPosition = next;
            currentRotation = previousRotation = transform.rotation;
            velocity = speedVector; Speed = speed; Distance = distance;
            RenderPosition = next; RenderRotation = transform.rotation;
        }

        void Update()
        {
            if (NativeRaceNetwork.ClientRace && !NativeRaceNetwork.IsLocalMotor(this)) return;
            if (!UseFixedVerificationClock) Simulate(Time.deltaTime);
        }

        void FixedUpdate()
        {
            if (UseFixedVerificationClock) Simulate(Time.fixedDeltaTime);
        }

        void Simulate(float dt)
        {
            if (Time.timeScale == 0f || dt <= 0f) return;
            SpeedBeforeStep = Speed;
            LastStepDeltaTime = dt;
            LastStepFrame = Time.frameCount;
            collision.AdvanceCrashTime(dt);
            if (HitFrozen) return;
            previousPosition = currentPosition;
            previousRotation = currentRotation;
            previousDriftRotation = driftRotation;
            bool wasGrounded = Grounded;
            JumpedThisStep = LeftGroundThisStep = LandedThisStep = false;
            UsesRestoredAirborne = !Grounded;
            CurrentAirTurn = 0f;
            CurrentAirVelocityTurn = LastLandingBonus = 0f;
            LastLandingFlips = 0;
            bool wasDrifting = IsDrifting;
            DriftState = driving && AIDriftActive
                ? NativeKartDriftState.DirectRequest(DriftState, AIDriftDirection, Grounded, stats)
                : NativeKartDriftState.Request(DriftState, new NativeKartDriftInput
            {
                requested = driving && drifting, grounded = Grounded, braking = braking,
                wishAccel = throttle, wishTurn = steering
            }, stats);
            StepWheelie(dt);
            if (driving) StepVehicleModule(dt);
            bool animationDriftEntered = !wasDrifting && IsDrifting;
            if (particles) particles.SetDrifting(IsDrifting);
            UsesRestoredDrift = Grounded && IsDrifting;
            UsesRestoredSteering = Grounded;
            float turnLimit = stats._turnMax;
            if (UsesRestoredSteering)
            {
                var turn = NativeKartSteeringState.Step(steeringAngle, new NativeKartSteeringInput
                {
                    speed = Speed, wishTurn = driving ? steering : 0f,
                    driftDirection = DriftDirection, extraSteer = IsWheelie ? stats._wheelieSteerAdd : 0f,
                    reversed = ControlsReversed
                }, stats, dt);
                steeringAngle = turn.currentTurn;
                turnLimit = turn.turnLimit;
                Speed = turn.currentSpeed;
                transform.rotation = turn.GroundRotation(transform.rotation, normal, stats, dt);
            }
            UsesRestoredAcceleration = driving && UsesRestoredSteering;
            if (UsesRestoredAcceleration)
            {
                AccelerationState = NativeKartAccelerationState.Step(Speed,
                    new NativeKartAccelerationInput
                    {
                        wishAccel = stats._autoAccel ? 0.5f : throttle,
                        braking = braking, onGrass = OnGrass || InWater,
                        groundAngle = GroundAngle, slopeLimit = body.slopeLimit, topSpeedModifier = 1f,
                        extraSpeed = VehicleExtraSpeed(),
                        driftDirection = DriftDirection, currentTurn = steeringAngle, turnLimit = turnLimit
                    }, stats, dt);
                Speed = AccelerationState.currentSpeed;
            }
            else if (Grounded)
            {
                Speed = Mathf.MoveTowards(Speed, 0f, 10f * dt);
            }

            if (UsesRestoredAirborne)
            {
                var turn = NativeKartSpin.Turn(driving ? steering : 0f, SpinRequested, AirTime, stats, dt);
                CurrentAirTurn = turn.bodyYaw;
                CurrentAirVelocityTurn = turn.velocityYaw;
                velocity = Quaternion.Euler(0f, CurrentAirVelocityTurn, 0f) * velocity;
                transform.Rotate(0f, CurrentAirTurn, 0f);
                verticalSpeed = NativeKartAirborne.StepGravity(verticalSpeed, stats._gravity, dt);
                int previousFlips = SpinState.flips;
                SpinState = NativeKartSpinState.Step(SpinState, CurrentAirTurn);
                Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
                if (Mathf.Abs(CurrentAirTurn) <= 0.1f && (horizontal).sqrMagnitude > 0.1f)
                    transform.rotation = Quaternion.Lerp(transform.rotation,
                        Quaternion.LookRotation(horizontal, transform.up), NativeKartAirborne.AlignmentFactor(stats, dt));
                if (particles && SpinState.flips > previousFlips) particles.Flip();
                AirTime += dt;
                Speed = NativeKartAirborne.ClampSpeed(Speed, stats);
            }

            if (pendingModuleSpeed > 0f)
            {
                Speed += pendingModuleSpeed;
                pendingModuleSpeed = 0f;
            }
            if (pendingModuleAirSpeed > 0f)
            {
                Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
                float gain = Mathf.Min(pendingModuleAirSpeed, Mathf.Max(0f, stats._topSpeed - horizontal.magnitude));
                if (horizontal.sqrMagnitude > 0f) velocity += horizontal.normalized * gain;
                pendingModuleAirSpeed = 0f;
            }

            if (Grounded)
            {
                driftRotation = NativeKartDriftState.VisualRotation(driftRotation, DriftDirection, stats, dt);
                verticalSpeed = NativeKartAccelerationState.GroundStickSpeed(
                    UsesRestoredAcceleration ? AccelerationState.speedBeforeCaps : Speed, transform.forward, stats);
                // Original grounded movement replaces the prior velocity after this step's turn.
                velocity = transform.forward.normalized * Speed;
            }
            JumpedThisStep = jump && NativeKartAirborne.RequestJump(false, Grounded, !driving, stats);
            if (JumpedThisStep)
                verticalSpeed = NativeKartAirborne.JumpVerticalSpeed(transform.forward, Speed, stats);
            jump = false;
            velocity.y = 0f;
            if (surfaceParticles) surfaceParticles.UpdateSpeed(velocity.magnitude, stats._topSpeed);
            CurrentVerticalSpeed = verticalSpeed;
            Vector3 previous = transform.position;
            validGround = collision.validGround = false;
            collision.DampenForce(dt, stats._forceFriction);
            hapticApproachVelocity = collision.MovementVelocity(velocity + Vector3.up * CurrentVerticalSpeed);
            NativeBridgeSweepTrace.BeforeMove(this, hapticApproachVelocity * dt, dt);
            var flags = body.Move(hapticApproachVelocity * dt);
            NativeBridgeSweepTrace.AfterMove(this, flags);
            LastCollisionFlags = flags;
            Grounded = NativeKartAirborne.ResolveGrounded(validGround, flags);
            if (!Grounded)
            {
                OnGrass = InWater = collision.onGrass = collision.groundWater = false;
                feedbackSurface = null;
            }
            if (Grounded) collision.ResetAirborneSideContact();
            if ((flags & CollisionFlags.Sides) != 0 && collision.HasSideContactNormal())
            {
                float speed = Speed;
                if (Grounded) collision.ResolveGroundedSideContact(transform.rotation, ref speed);
                else collision.ResolveAirborneSideContact(ref velocity, ref speed, stats._crashFactor, stats._crashMaxRate);
                Speed = speed;
            }
            if (Grounded && !wasGrounded)
            {
                LandedThisStep = true;
                if (AirTime > .12f && CurrentVerticalSpeed < -2f)
                    PublishHaptic(NativeHapticKind.Landing, Mathf.Clamp01(-CurrentVerticalSpeed / 30f));
                Landings++;
                LastLandingFlips = SpinState.flips;
                LastLandingBonus = LastLandingFlips * stats._flipSpeedBoost;
                Speed = NativeKartAirborne.LandingSpeed(Speed, velocity, LastLandingFlips,
                    stats._convertsAirVelocity, stats);
                Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
                if ((horizontal).magnitude > 1f)
                    transform.rotation = Quaternion.LookRotation(horizontal, transform.up);
                if (particles && LastLandingFlips > 0) particles.Land();
                AirTime = 0f;
                SpinState = default;
            }
            else if (!Grounded && wasGrounded && !JumpedThisStep)
            {
                LeftGroundThisStep = true;
                verticalSpeed = NativeKartAirborne.LeaveGroundVerticalSpeed(transform.forward, Speed, false);
            }
            if (surfaceParticles) surfaceParticles.UpdateSurface(Grounded, OnGrass || InWater, InWater, CurrentSurfaceKey);
            DriftState = DriftState.AdvanceTime(dt);
            Distance += Vector3.ProjectOnPlane(transform.position - previous, Vector3.up).magnitude;
            engine.pitch = 0.65f + Mathf.Clamp01(Mathf.Abs(Speed) / 42) * 1.05f;
            currentPosition = transform.position;
            currentRotation = transform.rotation;
            PhysicsVelocity = (currentPosition - previous) / dt;
            if (animationActions) animationActions.ObserveSimulation(animationDriftEntered,
                JumpedThisStep, LeftGroundThisStep, LandedThisStep, verticalSpeed);
            FixedStepCount++;
            if (!wasDrifting && IsDrifting) PublishHaptic(NativeHapticKind.DriftEnter);
            LastStepSideContactNormal = collision.hitNormal;
            collision.ClearSideContact();
        }

        float VehicleExtraSpeed()
        {
            if (IsWheelie)
                return Mathf.PingPong(WheelieBalance, 1f) * stats._wheelieSpeedAdd * 2f;
            return 0f;
        }

        void StepVehicleModule(float dt)
        {
            if (stats == null || stats._moduleKind == NativeVehicleModuleKind.None || HitFrozen) return;
            ModuleBoostRemaining = Mathf.Max(0f, ModuleBoostRemaining - dt);
            switch (stats._moduleKind)
            {
                case NativeVehicleModuleKind.BroomAirBoost:
                    if (Grounded)
                    {
                        ModuleCharge = Mathf.Min(stats._moduleLimit, ModuleCharge +
                            (OnGrass ? stats._moduleGrassRate : stats._moduleUpRate) * dt);
                        if (ModuleCharge >= stats._moduleLimit && !moduleReady)
                        {
                            SpawnModuleEffect(NativeVehicleModuleEffect.Ready);
                            moduleReady = true;
                            PublishHaptic(NativeHapticKind.Ready, context: "broom");
                        }
                    }
                    else if (moduleReady && ModuleCharge >= stats._moduleLimit)
                    {
                        pendingModuleAirSpeed += stats._moduleSpeedBoost;
                        PublishHaptic(NativeHapticKind.Boost, context: "broom");
                        SpawnModuleEffect(NativeVehicleModuleEffect.Use);
                        ModuleCharge = 0f;
                        ModuleBoostRemaining = stats._moduleActiveTime;
                        ModuleTriggerCount++;
                        moduleReady = false;
                    }
                    else ModuleCharge = Mathf.Max(0f, ModuleCharge - stats._moduleDecayRate * dt);
                    break;
                case NativeVehicleModuleKind.GenjiGrassBoost:
                    if (Grounded && OnGrass)
                    {
                        ModuleCharge = Mathf.Min(stats._moduleLimit, ModuleCharge +
                            (IsDrifting ? stats._moduleGrassDriftRate : stats._moduleGrassRate) * dt);
                        int totalSteps = stats._moduleSteps;
                        if (moduleSteps < totalSteps && ModuleCharge >=
                            (moduleSteps + 1f) / totalSteps * stats._moduleLimit)
                        {
                            SpawnModuleEffect(NativeVehicleModuleEffect.Ready);
                            moduleSteps++;
                            PublishHaptic(NativeHapticKind.ChargeStage, stage: moduleSteps, context: "genji");
                        }
                    }
                    else if (Grounded)
                    {
                        if (moduleSteps > 0)
                        {
                            PublishHaptic(NativeHapticKind.Boost, stage: moduleSteps, context: "genji");
                            if (stats._moduleSpeedBoosts != null && stats._moduleSpeedBoosts.Length >= moduleSteps)
                                pendingModuleSpeed += stats._moduleSpeedBoosts[moduleSteps - 1];
                            SpawnModuleEffect(moduleSteps >= stats._moduleSteps
                                ? NativeVehicleModuleEffect.UseMax : NativeVehicleModuleEffect.Use);
                            ModuleTriggerCount++;
                        }
                        ModuleCharge = 0f;
                        moduleSteps = 0;
                    }
                    break;
                case NativeVehicleModuleKind.ShoppingCartDriftCap:
                    ModuleCharge = Mathf.Clamp(ModuleCharge + (IsDrifting ? stats._moduleUpRate : -stats._moduleDecayRate) * dt,
                        0f, stats._moduleLimit);
                    UpdateHapticCartRisk();
                    if (ModuleCharge >= stats._moduleLimit)
                    {
                        PublishHaptic(NativeHapticKind.Overheat, context: "shoppingcart");
                        SpawnModuleEffect(NativeVehicleModuleEffect.Overheat);
                        ModuleCharge = 0f;
                        ModuleBoostRemaining = stats._moduleHurtTime;
                        ModuleTriggerCount++;
                        var hit = GetComponent<NativeKartHit>();
                        if (hit) hit.TakeDamage(stats._moduleHurtTime);
                    }
                    break;
            }
        }

        void SpawnModuleEffect(NativeVehicleModuleEffect effect)
        {
            var effects = GetComponent<NativeVehicleModuleEffects>();
            if (!effects) throw new System.InvalidOperationException("Selected module world FX component missing");
            effects.Spawn(effect);
        }

        void ResetRenderPose()
        {
            previousPosition = currentPosition = RenderPosition = transform.position;
            previousRotation = currentRotation = RenderRotation = transform.rotation;
            if (visual) visual.SetPositionAndRotation(RenderPosition + RenderRotation * modelOffset, RenderRotation);
            if (driftPivot) driftPivot.localRotation = driftRotation;
        }

        public void PrepareRenderPose(bool interpolate)
        {
            // Fixed-step formula fixtures retain interpolation; normal Update presents the current pose.
            float alpha = interpolate && UseFixedVerificationClock ? Mathf.Clamp01((float)((Time.timeAsDouble - Time.fixedTimeAsDouble) / Time.fixedDeltaTime)) : 1;
            RenderPosition = Vector3.Lerp(previousPosition, currentPosition, alpha);
            RenderRotation = Quaternion.Slerp(previousRotation, currentRotation, alpha);
            if (visual) visual.SetPositionAndRotation(RenderPosition + RenderRotation * modelOffset,
                RenderRotation);
            if (driftPivot) driftPivot.localRotation = Quaternion.Slerp(previousDriftRotation, driftRotation, alpha);
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            float hapticImpactSpeed = -Vector3.Dot(hapticApproachVelocity, hit.normal);
            if (Vector3.Angle(Vector3.up, hit.normal) > body.slopeLimit &&
                hapticContacts.Touch(hit.collider.GetInstanceID(), Time.timeAsDouble, hapticImpactSpeed > 4f))
                PublishHaptic(NativeHapticKind.Impact, Mathf.Clamp01(hapticImpactSpeed / 65f), source: hit.collider.GetInstanceID());
            CollisionContactCount++;
            var other = hit.collider.GetComponent<NativeKartMotor>();
            bool player = hit.collider.CompareTag("Player");
            if (player && other) VehicleContactCount++;
            var otherState = other ? other.collision : default;
            collision.ApplyContact(hit.normal, hit.point, body.slopeLimit, transform.right,
                hit.collider.sharedMaterial, hit.collider.CompareTag("Grass"), hit.collider.CompareTag("Water"),
                player, stats._crashFactor, stats._crashMaxRate, ref otherState, other != null);
            if (other) other.collision = otherState;
            validGround = collision.validGround;
            normal = collision.upNormal;
            GroundAngle = collision.groundAngle;
            OnGrass = collision.onGrass;
            InWater = collision.groundWater;
            if (Vector3.Angle(Vector3.up, hit.normal) <= body.slopeLimit)
            {
                particleSurfaceKey = null;
                foreach (var surface in feedbackSurfaces)
                    if (surface.material && surface.material == collision.surfaceMaterial)
                    { particleSurfaceKey = surface.key; break; }
            }
            feedbackSurface = null;
            if (OnGrass)
                foreach (var surface in feedbackSurfaces)
                    if (surface.material && surface.material == collision.surfaceMaterial)
                    { feedbackSurface = surface; break; }
        }
    }
}
