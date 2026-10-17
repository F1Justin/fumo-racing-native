using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public enum NativeItemActorMode { Projectile, Frog, Shield, Melee, Effect }

    [DefaultExecutionOrder(-40)]
    public sealed partial class NativeItemActor : MonoBehaviour
    {
        public string ItemId => entry == null ? null : entry.id;
        public NativeKartMotor Owner => owner;
        public bool Carried { get; private set; }
        public bool Finished { get; private set; }
        public Vector3 Velocity => velocity;
        public NativeItemActorMode Mode { get; private set; }
        public bool OnRadar => projectile != null && projectile.onRadar;
        public Sprite RadarIcon => projectile == null ? null : projectile.radarIcon;
        public NativeKartMotor RadarTarget => radarTarget;
        internal GameObject NetworkVisualPrefab { get; set; }
        internal int SpawnFrame { get; private set; }
        internal bool CanHitOwner => !Carried && Mode == NativeItemActorMode.Projectile &&
            projectile != null && projectile.collidesOwner;
        internal GameObject FrogExplosion => frog == null ? null : frog.explosionPrefab;
        public event Action<NativeItemActor> Destroyed;

        NativeItemWorld world;
        NativeItemCatalog.Entry entry;
        NativeItemCatalog.ProjectileSpec projectile;
        NativeItemCatalog.ShieldMotionSpec shieldMotion;
        NativeItemCatalog.FrogSpec frog;
        NativeKartMotor owner;
        NativeKartMotor target;
        NativeKartMotor radarTarget;
        Transform followAnchor;
        CharacterController body;
        Vector3 sightCenter;
        Vector3 sightSize;
        Vector3 velocity;
        Vector3 previousPosition;
        Vector3 wallNormal;
        float remaining;
        float age;
        float jumpClock;
        float targetClock;
        float ofudaDuration;
        float orbitAngle;
        float radius;
        int jumps;
        bool wallHit;
        bool ofudaLocked;
        bool initialized;

        internal void Init(NativeItemWorld itemWorld, NativeItemCatalog.Entry itemEntry,
            NativeKartMotor source, NativeItemActorMode mode, NativeItemCatalog.ProjectileSpec projectileSpec,
            Vector3 initialVelocity, bool carried, int ordinal = 0, NativeItemCatalog.FrogSpec frogSpec = null)
        {
            world = itemWorld;
            entry = itemEntry;
            owner = source;
            Mode = mode;
            projectile = projectileSpec;
            frog = frogSpec ?? itemEntry.frog;
            shieldMotion = mode == NativeItemActorMode.Shield && itemEntry.shieldMotions != null
                && ordinal < itemEntry.shieldMotions.Length ? itemEntry.shieldMotions[ordinal] : null;
            velocity = initialVelocity;
            Carried = carried;
            SpawnFrame = Time.frameCount;
            remaining = mode == NativeItemActorMode.Projectile
                ? Mathf.Max(0.1f, projectileSpec == null ? 6f : projectileSpec.duration)
                : mode == NativeItemActorMode.Frog ? Mathf.Max(0.1f, frog == null ? 12f : frog.duration)
                : mode == NativeItemActorMode.Effect ? Mathf.Max(0.1f, itemEntry.duration)
                : Mathf.Max(0.1f, itemEntry.duration);
            body = GetComponent<CharacterController>();
            if (mode == NativeItemActorMode.Projectile && !body && projectileSpec != null && projectileSpec.hasCharacterController)
                body = gameObject.AddComponent<CharacterController>();
            if (mode == NativeItemActorMode.Frog && !body) body = gameObject.AddComponent<CharacterController>();
            if (mode == NativeItemActorMode.Frog && entry.id == "Bnuuy")
            {
                var sight = GetComponent<BoxCollider>();
                if (sight && sight.isTrigger)
                {
                    sightCenter = sight.center;
                    sightSize = sight.size;
                    sight.enabled = false;
                }
            }
            if (body) radius = Mathf.Max(0.5f, body.radius);
            else
            {
                var collider = GetComponentInChildren<Collider>();
                radius = collider ? Mathf.Max(0.5f, collider.bounds.extents.magnitude * 0.5f) : 1.25f;
            }
            if (mode == NativeItemActorMode.Shield) orbitAngle = ordinal * 90f;
            if (mode == NativeItemActorMode.Frog) jumpClock = frog == null ? 1f : frog.jumpWait;
            if (mode == NativeItemActorMode.Projectile && world && world.IsHoming(itemEntry))
                InitializeHomingTarget();
            previousPosition = transform.position;
            NativeAudioEmitter.ReapplyHierarchy(gameObject);
            foreach (var listener in GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            initialized = true;
        }

        public void AttachTo(Transform parent)
        {
            if (!parent || Finished) return;
            Carried = true;
            NotifyRadarTarget(null);
            transform.SetParent(parent, true);
            if (body) body.enabled = false;
        }

        internal void SetFollowAnchor(Transform anchor)
        {
            if (!anchor || Finished) return;
            followAnchor = anchor;
            transform.SetParent(anchor, true);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        public void Launch(Vector3 position, Quaternion rotation, Vector3 initialVelocity)
        {
            if (Finished) return;
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(position, rotation);
            Carried = false;
            SpawnFrame = Time.frameCount;
            velocity = initialVelocity;
            age = 0f;
            remaining = Mode == NativeItemActorMode.Frog
                ? Mathf.Max(0.1f, frog == null ? 12f : frog.duration)
                : projectile == null ? 6f : Mathf.Max(0.1f, projectile.duration);
            previousPosition = position;
            if (body) body.enabled = true;
            if (Mode == NativeItemActorMode.Frog) jumpClock = frog == null ? 1f : frog.jumpWait;
            if (world && world.IsHoming(entry)) InitializeHomingTarget();
        }

        void InitializeHomingTarget()
        {
            SetHomingTarget(world.SelectTarget(owner, false));
            // Ofuda overrides the physical target after the base Init notification.
            if (projectile != null && projectile.ofudaBehaviour)
                target = world.SelectOfudaTarget(owner, projectile.seeksFirst);
        }

        void SetHomingTarget(NativeKartMotor next)
        {
            target = next;
            NotifyRadarTarget(!Carried && OnRadar ? next : null);
        }

        void NotifyRadarTarget(NativeKartMotor next)
        {
            if (radarTarget == next) return;
            if (radarTarget) radarTarget.GetComponent<NativeItemRadarSensor>()?.OnIgnored(this);
            radarTarget = next;
            if (!next) return;
            var sensor = next.GetComponent<NativeItemRadarSensor>();
            if (!sensor) sensor = next.gameObject.AddComponent<NativeItemRadarSensor>();
            sensor.OnTarget(this);
        }

        void LateUpdate()
        {
            if (!initialized || Finished || Time.timeScale <= 0f) return;
            if (Carried) { world.CheckCarriedContact(this, radius); return; }
            float dt = Time.deltaTime;
            age += dt;
            remaining -= dt;
            if (remaining <= 0f) { Expire(); return; }
            if (Mode == NativeItemActorMode.Shield) { OrbitOwner(dt); return; }
            if (Mode == NativeItemActorMode.Melee) { FollowOwner(); world.CheckHits(this, previousPosition, transform.position, radius); return; }
            if (Mode == NativeItemActorMode.Effect) return;
            previousPosition = transform.position;
            if (Mode == NativeItemActorMode.Frog) StepFrog(dt);
            else StepProjectile(dt);
            if (!Finished && !ofudaLocked)
            {
                world.CheckProjectileInterceptions(this, previousPosition, transform.position, radius);
                if (!Finished) world.CheckHits(this, previousPosition, transform.position, radius);
            }
        }

        void StepProjectile(float dt)
        {
            if (world.IsHoming(entry)) StepHoming(dt);
            if ((entry.id == "Ofuda" || entry.id == "FantasySeal") && target && !ofudaLocked &&
                Vector3.Distance(transform.position, target.transform.position) <= Mathf.Max(4f, projectile.homingDistance))
            {
                ofudaLocked = true;
                targetClock = ofudaDuration = Mathf.Max(0.05f, projectile.timeBeforeExplosion);
                if (body) body.enabled = false;
                transform.position = target.transform.position + Vector3.up * projectile.flyHeight;
            }
            if (ofudaLocked)
            {
                targetClock -= dt;
                if (target)
                {
                    float progress = 1f - Mathf.Clamp01(targetClock / ofudaDuration);
                    transform.position = target.transform.position + Vector3.up *
                        Mathf.Lerp(projectile.flyHeight, projectile.targetHeight, progress);
                }
                if (targetClock <= 0f)
                {
                    if (target) world.ResolveHit(this, target, projectile.damagedDuration);
                    world.SpawnEffect(projectile.explosionPrefab, transform.position, Quaternion.identity, 2f, owner);
                    DestroyNow();
                }
                return;
            }
            if (body && body.enabled)
            {
                if (body.isGrounded) velocity.y = -(projectile == null ? 45f : projectile.stickForce);
                else velocity.y -= (projectile == null ? 25f : projectile.gravity) * dt;
                body.Move(velocity * dt);
                if (wallHit)
                {
                    Vector3 normal = wallNormal.normalized;
                    if (normal.sqrMagnitude > 0.001f)
                        velocity = Vector3.Reflect(velocity, normal) * (projectile == null ? 1f : projectile.bounce);
                    if (projectile != null && projectile.destroyOnImpact) { DestroyNow(); return; }
                }
                wallHit = false;
            }
            else
            {
                velocity.y -= projectile == null ? 0f : projectile.gravity * dt;
                transform.position += velocity * dt;
            }
        }

        void StepHoming(float dt)
        {
            if (projectile == null) return;
            if (!target) return;
            float distance = target ? Vector3.Distance(transform.position, target.transform.position) : float.MaxValue;
            bool nearTarget = target && distance < projectile.homingDistance;
            Vector3 goal = nearTarget ? target.transform.position : world.RouteTarget(transform.position);
            float intensity = nearTarget ? projectile.homingIntensity : projectile.waypointIntensity;
            Vector3 toward = goal - transform.position;
            if (toward.sqrMagnitude > 0.001f)
            {
                float force = Mathf.Max(velocity.magnitude, 1f);
                velocity = Vector3.Lerp(velocity, toward.normalized * force, Mathf.Clamp01(intensity * dt));
                if (velocity.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        Quaternion.LookRotation(velocity.normalized), Mathf.Max(0f, projectile.turnSpeed) * dt);
            }
            if (nearTarget && !projectile.ofudaBehaviour)
            {
                var health = target.GetComponent<NativeKartHit>();
                if (health && health.Invulnerable) SetHomingTarget(world.SelectHomingRetarget(owner, target));
            }
        }

        void StepFrog(float dt)
        {
            if (entry.id == "Bnuuy" && (!target || world.IsFinished(target))) AcquireBunnyTarget();
            if (body && body.enabled)
            {
                if (body.isGrounded)
                {
                    velocity.y = -Mathf.Max(1f, frog == null ? 10f : frog.gravity);
                    if (frog != null && (frog.jumpHeight > 0f || frog.runJumpHeight > 0f))
                    {
                        jumpClock -= dt;
                        if (jumpClock <= 0f)
                        {
                            bool run = entry.id == "Bnuuy" && target;
                            velocity.y = run && frog.runJumpHeight > 0f ? frog.runJumpHeight : frog.jumpHeight;
                            Vector3 direction = run ? (target.transform.position - transform.position).normalized : transform.forward;
                            float forward = run && frog.runJumpForward > 0f ? frog.runJumpForward : frog.jumpForward;
                            velocity = Vector3.ProjectOnPlane(direction, Vector3.up).normalized * forward + Vector3.up * velocity.y;
                            jumps++;
                            if (jumps >= Mathf.Max(1, frog.jumpsToChangeDirection))
                            {
                                jumps = 0;
                                if (UnityEngine.Random.value * 100f <= frog.changeDirectionChance)
                                    transform.Rotate(0f, UnityEngine.Random.Range(-90f, 90f), 0f);
                            }
                            jumpClock = run && frog.runJumpWait > 0f ? frog.runJumpWait : Mathf.Max(0.05f, frog.jumpWait);
                        }
                    }
                }
                else velocity.y -= (frog == null ? 20f : frog.gravity) * dt;
                body.Move(velocity * dt);
            }
            else transform.position += velocity * dt;
        }

        void AcquireBunnyTarget()
        {
            if (world.racers == null || sightSize.sqrMagnitude <= 0f) return;
            foreach (var racer in world.racers)
            {
                if (!racer || racer == owner || world.IsFinished(racer)) continue;
                Vector3 local = transform.InverseTransformPoint(racer.transform.position + Vector3.up);
                Vector3 delta = local - sightCenter;
                if (Mathf.Abs(delta.x) <= sightSize.x * 0.5f &&
                    Mathf.Abs(delta.y) <= sightSize.y * 0.5f &&
                    Mathf.Abs(delta.z) <= sightSize.z * 0.5f)
                {
                    target = racer;
                    return;
                }
            }
        }

        void OrbitOwner(float dt)
        {
            if (!owner) { DestroyNow(); return; }
            previousPosition = transform.position;
            if (shieldMotion != null)
            {
                float distance = Mathf.Abs(Mathf.Sin(age * Mathf.PI * 2f * shieldMotion.distanceSpeed)
                    * shieldMotion.distanceMax + shieldMotion.distanceMin);
                orbitAngle = shieldMotion.rotation *
                    (age * shieldMotion.rotationSpeed + shieldMotion.rotOffset);
                transform.position = owner.transform.position + owner.transform.rotation *
                    (Quaternion.Euler(0f, orbitAngle, 0f) * Vector3.forward * distance);
                transform.rotation = owner.transform.rotation * Quaternion.Euler(0f, orbitAngle, 0f);
            }
            else
            {
                orbitAngle += 150f * dt;
                transform.position = owner.transform.position + Quaternion.Euler(0f, orbitAngle, 0f) * Vector3.forward * 3f;
            }
            world.CheckShieldCollisions(this, previousPosition, transform.position, radius);
            if (!Finished) world.CheckHits(this, previousPosition, transform.position, radius);
        }

        void FollowOwner()
        {
            if (!owner) { DestroyNow(); return; }
            previousPosition = transform.position;
            if (followAnchor)
            {
                transform.position = followAnchor.position;
                transform.rotation = followAnchor.rotation;
            }
            else
            {
                transform.position = owner.transform.position + owner.transform.forward * 2.5f + Vector3.up;
                transform.rotation = owner.transform.rotation;
            }
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!initialized || Finished || hit == null || hit.collider == null || body == null) return;
            if (Vector3.Angle(hit.normal, Vector3.up) > body.slopeLimit)
            {
                wallNormal = hit.normal;
                wallHit = true;
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (!initialized || Finished || Carried || !world || !other) return;
            var motor = other.GetComponentInParent<NativeKartMotor>();
            if (motor) world.ResolveHit(this, motor, 0f, "OnTriggerEnter");
        }

        internal void Expire()
        {
            if (!Finished) NativeTelemetry.Count("item.actor.expired");
            if (Mode == NativeItemActorMode.Frog && FrogExplosion)
                world.SpawnEffect(FrogExplosion, transform.position, Quaternion.identity, 2f, owner);
            DestroyNow();
        }

        public void DestroyNow()
        {
            if (Finished) return;
            ReleaseInsanityLease();
            NotifyRadarTarget(null);
            Finished = true;
            Destroyed?.Invoke(this);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            ReleaseInsanityLease();
            NotifyRadarTarget(null);
            if (!Finished)
            {
                Finished = true;
                Destroyed?.Invoke(this);
            }
        }
    }
}
