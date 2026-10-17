using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartItemInventory : MonoBehaviour
    {
        public NativeKartMotor motor;
        public NativeItemCatalog catalog;
        public GameObject projectilePrefab;
        public Sprite powerIcon, yinYangIcon;
        public Transform throwPoint, placePoint, animAttachPoint;
        public NativeKartItemState State { get; private set; }
        public bool HasItem => State.HasItem;
        public bool ItemDecided => State.decided;
        public NativeKartItemKind CurrentKind => State.kind;
        public string CurrentId => HasItem ? State.kind.ToString() : null;
        public Sprite CurrentIcon => ItemDecided && catalog ? catalog.Find(CurrentId)?.icon : null;
        public int GiveCount => State.giveCount;
        public int DecideCount => State.decideCount;
        public int UseCount => State.useCount;
        public int DoneCount => State.doneCount;
        public bool Frozen { get; private set; }
        public string CharacterId { get; private set; }
        public float CharacterCooldown { get; private set; }
        public NativeKartItemEffects Effects { get; private set; }
        public NativeItemWorld World { get; private set; }

        NativeRace race;
        NativeItemActor carriedActor;
        bool aiUse;
        float aiUseRemaining = 1f;
        float heldRemaining;
        bool resetting;
        bool useRequested;

        void Awake()
        {
            if (!motor) motor = GetComponent<NativeKartMotor>();
            Effects = GetComponent<NativeKartItemEffects>();
            if (!Effects) Effects = gameObject.AddComponent<NativeKartItemEffects>();
            Effects.Completed += EffectCompleted;
        }

        public void Configure(NativeRace ownerRace, NativeItemWorld world, string characterId, bool useAI)
        {
            race = ownerRace;
            World = world;
            CharacterId = characterId;
            aiUse = useAI;
        }

        public void ApplyNetworkInventory(NativeKartItemState state, bool frozen)
        { State = state; Frozen = frozen; }

        void Update()
        {
            if (NativeRaceNetwork.ClientRace) return;
            if (Time.timeScale <= 0f) return;
            if (useRequested)
            {
                useRequested = false;
                UseItem();
            }
            CharacterCooldown = Mathf.Max(0f, CharacterCooldown - Time.deltaTime);
            var next = State;
            bool wasDecided = next.decided;
            // Effect completion owns active timers; the pure state helper retains its isolated Power fixture.
            if (!next.used) next.Advance(Time.deltaTime);
            State = next;
            if (!wasDecided && next.decided) StartItem();
            if (heldRemaining > 0f)
            {
                heldRemaining -= Time.deltaTime;
                if (heldRemaining <= 0f) CompleteCurrent();
            }
        }

        public void StepAIUse(float deltaTime, float minimumUseTime, float maximumUseTime)
        {
            if (NativeRaceNetwork.ClientRace || !aiUse || !race || !race.IsRaceRunning
                || !motor || !motor.driving || deltaTime <= 0f || aiUseRemaining <= 0f) return;
            aiUseRemaining -= deltaTime;
            if (aiUseRemaining > 0f) return;
            UseItem(false, false);
            var ufo = GetComponent<NativeUfoPlayer>();
            if (ufo) ufo.Use();
            aiUseRemaining = UnityEngine.Random.Range(minimumUseTime, maximumUseTime);
        }

        public bool TryGive(string id, bool random = true)
        {
            return Enum.TryParse(id, out NativeKartItemKind kind) && TryGive(kind, random);
        }

        public bool TryGive(NativeKartItemKind kind, bool random = true)
        {
            if (NativeRaceNetwork.ClientRace) { NativeTelemetry.Count("item.give.non-authority"); return false; }
            if (!catalog || catalog.Find(kind.ToString()) == null) { NativeTelemetry.Event("item.give", "failed", "catalog-entry-missing", kind.ToString()); return false; }
            var next = State;
            if (!next.Give(kind, random)) { NativeTelemetry.Count("item.give.ignored"); return false; }
            State = next;
            NativeTelemetry.Count("item.give.accepted");
            var entry = catalog.Find(kind.ToString());
            motor.PublishHaptic(NativeHapticKind.Pickup, context: kind.ToString());
            if (entry.family == NativeItemFamily.Shield) heldRemaining = entry.duration;
            if (entry.id == "Ofuda" && race) race.SourceOfudaCreated(entry);
            if (!random) StartItem();
            return true;
        }

        public bool TryGiveRandom()
        {
            if (HasItem || !race) return false;
            var entry = NativeItemSelection.Choose(catalog, race.RankOf(motor) - 1, race.racers.Length,
                CharacterId, CharacterCooldown, race.SourceGameTime, race.NextOfudaTime, race.SourceFirstRacerTime, race.SourceOfudaEligibilityNext);
            return entry != null && TryGive(entry.id);
        }

        void StartItem()
        {
            NativeTelemetry.Count("item.decided");
            var entry = catalog ? catalog.Find(CurrentId) : null;
            if (entry != null && entry.useAtStart) UseItem(true);
        }

        public bool UseItem() => UseItem(false);

        public void RequestUse()
        {
            // Player input runs before vehicle movement; consume it in this component's later Update.
            if (Time.timeScale <= 0f || !motor || Frozen) { NativeTelemetry.Count("item.request.suppressed"); return; }
            var ufo = GetComponent<NativeUfoPlayer>();
            if (!HasItem && ufo && ufo.Use()) return;
            if (HasItem && ItemDecided) { useRequested = true; NativeTelemetry.Count("item.request.accepted"); }
            else NativeTelemetry.Count("item.request.ignored");
        }

        bool UseItem(bool automatic, bool allowUfoFallback = true)
        {
            string itemId = CurrentId;
            bool used = UseItemCore(automatic, allowUfoFallback);
            NativeTelemetry.Count(used ? "item.use.accepted" : "item.use.ignored");
            if (used && !string.IsNullOrEmpty(itemId)) NativeTelemetry.Event("item.use", "applied", detail: "item=" + itemId + ";entity=" + motor.GetInstanceID() + ";automatic=" + automatic);
            if (used) motor.PublishHaptic(itemId == "Power" || itemId == "FullPower" ? NativeHapticKind.Boost : NativeHapticKind.ItemUse, context: itemId);
            // PlayerItems plays the current clip after Item.Use succeeds and before onItemUsed.
            if (used && !string.IsNullOrEmpty(itemId)) { NativeAudioRuntime.PlayEvent("item." + itemId, transform); NativeRaceNetwork.RecordSourceAudio(transform,"item."+itemId); }
            return used;
        }

        bool UseItemCore(bool automatic, bool allowUfoFallback)
        {
            if (!motor || (Frozen && !automatic) || !World) return false;
            var ufo = GetComponent<NativeUfoPlayer>();
            if (allowUfoFallback && !HasItem && ufo && ufo.Use()) return true;
            if (!HasItem || !ItemDecided) return false;
            var entry = catalog.Find(CurrentId);
            if (entry == null || (State.used && !State.carried)) return false;
            bool carry = (entry.twoAction || entry.family == NativeItemFamily.Frog) && !State.carried;
            var next = State;
            if (!next.BeginUse(Frozen && !automatic, carry)) return false;
            State = next;
            if (!string.IsNullOrEmpty(entry.trigger))
            {
                var actions = GetComponent<NativeKartAnimationActions>();
                if (actions) actions.SetTrigger(entry.trigger);
            }
            if (Effects.TryUse(entry, this)) return true;
            if (entry.family == NativeItemFamily.Boost || entry.family == NativeItemFamily.FullPower || entry.family == NativeItemFamily.WindBoost)
            {
                CompleteCurrent();
                return false;
            }
            if (carry)
            {
                var point = placePoint ? placePoint : transform;
                carriedActor = World.Spawn(entry.id, motor, point.position, point.rotation, Vector3.zero, true);
                if (carriedActor)
                {
                    carriedActor.AttachTo(point);
                    carriedActor.Destroyed += CarriedDestroyed;
                }
                else
                {
                    CompleteCurrent();
                    return false;
                }
                return true;
            }
            if (entry.family == NativeItemFamily.Frog && carriedActor)
            {
                carriedActor.Destroyed -= CarriedDestroyed;
                carriedActor.Launch(carriedActor.transform.position, carriedActor.transform.rotation, Vector3.zero);
                carriedActor = null;
                CompleteCurrent();
                return true;
            }
            ClearCarried();
            var origin = entry.family == NativeItemFamily.Bomb || entry.family == NativeItemFamily.Shield ? transform
                : entry.family == NativeItemFamily.Melee ? (animAttachPoint ? animAttachPoint : transform)
                : (throwPoint ? throwPoint : transform);
            int count = Mathf.Max(1, entry.count);
            bool spawned = true;
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = entry.offsets != null && i < entry.offsets.Length ? entry.offsets[i] : Vector3.zero;
                Vector3 spread = entry.spread != null && i < entry.spread.Length ? entry.spread[i] : Vector3.zero;
                bool fast = motor.Speed >= entry.fastSpeedCondition;
                float force = fast ? entry.forceFast : entry.force;
                float up = fast ? entry.forceFastUp : entry.forceUp;
                Quaternion rotation = origin.rotation * Quaternion.Euler(spread);
                var actor = World.Spawn(entry.id, motor, origin.TransformPoint(offset), rotation,
                    rotation * Vector3.forward * force + Vector3.up * up);
                // Area effects resolve immediately and have no persistent actor.
                if (!actor && entry.family != NativeItemFamily.Bomb) spawned = false;
            }
            if (entry.family == NativeItemFamily.Melee) heldRemaining = entry.duration;
            else if (entry.family != NativeItemFamily.Shield) CompleteCurrent();
            return spawned;
        }

        void CarriedDestroyed(NativeItemActor actor)
        {
            if (carriedActor != actor) return;
            carriedActor.Destroyed -= CarriedDestroyed;
            carriedActor = null;
            if (!resetting) CompleteCurrent();
        }

        void ClearCarried()
        {
            if (carriedActor)
            {
                carriedActor.Destroyed -= CarriedDestroyed;
                carriedActor.DestroyNow();
            }
            carriedActor = null;
        }

        void EffectCompleted(string id)
        {
            if (id == CurrentId && State.used && !resetting) CompleteCurrent();
        }

        void CompleteCurrent()
        {
            if (!HasItem) return;
            var entry = catalog.Find(CurrentId);
            if (entry.family == NativeItemFamily.Shield && World) World.ClearItemForOwner(entry.id, motor);
            if (!string.IsNullOrEmpty(entry.characterConditionId)) CharacterCooldown = entry.characterCooldown;
            var next = State;
            next.ClearCompleted();
            State = next;
            heldRemaining = 0f;
            if (!resetting && !string.IsNullOrEmpty(entry.giveOnDoneId)) TryGive(entry.giveOnDoneId, false);
        }

        public void SetFrozen(bool frozen) { Frozen = frozen; }

        public void ResetItems()
        {
            resetting = true;
            useRequested = false;
            ClearCarried();
            if (World) World.ClearForOwner(motor);
            NativeKartItemProjectile.ClearForOwner(motor);
            if (Effects) Effects.ResetEffects();
            Frozen = false;
            heldRemaining = CharacterCooldown = 0f;
            aiUseRemaining = 1f;
            var next = State;
            next.Reset();
            State = next;
            resetting = false;
        }

        void OnDestroy()
        {
            resetting = true;
            ClearCarried();
            if (Effects) Effects.Completed -= EffectCompleted;
        }
    }
}
