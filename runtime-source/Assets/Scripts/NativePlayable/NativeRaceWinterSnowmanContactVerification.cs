using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace Fumo.NativePlayable
{
    public sealed class NativeSnowmanContactProbe : MonoBehaviour
    {
        public CharacterController controller;
        public Rigidbody body;
        public Vector3 velocity;
        public bool moving;
        public int bodyMoveTicks;
        public Vector3 lastBodyTarget;
        public Action observeFrame;
        void FixedUpdate()
        {
            if (!moving) return;
            if (controller && controller.enabled) controller.Move(velocity * Time.fixedDeltaTime);
            else if (body) { lastBodyTarget = body.position + velocity * Time.fixedDeltaTime; bodyMoveTicks++; body.MovePosition(lastBodyTarget); }
        }
        void Update() => observeFrame?.Invoke();
    }

    public sealed partial class NativeRace
    {
        [Serializable] sealed class ContactReceipt
        {
            public string domain, phase, receiverSource, receiverPath, actorPath, itemId, prefabName, bridgeSource, bridgeSiblingPath, tag;
            public int frame, sceneHandle, receiverId, actorId, colliderId, layer, receiverLayer;
            public float gameTime, realTime, threshold, projectedSquared, duration;
            public Vector3 velocity, forward;
            public bool alive, after, useDuration, trigger, enabled, active, kinematic, ignoredLayers, ignoredPair;
        }
        [Serializable] sealed class ContactPausedMotion
        {
            public string source, path;
            public Vector3 rotationPerSecond;
            public bool overrideLocalPosition;
            public float bobAmplitude, bobRate;
        }
        [Serializable] sealed class ContactItemAttempt
        {
            public string phase, itemId, prefabName, bridgeSource, bridgeSiblingPath;
            public string[] pausedAncestorMotionSources;
            public ContactPausedMotion[] pausedAncestorMotion;
            public int startFrame, endFrame, actorId, bodyId, colliderId, bodyMoveTicks;
            public float startTime, endTime, closestBoundsCenterDistance;
            public Vector3 bodyStart, bodyEnd, hurtCenterStart, hurtCenterEnd, receiverCenter, lastRequestedBodyTarget, hurtLocalPosition, hurtLossyScale;
            public float originalSphereRadius;
            public bool actorActive, hurtActive, bridgeActive, detectCollisions, correctAttachedRigidbody, bodyAwakeAtStart, receiverActive;
        }
        [Serializable] sealed class ContactEventReceipt
        {
            public string receiverSource, path, eventName;
            public int frame, destroyed, respawned;
            public float gameTime, realTime, ageSinceLastDamage;
            public bool alive;
        }
        [Serializable] sealed class ContactOtherSource
        {
            public string path;
            public int sourceId, timeSamples;
            public bool enabled, active, playing, isVirtual;
        }
        [Serializable] sealed class ContactAudioFrame
        {
            public int frame, sourceTimeSamples, otherPlayingCount, listenerId, activeListenerCount;
            public double dspTime;
            public float realTime, sourcePeak, listenerPeak, distance;
            public bool sourcePlaying, sourceVirtual, listenerActiveEnabled;
            public Vector3 sourcePosition, listenerPosition;
            public ContactOtherSource[] otherOriginalSources;
        }
        [Serializable] sealed class ContactAudioWindow
        {
            public string path, receiverSource, eventName, clip, mixer;
            public int sourceId;
            public float started, sourcePeak, listenerPeak, clipSeconds, renderedClipSeconds, bufferLatencySeconds, plannedWindowSeconds, realDeadline;
            public int sampleReads, pairedNonzeroFrames, sourcePriority, sourceRolloff;
            public float sourceMinDistance, sourceMaxDistance, sourceSpatialBlend;
            public bool derivedClockWindowCompleted;
            public ContactAudioFrame firstRead, lastRead, pairedRead;
            public bool enabled, active, playing, firstDeath, complete;
            public NativeAudioObservation.Snapshot audioStart, audioEnd;
            [NonSerialized] public AudioSource source;
        }
        [Serializable] sealed class ContactListenerRestore
        {
            public string path;
            public int instanceId;
            public bool originalEnabled, restoredEnabled, restored;
        }
        [Serializable] sealed class ContactPhysicsReport
        {
            public bool passed, accepted, reportSaved, ordinaryAudibleAccepted, injectedActorMovement = true, actualPhysicsEvent;
            public bool perOriginalCallTraceOpen = true, naturalDrivingAccepted, isolatedListenerOutput, nativeActorMotionDisabled = true, injectedTagAndPose = true;
            public string listenerDSPAttribution = "dedicated near-owner fixture listener; paired source/listener nonzero in the same frame while other seven original Snowman sources are all not playing";
            public bool injectedListenerGeometry = true, serialOwnerLifecycle = true;
            public string error, baselineSHA256, assemblySHA256, outputMode, fixtureType = "real-physics-contact-with-injected-actor-movement";
            public string[] commandLine;
            public int engineErrorCount, entryEngineErrorCount;
            public float realtimeSeconds;
            public NativeAudioObservation.Snapshot audioEntry, audioEnd;
            public List<WinterCheck> checks = new List<WinterCheck>();
            public List<ContactReceipt> contacts = new List<ContactReceipt>();
            public List<ContactItemAttempt> itemAttempts = new List<ContactItemAttempt>();
            public bool injectedAncestorMotionPause = true;
            public List<ContactEventReceipt> events = new List<ContactEventReceipt>();
            public List<ContactAudioWindow> audio = new List<ContactAudioWindow>();
            public List<ContactListenerRestore> originalListeners = new List<ContactListenerRestore>();
            public WinterCheck[] sourceFieldChecks;
            public WinterAudioSample[] sourceAudioFields;
        }
        sealed class ContactOwner
        {
            public NativeSnowmanDestructable snow;
            public WinterSnowmanRef baseline;
            public CapsuleCollider collider;
            public int initialDeaths, initialRespawns, observedDeaths, observedRespawns;
            public float firstDamage, lastDamage;
            public bool sampledBeforeDeadline, sampledOldDeadline;
            public UnityAction death, respawn;
        }
        ContactPhysicsReport contactReport;
        readonly List<ContactOwner> contactOwners = new List<ContactOwner>();
        readonly List<NativeItemActor> contactActors = new List<NativeItemActor>();
        readonly HashSet<AudioSource> contactSnowAudio = new HashSet<AudioSource>();
        NativeSnowmanContactProbe contactProbe;
        CharacterController contactController;
        AudioListener contactFixtureListener;
        readonly Dictionary<AudioListener, bool> contactOriginalListeners = new Dictionary<AudioListener, bool>();
        string contactPhase;
        bool contactOrdinary;
        string contactObserverError;
        void ContactRequire(bool passed, string id, string detail = "")
        { contactReport.checks.Add(new WinterCheck { id = id, passed = passed, detail = detail, evidenceKind = "injected-actor-movement-real-physics-event" }); }
        void ContactNeed(bool passed, string id, string detail = "")
        { ContactRequire(passed, id, detail); if (!passed) throw new InvalidOperationException(id + ": " + detail); }
        void ContactGuard(Action action)
        {
            try { action(); }
            catch (Exception error) { ContactError(error); }
        }
        void ContactError(Exception error)
        { contactReport.error = (contactReport.error == null ? "" : contactReport.error + "\n") + error; }
        void ContactLog(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) contactReport.engineErrorCount++; }
        void ContactStay(NativeSnowmanDestructable snow, Collider other)
        {
            if (other != contactController || !contactOwners.Any(o => o.snow == snow)) return;
            var velocity = contactController.velocity;
            float dot = Vector3.Dot(velocity.normalized, other.transform.forward);
            contactReport.contacts.Add(new ContactReceipt { domain = "CharacterController.OnTriggerStay", phase = contactPhase,
                receiverSource = snow.sourceComponentId, receiverPath = WinterPath(snow.transform), actorPath = WinterPath(other.transform),
                frame = Time.frameCount, sceneHandle = snow.gameObject.scene.handle, receiverId = snow.GetInstanceID(), actorId = other.gameObject.GetInstanceID(),
                colliderId = other.GetInstanceID(), gameTime = Time.time, realTime = Time.realtimeSinceStartup, alive = snow.IsAlive,
                tag = other.tag, layer = other.gameObject.layer, receiverLayer = snow.gameObject.layer, threshold = snow.speedRequired * snow.speedRequired,
                velocity = velocity, forward = other.transform.forward, projectedSquared = (velocity * dot).sqrMagnitude,
                enabled = other.enabled, active = other.gameObject.activeInHierarchy,
                trigger = other.isTrigger, ignoredPair = Physics.GetIgnoreCollision(other, snow.GetComponent<CapsuleCollider>()),
                ignoredLayers = Physics.GetIgnoreLayerCollision(other.gameObject.layer, snow.gameObject.layer) });
        }
        void ContactEnter(NativeSnowmanDamageTrigger bridge, Collider other, bool after)
        {
            var snow = other.GetComponent<NativeSnowmanDestructable>();
            var actor = bridge.GetComponentInParent<NativeItemActor>();
            if (!snow || !actor || !contactActors.Contains(actor) || !contactOwners.Any(o => o.snow == snow)) return;
            var collider = bridge.GetComponent<Collider>(); var body = bridge.GetComponent<Rigidbody>();
            contactReport.contacts.Add(new ContactReceipt { domain = "original-prefab-TriggerHurt.OnTriggerEnter", phase = contactPhase,
                receiverSource = snow.sourceComponentId, receiverPath = WinterPath(snow.transform), actorPath = WinterPath(actor.transform), itemId = actor.ItemId, prefabName = itemWorld.catalog.Find(actor.ItemId).throwPrefab.name,
                bridgeSource = bridge.sourceComponentId, bridgeSiblingPath = bridge.sourceSiblingPath, frame = Time.frameCount,
                sceneHandle = snow.gameObject.scene.handle, receiverId = snow.GetInstanceID(), actorId = actor.GetInstanceID(), colliderId = collider.GetInstanceID(),
                gameTime = Time.time, realTime = Time.realtimeSinceStartup, alive = snow.IsAlive, after = after, duration = bridge.damageDuration,
                useDuration = bridge.useDamageDuration, layer = collider.gameObject.layer, receiverLayer = other.gameObject.layer,
                trigger = collider.isTrigger, enabled = collider.enabled, active = collider.gameObject.activeInHierarchy, kinematic = body && body.isKinematic,
                ignoredLayers = Physics.GetIgnoreLayerCollision(collider.gameObject.layer, other.gameObject.layer), ignoredPair = Physics.GetIgnoreCollision(collider, other) });
        }
        void ContactEvent(ContactOwner owner, bool death)
        {
            if (death)
            {
                owner.observedDeaths++; if (owner.observedDeaths == 1) owner.firstDamage = Time.time;
                owner.lastDamage = Time.time; owner.sampledBeforeDeadline = false;
            }
            else owner.observedRespawns++;
            var snow = owner.snow;
            contactReport.events.Add(new ContactEventReceipt { receiverSource = snow.sourceComponentId, path = WinterPath(snow.transform),
                eventName = death ? "onDestructed-tail" : "onRespawn-tail", frame = Time.frameCount, gameTime = Time.time, realTime = Time.realtimeSinceStartup,
                destroyed = snow.DestructionCount, respawned = snow.RespawnCount, alive = snow.IsAlive, ageSinceLastDamage = Time.time - owner.lastDamage });
            ContactRequire(snow.IsAlive == !death, "event-tail-alive." + owner.baseline.sourceId + "." + (death ? "death" : "respawn"));
            if (!death) ContactRequire(Time.time - owner.lastDamage >= 9.95f && owner.observedRespawns == 1,
                "real-Invoke10-respawn." + owner.baseline.sourceId, "elapsedScaled=" + (Time.time - owner.lastDamage));
            foreach (var call in owner.baseline.calls.Where(c => c.eventName == (death ? "onDestructed" : "onRespawn")))
            {
                var target = WinterTarget(WinterFind(call.targetPath), call.targetType);
                if (target is GameObject node) ContactRequire(node.activeSelf == call.argument, "event-tail-node." + call.sourceTargetId);
                if (target is ParticleSystem particles) ContactRequire(particles.isPlaying, "event-tail-particle." + call.sourceTargetId);
            }
            foreach (var audio in snow.GetComponentsInChildren<AudioSource>(true).Where(a => a.gameObject.activeInHierarchy && a.isPlaying))
            {
                if (death && !WinterPath(audio.transform).Contains("/FX/")) continue;
                if (!death && !WinterPath(audio.transform).EndsWith("/RespawnAudio", StringComparison.Ordinal)) continue;
                var snapshot = NativeAudioObservation.Capture(contactReport.outputMode);
                float bufferLatency = snapshot.outputSampleRate > 0 ? (float)snapshot.dspBufferFrames * snapshot.dspBufferCount / snapshot.outputSampleRate : .1f;
                float clipSeconds = audio.clip ? (float)audio.clip.samples / audio.clip.frequency : 0;
                float renderedSeconds = clipSeconds / Mathf.Max(.01f, Mathf.Abs(audio.pitch));
                float plannedSeconds = Mathf.Max(.25f, renderedSeconds + 2 * bufferLatency);
                var window = new ContactAudioWindow { path = WinterPath(audio.transform), receiverSource = snow.sourceComponentId,
                    eventName = death ? "death" : "respawn", clip = audio.clip ? audio.clip.name : "", mixer = audio.outputAudioMixerGroup ? audio.outputAudioMixerGroup.name : "",
                    sourceId = audio.GetInstanceID(), started = Time.realtimeSinceStartup, enabled = audio.enabled, active = audio.gameObject.activeInHierarchy,
                    playing = audio.isPlaying, firstDeath = death && owner.observedDeaths == 1, source = audio, audioStart = snapshot,
                    clipSeconds = clipSeconds, renderedClipSeconds = renderedSeconds, bufferLatencySeconds = bufferLatency, plannedWindowSeconds = plannedSeconds,
                    sourceMinDistance = audio.minDistance, sourceMaxDistance = audio.maxDistance, sourceSpatialBlend = audio.spatialBlend,
                    sourcePriority = audio.priority, sourceRolloff = (int)audio.rolloffMode,
                    realDeadline = Time.realtimeSinceStartup + plannedSeconds + Mathf.Max(1f, bufferLatency * 4) };
                contactReport.audio.Add(window);
                ContactRequire(clipSeconds > 0 && plannedSeconds <= 10f, "bounded-original-clip-DSP-window." + window.path + "." + window.eventName);
                window.realDeadline = Mathf.Min(window.realDeadline, window.started + 12f);
            }
        }
        void ContactObserveFrame()
        {
            try
            {
                if (contactOrdinary)
                    foreach (var audio in FindObjectsOfType<AudioSource>(true)) if (!contactSnowAudio.Contains(audio)) audio.Stop();
                foreach (var owner in contactOwners.Where(o => o.observedDeaths > 0 && o.observedRespawns == 0))
                {
                    float age = Time.time - owner.lastDamage;
                    if (!owner.sampledBeforeDeadline && age >= 9.8f)
                    {
                        owner.sampledBeforeDeadline = true;
                        ContactRequire(age < 10f && !owner.snow.IsAlive && owner.snow.RespawnCount == owner.initialRespawns,
                            "last-contact-plus9.8-still-dead." + owner.baseline.sourceId, "actualAge=" + age);
                    }
                    if (owner.observedDeaths > 1 && !owner.sampledOldDeadline && Time.time >= owner.firstDamage + 10f && age < 9.8f)
                    { owner.sampledOldDeadline = true; ContactRequire(!owner.snow.IsAlive, "old-Invoke-cancelled-after-real-repeat." + owner.baseline.sourceId); }
                }
                var buffer = new float[256];
                foreach (var window in contactReport.audio.Where(w => !w.complete))
                {
                    float sourcePeak = 0, listenerPeak = 0;
                    if (window.source) { window.source.GetOutputData(buffer, 0); foreach (float sample in buffer) sourcePeak = Mathf.Max(sourcePeak, Mathf.Abs(sample)); }
                    AudioListener.GetOutputData(buffer, 0); foreach (float sample in buffer) listenerPeak = Mathf.Max(listenerPeak, Mathf.Abs(sample));
                    var otherSources = contactSnowAudio.Where(a => a && a != window.source).Select(a => new ContactOtherSource {
                        path = WinterPath(a.transform), sourceId = a.GetInstanceID(), timeSamples = a.timeSamples,
                        enabled = a.enabled, active = a.gameObject.activeInHierarchy, playing = a.isPlaying, isVirtual = a.isVirtual }).ToArray();
                    var frame = new ContactAudioFrame { frame = Time.frameCount, dspTime = AudioSettings.dspTime, realTime = Time.realtimeSinceStartup,
                        sourceTimeSamples = window.source ? window.source.timeSamples : 0, sourcePlaying = window.source && window.source.isPlaying,
                        sourceVirtual = window.source && window.source.isVirtual, sourcePeak = sourcePeak, listenerPeak = listenerPeak,
                        sourcePosition = window.source ? window.source.transform.position : Vector3.zero,
                        listenerPosition = contactFixtureListener ? contactFixtureListener.transform.position : Vector3.zero,
                        otherPlayingCount = otherSources.Count(a => a.playing), otherOriginalSources = otherSources,
                        listenerId = contactFixtureListener ? contactFixtureListener.GetInstanceID() : 0,
                        listenerActiveEnabled = contactFixtureListener && contactFixtureListener.isActiveAndEnabled,
                        activeListenerCount = FindObjectsOfType<AudioListener>(true).Count(a => a.isActiveAndEnabled) };
                    frame.distance = Vector3.Distance(frame.sourcePosition, frame.listenerPosition);
                    window.sampleReads++; if (window.firstRead == null) window.firstRead = frame; window.lastRead = frame;
                    window.sourcePeak = Mathf.Max(window.sourcePeak, sourcePeak); window.listenerPeak = Mathf.Max(window.listenerPeak, listenerPeak);
                    bool sourcePositive = sourcePeak > 0 && !float.IsNaN(sourcePeak) && !float.IsInfinity(sourcePeak);
                    bool listenerPositive = listenerPeak > 0 && !float.IsNaN(listenerPeak) && !float.IsInfinity(listenerPeak);
                    if (sourcePositive && listenerPositive && frame.otherPlayingCount == 0 && otherSources.Length == 7
                        && frame.sourcePlaying && !frame.sourceVirtual && frame.sourceTimeSamples > 0 && frame.distance < window.source.minDistance
                        && frame.listenerActiveEnabled && frame.activeListenerCount == 1)
                    { window.pairedNonzeroFrames++; if (window.pairedRead == null) window.pairedRead = frame; }
                    bool clockComplete = frame.realTime - window.started >= window.plannedWindowSeconds
                        && frame.dspTime - window.audioStart.dspTime >= window.plannedWindowSeconds;
                    if (!clockComplete && frame.realTime < window.realDeadline) continue;
                    window.complete = true; window.derivedClockWindowCompleted = clockComplete;
                    window.audioEnd = NativeAudioObservation.Capture(contactReport.outputMode);
                    ContactRequire(window.firstRead.distance < window.source.minDistance && window.lastRead.listenerActiveEnabled && window.lastRead.activeListenerCount == 1,
                        "actual-near-owner-unique-listener." + window.path + "." + window.eventName);
                    ContactRequire(clockComplete, "derived-real-and-DSP-window-complete." + window.path + "." + window.eventName);
                    ContactRequire(window.audioStart.captured && window.audioEnd.captured && window.audioEnd.dspTime > window.audioStart.dspTime
                        && window.audioStart.dspTime > 0 && window.audioEnd.dspTime > 0
                        && !double.IsNaN(window.audioStart.dspTime) && !double.IsInfinity(window.audioStart.dspTime)
                        && !double.IsNaN(window.audioEnd.dspTime) && !double.IsInfinity(window.audioEnd.dspTime), "actual-DSP-clock-advanced." + window.path + "." + window.eventName);
                    ContactRequire(window.sourcePeak > 0 && !float.IsNaN(window.sourcePeak) && !float.IsInfinity(window.sourcePeak), "actual-source-DSP." + window.path + "." + window.eventName);
                    if (contactOrdinary) ContactRequire(window.listenerPeak > 0 && !float.IsNaN(window.listenerPeak) && !float.IsInfinity(window.listenerPeak)
                        && window.pairedNonzeroFrames > 0, "isolated-listener1-DSP." + window.path + "." + window.eventName);
                }
            }
            catch (Exception error) { contactObserverError = error.ToString(); }
        }
        IEnumerator ContactCCPhase(ContactOwner owner, string phase, Vector3 movement, Vector3 facing, string tag, bool expectDeath)
        {
            contactPhase = phase; contactProbe.moving = false; contactProbe.body = null; contactProbe.controller = contactController;
            contactController.enabled = false; contactController.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            float distance = owner.collider.bounds.extents.magnitude + contactController.radius + 1;
            Vector3 center = owner.collider.bounds.center - movement.normalized * distance;
            contactController.transform.position = center - contactController.transform.TransformVector(contactController.center);
            contactController.tag = tag; contactController.enabled = true; Physics.SyncTransforms();
            ContactNeed(!Physics.GetIgnoreLayerCollision(contactController.gameObject.layer, owner.collider.gameObject.layer), "CC-real-layer-pair." + phase);
            int start = contactReport.contacts.Count; contactProbe.velocity = movement; contactProbe.moving = true;
            float deadline = Time.realtimeSinceStartup + 3;
            while (Time.realtimeSinceStartup < deadline && (expectDeath ? owner.snow.IsAlive : !contactReport.contacts.Skip(start).Any(r => r.receiverId == owner.snow.GetInstanceID()))) yield return null;
            contactProbe.moving = false;
            var observed = contactReport.contacts.Skip(start).Where(r => r.receiverId == owner.snow.GetInstanceID()).ToArray();
            ContactNeed(observed.Length > 0, "actual-CC-stay." + phase);
            ContactNeed(observed.Any(r => r.velocity.sqrMagnitude > .01f), "actual-CC-Move-velocity-observed." + phase);
            if (expectDeath)
                ContactNeed(!owner.snow.IsAlive && observed.Any(r => r.alive && r.tag == "Player" && r.projectedSquared >= r.threshold), "actual-CC-threshold-death." + phase);
            else ContactNeed(owner.snow.IsAlive, "actual-CC-gate-survives." + phase);
            if (phase.EndsWith("sideways", StringComparison.Ordinal)) ContactRequire(observed.All(r => r.projectedSquared < r.threshold), "actual-sideways-projection");
            if (phase.EndsWith("low", StringComparison.Ordinal)) ContactRequire(observed.All(r => r.projectedSquared < r.threshold), "actual-below-threshold-projection");
            contactController.enabled = false; contactController.transform.position = owner.collider.bounds.center + Vector3.up * 20; contactController.enabled = true;
            Physics.SyncTransforms(); yield return new WaitForFixedUpdate();
        }
        IEnumerator ContactDeadCC(ContactOwner owner)
        {
            contactPhase = "CC-dead-alive-gate"; int before = owner.snow.DestructionCount; int start = contactReport.contacts.Count;
            contactController.enabled = false; contactController.transform.position = owner.collider.bounds.center - contactController.transform.TransformVector(contactController.center);
            contactController.tag = "Player"; contactController.enabled = true; Physics.SyncTransforms();
            contactProbe.velocity = contactController.transform.forward * 18; contactProbe.moving = true;
            yield return new WaitForSeconds(.25f); contactProbe.moving = false;
            ContactRequire(owner.collider.enabled && !owner.snow.IsAlive && owner.snow.DestructionCount == before
                && contactReport.contacts.Skip(start).Any(r => r.receiverId == owner.snow.GetInstanceID() && !r.alive), "actual-dead-CC-stay-no-damage");
            contactController.enabled = false; contactController.transform.position = owner.collider.bounds.center + Vector3.up * 20; contactController.enabled = true; Physics.SyncTransforms();
        }
        IEnumerator ContactItem(ContactOwner owner, string itemId, bool duration, string phase)
        {
            contactPhase = phase; contactProbe.moving = false; contactProbe.controller = null;
            var entry = itemWorld.catalog.Find(itemId); ContactNeed(entry != null && entry.throwPrefab, "original-item-catalog." + itemId);
            var expected = entry.throwPrefab.GetComponentsInChildren<NativeSnowmanDamageTrigger>(true).Where(b => b.useDamageDuration == duration).ToArray();
            ContactNeed(expected.Length == 1, "original-item-bridge-denominator." + itemId);
            Vector3 direction = Vector3.forward; float distance = owner.collider.bounds.extents.magnitude + 4;
            var actor = itemWorld.Spawn(itemId, racers[0], owner.collider.bounds.center - direction * distance, Quaternion.identity, Vector3.zero);
            ContactNeed(actor, "production-Spawn-returned-actor." + itemId); contactActors.Add(actor); actor.enabled = false;
            var bridge = actor.GetComponentsInChildren<NativeSnowmanDamageTrigger>(true).Single(b => b.sourceComponentId == expected[0].sourceComponentId);
            var hurt = bridge.GetComponent<Collider>(); var body = bridge.GetComponent<Rigidbody>();
            ContactNeed(hurt && hurt.enabled && hurt.isTrigger && bridge.isActiveAndEnabled && hurt.gameObject.activeInHierarchy
                && body && body.isKinematic && body.detectCollisions && hurt.attachedRigidbody == body,
                "original-hurt-trigger-kinematic-RB." + itemId);
            ContactNeed(bridge.useDamageDuration == expected[0].useDamageDuration && bridge.damageDuration == expected[0].damageDuration
                && bridge.sourceSiblingPath == expected[0].sourceSiblingPath && !string.IsNullOrEmpty(bridge.sourceComponentId), "original-hurt-fields." + itemId);
            ContactNeed(!Physics.GetIgnoreLayerCollision(hurt.gameObject.layer, owner.collider.gameObject.layer) && !Physics.GetIgnoreCollision(hurt, owner.collider), "real-item-layer-pair." + itemId);
            var ancestorMotion = bridge.GetComponentsInParent<NativeItemPrefabMotion>(true)
                .Where(m => m.enabled && (m.transform == actor.transform || m.transform.IsChildOf(actor.transform))).ToArray();
            foreach (var motion in ancestorMotion) motion.enabled = false;
            Physics.SyncTransforms();
            actor.transform.position += owner.collider.bounds.center - direction * distance - hurt.bounds.center; Physics.SyncTransforms();
            var attempt = new ContactItemAttempt { phase = phase, itemId = itemId, prefabName = entry.throwPrefab.name,
                bridgeSource = bridge.sourceComponentId, bridgeSiblingPath = bridge.sourceSiblingPath,
                pausedAncestorMotionSources = ancestorMotion.Select(m => m.sourceComponentId).ToArray(),
                pausedAncestorMotion = ancestorMotion.Select(m => new ContactPausedMotion { source = m.sourceComponentId, path = WinterPath(m.transform),
                    rotationPerSecond = m.rotationPerSecond, overrideLocalPosition = m.overrideLocalPosition, bobAmplitude = m.bobAmplitude, bobRate = m.bobRate }).ToArray(),
                hurtLocalPosition = hurt.transform.localPosition, hurtLossyScale = hurt.transform.lossyScale,
                originalSphereRadius = hurt is SphereCollider sphere ? sphere.radius : 0, startFrame = Time.frameCount,
                startTime = Time.time, actorId = actor.GetInstanceID(), bodyId = body.GetInstanceID(), colliderId = hurt.GetInstanceID(),
                actorActive = actor.gameObject.activeInHierarchy, hurtActive = hurt.gameObject.activeInHierarchy,
                bridgeActive = bridge.isActiveAndEnabled, receiverActive = owner.collider.gameObject.activeInHierarchy,
                detectCollisions = body.detectCollisions, correctAttachedRigidbody = hurt.attachedRigidbody == body, bodyAwakeAtStart = !body.IsSleeping(),
                bodyStart = body.position, hurtCenterStart = hurt.bounds.center, receiverCenter = owner.collider.bounds.center,
                closestBoundsCenterDistance = Vector3.Distance(hurt.bounds.center, owner.collider.bounds.center) };
            contactReport.itemAttempts.Add(attempt);
            ContactNeed(attempt.actorActive && attempt.hurtActive && attempt.bridgeActive && attempt.receiverActive,
                "actual-item-and-receiver-active-hierarchy." + phase);
            ContactNeed(!hurt.bounds.Intersects(owner.collider.bounds), "item-start-outside." + phase);
            int count = owner.snow.DestructionCount; int start = contactReport.contacts.Count;
            contactProbe.body = body; contactProbe.bodyMoveTicks = 0; contactProbe.velocity = direction * 18; contactProbe.moving = true;
            float deadline = Time.realtimeSinceStartup + 3;
            while (owner.snow.DestructionCount == count && actor && Time.realtimeSinceStartup < deadline)
            {
                attempt.closestBoundsCenterDistance = Mathf.Min(attempt.closestBoundsCenterDistance,
                    Vector3.Distance(hurt.bounds.center, owner.collider.bounds.center));
                yield return null;
            }
            contactProbe.moving = false; attempt.endFrame = Time.frameCount; attempt.endTime = Time.time;
            attempt.bodyMoveTicks = contactProbe.bodyMoveTicks; attempt.lastRequestedBodyTarget = contactProbe.lastBodyTarget;
            if (body) attempt.bodyEnd = body.position; if (hurt) attempt.hurtCenterEnd = hurt.bounds.center;
            ContactNeed(attempt.bodyMoveTicks > 0, "actual-original-item-body-MovePosition-ticks." + phase);
            ContactNeed(owner.snow.DestructionCount == count + 1 && !owner.snow.IsAlive,
                "actual-original-item-enter-damage." + phase, "moveTicks=" + attempt.bodyMoveTicks + " closestBoundsCenterDistance=" + attempt.closestBoundsCenterDistance);
            ContactNeed(contactReport.contacts.Skip(start).Any(r => r.receiverId == owner.snow.GetInstanceID() && r.itemId == itemId && r.after && r.useDuration == duration), "actual-bridge-after-receipt." + phase);
            if (actor) actor.DestroyNow(); yield return new WaitForFixedUpdate();
        }
        void ContactConfigureListener(ContactOwner owner)
        {
            if (!contactFixtureListener)
            {
                foreach (var listener in FindObjectsOfType<AudioListener>(true))
                {
                    contactOriginalListeners.Add(listener, listener.enabled);
                    contactReport.originalListeners.Add(new ContactListenerRestore { path = WinterPath(listener.transform), instanceId = listener.GetInstanceID(), originalEnabled = listener.enabled });
                    listener.enabled = false;
                }
                contactFixtureListener = contactProbe.gameObject.AddComponent<AudioListener>();
            }
            ContactNeed(!contactReport.audio.Any(w => !w.complete) && !contactSnowAudio.Any(a => a && a.isPlaying),
                "serial-owner-audio-quiescent." + owner.baseline.sourceId);
            contactFixtureListener.transform.position = owner.collider.bounds.center;
            ContactNeed(contactFixtureListener.isActiveAndEnabled && FindObjectsOfType<AudioListener>(true).Count(a => a.isActiveAndEnabled) == 1,
                "one-real-fixture-listener." + owner.baseline.sourceId);
        }
        IEnumerator ContactCompleteOwner(ContactOwner owner)
        {
            float deadline = Time.realtimeSinceStartup + 22f;
            while ((owner.observedRespawns == 0 || contactReport.audio.Any(w => w.receiverSource == owner.snow.sourceComponentId && !w.complete)
                || owner.snow.GetComponentsInChildren<AudioSource>(true).Any(a => a.isPlaying)) && Time.realtimeSinceStartup < deadline)
            { if (contactObserverError != null) throw new InvalidOperationException(contactObserverError); yield return null; }
            ContactNeed(owner.observedRespawns == 1 && contactReport.audio.Where(w => w.receiverSource == owner.snow.sourceComponentId).All(w => w.complete)
                && !owner.snow.GetComponentsInChildren<AudioSource>(true).Any(a => a.isPlaying), "serial-owner-real-respawn-and-natural-audio-complete." + owner.baseline.sourceId);
        }
        IEnumerator ContactPhysicsCore()
        {
            float splashDeadline = Time.realtimeSinceStartup + 20;
            while (StartupSplashActive && Time.realtimeSinceStartup < splashDeadline) yield return null;
            ContactNeed(trackId == "wintermistylake" && StartupSplash && !StartupSplashActive, "Winter-scene-real-Splash-complete");
            ContactNeed(!NativeAudioRuntime.Muted, "production-audio-unmuted");
            ContactNeed(errorCount == 0, "clear-engine-error-at-contact-entry", "errors=" + errorCount);
            ContactNeed(Time.timeScale == 1f, "real-scaled-clock-default-speed");
            var baseline = NativeContent.Load<TextAsset>("NativeVerification/WinterRuntimeSource");
            ContactNeed(baseline && WinterHash(baseline.bytes) == WinterBaselineHash, "independent-source-baseline-hash");
            contactReport.baselineSHA256 = WinterBaselineHash; var source = JsonUtility.FromJson<WinterSource>(baseline.text);
            ContactNeed(source.snowmen.Length == 4 && source.snowmen.Sum(s => s.calls.Length) == 24 && source.awakeDeathAudio.Length == 2, "source4snow24calls8audio");
            foreach (var row in source.snowmen)
            {
                var snow = WinterFind(row.path).GetComponent<NativeSnowmanDestructable>(); var capsule = snow.GetComponent<CapsuleCollider>();
                ContactNeed(snow.IsAlive && capsule && capsule.enabled && capsule.isTrigger && snow.speedRequired == row.speedRequired && snow.respawnTime == 10,
                    "original-snowman-fields." + row.sourceId);
                var owner = new ContactOwner { snow = snow, baseline = row, collider = capsule, initialDeaths = snow.DestructionCount, initialRespawns = snow.RespawnCount };
                contactOwners.Add(owner); owner.death = () => ContactEvent(owner, true); owner.respawn = () => ContactEvent(owner, false);
                snow.onDestructed.AddListener(owner.death); snow.onRespawn.AddListener(owner.respawn);
                WinterRequire(snow.onDestructed.GetPersistentEventCount() == row.calls.Count(c => c.eventName == "onDestructed")
                    && snow.onRespawn.GetPersistentEventCount() == row.calls.Count(c => c.eventName == "onRespawn"),
                    "contact.original-call-denominator." + row.sourceId, "ordered-serialized-field");
                foreach (var call in row.calls)
                {
                    var value = call.eventName == "onDestructed" ? snow.onDestructed : snow.onRespawn;
                    var expected = WinterTarget(WinterFind(call.targetPath), call.targetType); var persistent = WinterPersistentCall(value, call.order);
                    WinterRequire(value.GetPersistentTarget(call.order) == expected && value.GetPersistentMethodName(call.order) == call.method
                        && (int)value.GetPersistentListenerState(call.order) == call.state && Convert.ToInt32(WinterField(persistent, "m_Mode")) == call.mode
                        && (bool)WinterField(WinterField(persistent, "m_Arguments"), "m_BoolArgument") == call.argument,
                        "contact.original-call." + row.sourceId + "." + call.eventName + "." + call.order, "ordered-serialized-field");
                    if (expected is AudioSource audio) CheckWinterAudioFields(audio, call.audio, call.sourceTargetId);
                }
                foreach (var audio in snow.GetComponentsInChildren<AudioSource>(true)) contactSnowAudio.Add(audio);
            }
            foreach (var audio in source.awakeDeathAudio) CheckWinterAudioFields(WinterFind(audio.path).GetComponent<AudioSource>(), audio.audio, audio.sourceId, true);
            ContactNeed(contactSnowAudio.Count == 8, "original-eight-snowman-audio-sources");
            contactProbe.observeFrame = ContactObserveFrame;
            foreach (var motor in racers) motor.enabled = false;
            contactController = racers[0].GetComponent<CharacterController>(); ContactNeed(contactController && contactController.enabled, "original-player-CC");
            var cc0 = contactOwners.Single(o => o.baseline.path == "Snowman"); var cc1 = contactOwners.Single(o => o.baseline.path == "Snowman (3)");
            var item0 = contactOwners.Single(o => o.baseline.path == "Snowman (2)"); var item1 = contactOwners.Single(o => o.baseline.path == "Snowman (1)");
            ContactConfigureListener(cc0);
            yield return ContactCCPhase(cc0, "CC-low", Vector3.forward * 10, Vector3.forward, "Player", false);
            yield return ContactCCPhase(cc0, "CC-sideways", Vector3.right * 18, Vector3.forward, "Player", false);
            yield return ContactCCPhase(cc0, "CC-nonPlayer", Vector3.forward * 18, Vector3.forward, "Untagged", false);
            yield return ContactCCPhase(cc0, "CC-forward-high", Vector3.forward * 18, Vector3.forward, "Player", true);
            yield return ContactDeadCC(cc0); yield return ContactCompleteOwner(cc0);
            ContactConfigureListener(cc1);
            yield return ContactCCPhase(cc1, "CC-backward-high", Vector3.back * 18, Vector3.forward, "Player", true);
            yield return ContactCompleteOwner(cc1);
            ContactConfigureListener(item0);
            yield return ContactItem(item0, "YinYang", false, "item-noarg-first");
            yield return new WaitForSeconds(2);
            yield return ContactItem(item0, "YinYang", false, "item-noarg-dead-repeat");
            yield return ContactCompleteOwner(item0);
            ContactConfigureListener(item1);
            yield return ContactItem(item1, "Shangai", true, "item-duration-first");
            yield return new WaitForSeconds(2);
            yield return ContactItem(item1, "Shangai", true, "item-duration-dead-repeat");
            yield return ContactCompleteOwner(item1);
            float deadline = Time.realtimeSinceStartup + 14f;
            while (contactReport.audio.Any(w => !w.complete) && Time.realtimeSinceStartup < deadline)
            { if (contactObserverError != null) throw new InvalidOperationException(contactObserverError); yield return null; }
            ContactNeed(contactReport.audio.All(w => w.complete && w.derivedClockWindowCompleted), "all-derived-audio-windows-complete");
            foreach (var owner in contactOwners)
            {
                int expectedDeaths = owner == item0 || owner == item1 ? 2 : 1;
                ContactRequire(owner.observedDeaths == expectedDeaths && owner.observedRespawns == 1 && owner.snow.IsAlive
                    && owner.snow.DestructionCount == owner.initialDeaths + expectedDeaths && owner.snow.RespawnCount == owner.initialRespawns + 1,
                    "four-independent-real-events-and-Invoke." + owner.baseline.sourceId);
                ContactRequire(owner.sampledBeforeDeadline, "deadline-precheck-recorded." + owner.baseline.sourceId);
            }
            ContactRequire(item0.sampledOldDeadline && item1.sampledOldDeadline, "real-item-repeat-cancels-old-deadline");
            ContactRequire(contactReport.audio.Count(w => w.firstDeath) == 4 && contactReport.audio.Count(w => w.eventName == "respawn") == 4,
                "first-death-four-and-respawn-four-DSP-windows");
        }
        internal IEnumerator CheckWinterSnowmanContactPhysics()
        {
            var args = Environment.GetCommandLineArgs(); contactOrdinary = args.Contains("--winter-ordinary-audio-output");
            contactReport = new ContactPhysicsReport { commandLine = args, outputMode = contactOrdinary ? "isolated-snowman-listener1" : "snowman-source-DSP-listener0", isolatedListenerOutput = contactOrdinary };
            winterAudit = new WinterRuntimeReport { outputMode = contactReport.outputMode };
            contactOwners.Clear(); contactActors.Clear(); contactSnowAudio.Clear(); contactOriginalListeners.Clear(); contactFixtureListener = null; contactObserverError = null;
            contactReport.entryEngineErrorCount = errorCount;
            float started = Time.realtimeSinceStartup, listener = AudioListener.volume;
            bool silence = NativeAudioRuntime.VerificationListenerSilenced;
            var motors = new Dictionary<NativeKartMotor, bool>(); Transform actor = null;
            CharacterController originalController = null; bool controllerEnabled = false;
            Vector3 position = Vector3.zero; Quaternion rotation = Quaternion.identity; string tag = null;
            GameObject node = null; var stack = new Stack<IEnumerator>();
            try
            {
                motors = racers.ToDictionary(r => r, r => r.enabled); actor = racers[0].transform;
                originalController = actor.GetComponent<CharacterController>(); controllerEnabled = originalController && originalController.enabled;
                position = actor.position; rotation = actor.rotation; tag = actor.tag;
                node = new GameObject("Snowman contact physics observer"); contactProbe = node.AddComponent<NativeSnowmanContactProbe>();
                NativeSnowmanDestructable.PhysicalStayObserved += ContactStay; NativeSnowmanDamageTrigger.PhysicalEnterObserved += ContactEnter;
                Application.logMessageReceived += ContactLog;
                NativeAudioRuntime.VerificationListenerSilenced = !contactOrdinary; AudioListener.volume = contactOrdinary ? 1 : 0;
                contactReport.audioEntry = NativeAudioObservation.Capture(contactReport.outputMode);
                stack.Push(ContactPhysicsCore());
            }
            catch (Exception error) { ContactError(error); }
            while (stack.Count > 0)
            {
                bool next; object current;
                try { next = stack.Peek().MoveNext(); current = next ? stack.Peek().Current : null; }
                catch (Exception error) { ContactError(error); break; }
                if (!next) { var done = stack.Pop() as IDisposable; if (done != null) ContactGuard(done.Dispose); continue; }
                if (current is CustomYieldInstruction instruction) { yield return instruction; continue; }
                if (current is IEnumerator child) { stack.Push(child); continue; }
                yield return current;
            }
            foreach (var remaining in stack) { var disposable = remaining as IDisposable; if (disposable != null) ContactGuard(disposable.Dispose); }
            ContactGuard(() => { if (contactProbe) { contactProbe.moving = false; contactProbe.observeFrame = null; } });
            ContactGuard(() => NativeSnowmanDestructable.PhysicalStayObserved -= ContactStay);
            ContactGuard(() => NativeSnowmanDamageTrigger.PhysicalEnterObserved -= ContactEnter);
            foreach (var owner in contactOwners) ContactGuard(() => { if (owner.snow) { owner.snow.onDestructed.RemoveListener(owner.death); owner.snow.onRespawn.RemoveListener(owner.respawn); } });
            foreach (var item in contactActors) ContactGuard(() => { if (item) item.DestroyNow(); });
            ContactGuard(() => { if (originalController) originalController.enabled = false; if (actor) { actor.SetPositionAndRotation(position, rotation); actor.tag = tag; } });
            ContactGuard(() => { if (originalController) originalController.enabled = controllerEnabled; });
            foreach (var motor in motors) ContactGuard(() => { if (motor.Key) motor.Key.enabled = motor.Value; });
            ContactGuard(() => { if (contactFixtureListener) contactFixtureListener.enabled = false;
                ContactRequire(!contactFixtureListener || !contactFixtureListener.enabled, "fixture-listener-disabled-before-return"); });
            foreach (var listenerState in contactOriginalListeners) ContactGuard(() =>
            {
                if (listenerState.Key)
                {
                    listenerState.Key.enabled = listenerState.Value;
                    var receipt = contactReport.originalListeners.Single(r => r.instanceId == listenerState.Key.GetInstanceID());
                    receipt.restoredEnabled = listenerState.Key.enabled; receipt.restored = receipt.restoredEnabled == receipt.originalEnabled;
                    ContactRequire(receipt.restored, "original-listener-enabled-restored." + receipt.instanceId);
                }
                else ContactRequire(false, "original-listener-removed-during-fixture");
            });
            ContactGuard(() => Destroy(node));
            ContactGuard(() => { NativeAudioRuntime.VerificationListenerSilenced = silence; AudioListener.volume = listener; });
            contactReport.sourceFieldChecks = winterAudit.checks.ToArray(); contactReport.sourceAudioFields = winterAudit.audio.ToArray();
            ContactGuard(() => contactReport.audioEnd = NativeAudioObservation.Capture(contactReport.outputMode));
            contactReport.realtimeSeconds = Time.realtimeSinceStartup - started;
            ContactGuard(() => contactReport.assemblySHA256 = WinterHash(File.ReadAllBytes(typeof(NativeRace).Assembly.Location)));
            if (contactObserverError != null) ContactError(new InvalidOperationException(contactObserverError));
            contactReport.actualPhysicsEvent = contactReport.contacts.Any(r => r.domain == "CharacterController.OnTriggerStay")
                && contactReport.contacts.Any(r => r.domain == "original-prefab-TriggerHurt.OnTriggerEnter" && r.after);
            ContactRequire(contactReport.actualPhysicsEvent, "both-real-physics-domains-observed");
            Application.logMessageReceived -= ContactLog;
            contactReport.passed = contactReport.error == null && contactReport.engineErrorCount == 0 && errorCount == 0 && contactReport.entryEngineErrorCount == 0 && contactReport.checks.All(c => c.passed) && contactReport.sourceFieldChecks.All(c => c.passed);
            try
            {
                Directory.CreateDirectory(reportDirectory); contactReport.reportSaved = true;
                File.WriteAllText(Path.Combine(reportDirectory, "winter-snowman-contact-report.json"), JsonUtility.ToJson(contactReport, true));
                Debug.Log("WINTER_SNOWMAN_CONTACT_RESULT passed=" + contactReport.passed + " accepted=false physicalReceipts=" + contactReport.contacts.Count);
            }
            catch (Exception error)
            {
                contactReport.reportSaved = false; contactReport.passed = false; ContactError(error);
                Debug.LogError("WINTER_SNOWMAN_CONTACT_REPORT_WRITE_FAILED " + error);
            }
            finally { Application.Quit(contactReport.passed && contactReport.reportSaved ? 0 : 2); }
        }
    }
}
