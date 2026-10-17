using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeItemCoverageVerification
    {
        [Serializable] sealed class Check
        {
            public string name, detail;
            public bool passed;
        }

        [Serializable] sealed class ItemResult
        {
            public string id, family, finalId;
            public bool given, decided, used, completed, interaction;
            public int actorsOnUse, actorsAfter, giveDelta, decideDelta, useDelta, doneDelta;
        }

        [Serializable] sealed class Report
        {
            public string version, track;
            public bool passed;
            public int catalogCount, checksPassed, checksTotal;
            public Check[] checks;
            public ItemResult[] items;
            public string[] captures;
        }

        sealed class Expected
        {
            public string id, character, next;
            public int min, max;
            public float cooldown, chance;
            public Expected(string item, int first, int last, string owner = null,
                float cd = 45f, float roll = 50f, string chain = null)
            { id = item; min = first; max = last; character = owner; cooldown = cd; chance = roll; next = chain; }
        }

        static readonly Expected[] Original =
        {
            new Expected("BlueStar",2,6), new Expected("Bnuuy",1,7,"Tewi",33),
            new Expected("Bomb",4,6), new Expected("DriftPlusPowerup",-1,-1),
            new Expected("FantasySeal",2,7,"Reimu"), new Expected("FreezeBomb",1,6,"Cirno",30,66),
            new Expected("Frog",0,6), new Expected("FullPower",7,7),
            new Expected("Insanity",2,7,"Reisen",35,59), new Expected("JumperFrog",0,6),
            new Expected("MarisaShield",0,6,"Marisa",36,66), new Expected("Ofuda",2,7),
            new Expected("Power",1,4), new Expected("Power2",3,6,chain:"Power"),
            new Expected("Power3",6,7,chain:"Power2"),
            new Expected("RockThrow",2,7,"Suika",20,60),
            new Expected("SanaeSnakes",2,7,"Sanae",32,60),
            new Expected("Shangai",2,7,"Alice",32,75),
            new Expected("SwordSlash",1,6,"Youmu",30),
            new Expected("WindBoost",1,7,"Aya",35),
            new Expected("YellowStar",1,5), new Expected("YinYang",0,5),
            new Expected("YinYang2",1,6,chain:"YinYang"),
            new Expected("YinYangHoming",1,5),
            new Expected("YinYangHoming2",5,7,chain:"YinYangHoming")
        };

        static void Add(List<Check> checks, string name, bool passed, string detail)
        { checks.Add(new Check { name = name, passed = passed, detail = detail }); }

        static bool Equal(float actual, float expected) => Mathf.Abs(actual - expected) < .001f;
        static bool SameOptional(string actual, string expected) =>
            string.IsNullOrEmpty(actual) ? string.IsNullOrEmpty(expected) :
            string.Equals(actual,expected,StringComparison.Ordinal);

        static void Reset(NativeKartItemInventory inventory, NativeItemWorld world,
            NativeKartItemEffects effects, NativeKartHit hit)
        {
            inventory.ResetItems();
            if (world) world.ResetWorld();
            if (effects) effects.ResetEffects();
            if (hit) hit.ResetState();
        }

        static NativeItemActor[] Actors(NativeKartMotor owner, string id) =>
            UnityEngine.Object.FindObjectsOfType<NativeItemActor>()
                .Where(actor => actor && !actor.Finished && actor.Owner == owner && actor.ItemId == id).ToArray();

        static IEnumerator WaitScaled(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        static bool Capture(Camera camera, NativeKartMotor owner, string directory, string id,
            List<string> captures)
        {
            if (!camera || !owner) return false;
            camera.transform.position = owner.transform.position - owner.transform.forward * 14f +
                owner.transform.right * 11f + Vector3.up * 7f;
            camera.transform.LookAt(owner.transform.position + owner.transform.forward * 4f + Vector3.up);
            var image = new RenderTexture(960,540,24,RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(960,540,TextureFormat.RGB24,false);
            var oldActive = RenderTexture.active;
            var oldTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = image;
                camera.Render();
                RenderTexture.active = image;
                pixels.ReadPixels(new Rect(0,0,960,540),0,0);
                pixels.Apply();
                float low = 1f, high = 0f;
                for (int y = 20; y < 540; y += 40)
                    for (int x = 20; x < 960; x += 40)
                    {
                        var color = pixels.GetPixel(x,y);
                        float value = (color.r + color.g + color.b) / 3f;
                        low = Mathf.Min(low,value);
                        high = Mathf.Max(high,value);
                    }
                string path = Path.Combine(directory,"native-item-"+id+".png");
                File.WriteAllBytes(path,pixels.EncodeToPNG());
                captures.Add(path);
                return high - low > .04f;
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                UnityEngine.Object.Destroy(image);
                UnityEngine.Object.Destroy(pixels);
            }
        }

        static void Place(NativeKartMotor motor, Vector3 position, Quaternion rotation)
        {
            var body = motor.GetComponent<CharacterController>();
            if (body) body.enabled = false;
            motor.transform.SetPositionAndRotation(position, rotation);
            if (body) body.enabled = true;
            Physics.SyncTransforms();
        }

        static NativeKartMotor CreateAbilityPilot(NativeKartMotor reference,
            NativeKartSettings settings, GameObject root, Vector3 origin,
            out NativeKartHit hit, out NativeKartItemEffects effects)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.SetParent(root.transform);
            ground.transform.position = origin + Vector3.down * .5f;
            ground.transform.localScale = new Vector3(1000f,1f,1000f);
            var racer = new GameObject("ItemAbilityPilot");
            racer.tag = "Player";
            racer.transform.SetParent(root.transform);
            racer.transform.position = origin + Vector3.up * 2f;
            var sourceBody = reference.GetComponent<CharacterController>();
            var body = racer.AddComponent<CharacterController>();
            body.center = sourceBody.center;
            body.radius = sourceBody.radius;
            body.height = sourceBody.height;
            body.skinWidth = sourceBody.skinWidth;
            body.slopeLimit = Mathf.Max(sourceBody.slopeLimit,settings._slopeLimit);
            body.stepOffset = sourceBody.stepOffset;
            body.minMoveDistance = sourceBody.minMoveDistance;
            var motor = racer.AddComponent<NativeKartMotor>();
            motor.stats = settings;
            motor.UseFixedVerificationClock = true;
            hit = racer.AddComponent<NativeKartHit>();
            effects = racer.AddComponent<NativeKartItemEffects>();
            motor.ResetAt(racer.transform.position,Quaternion.identity,false);
            return motor;
        }

        static IEnumerator VerifyAbilityInteractions(NativeRace race, NativeItemCatalog catalog,
            NativeKartMotor reference, List<Check> checks)
        {
            var roster = race.vehicleRoster;
            var bike = roster && roster.vehicles != null
                ? roster.vehicles.FirstOrDefault(vehicle => vehicle != null && vehicle.id == "Bike") : null;
            var broom = roster && roster.vehicles != null
                ? roster.vehicles.FirstOrDefault(vehicle => vehicle != null && vehicle.id == "Broom") : null;
            Add(checks,"ability-original-vehicles",bike != null && bike.settings != null &&
                broom != null && broom.settings != null,"Bike and Broom definitions from original roster");
            if (bike == null || bike.settings == null || broom == null || broom.settings == null)
                yield break;

            var bikeRoot = new GameObject("ItemBikeAbilityFixture");
            try
            {
                var pilot = CreateAbilityPilot(reference,bike.settings,bikeRoot,
                    new Vector3(23000f,1000f,10000f),out var hit,out var effects);
                Physics.SyncTransforms();
                for (int i = 0; i < 120 && !pilot.Grounded; i++) yield return new WaitForFixedUpdate();
                pilot.driving = true;
                bool power = effects.TryUse(catalog.Require("Power"),null);
                pilot.WheelieInputVertical(1f);
                yield return new WaitForFixedUpdate();
                pilot.WheelieInputVertical(0f);
                pilot.WheelieInputVertical(-1f);
                yield return new WaitForFixedUpdate();
                pilot.WheelieInputVertical(0f);
                pilot.WheelieInputVertical(1f);
                bool wheelie = pilot.IsWheelie;
                bool frozen = hit.FreezeFor(.5f);
                Add(checks,"bike-power-wheelie-freeze",pilot.Grounded && power && wheelie &&
                    frozen && !pilot.IsWheelie && effects.IsActive("Power") &&
                    Equal(pilot.stats._topSpeed,bike.settings._topSpeed + catalog.Require("Power").topSpeedAdd),
                    "wheelie starts under Power, hit freeze stops wheelie while Power remains active");
                effects.ResetEffects();
                Add(checks,"bike-power-restored",pilot.stats == bike.settings,
                    "Power reset restores Bike settings identity");
                hit.ResetState();
                bool fullPower = effects.TryUse(catalog.Require("FullPower"),null);
                Add(checks,"bike-fullpower-invulnerable",fullPower && hit.Invulnerable &&
                    !hit.FreezeFor(.75f),"FullPower blocks a freeze hit on a vehicle ability pilot");
            }
            finally { UnityEngine.Object.Destroy(bikeRoot); }
            yield return null;

            var broomRoot = new GameObject("ItemBroomAbilityFixture");
            try
            {
                var pilot = CreateAbilityPilot(reference,broom.settings,broomRoot,
                    new Vector3(20000f,1000f,10000f),out _,out var effects);
                var moduleFx = pilot.gameObject.AddComponent<NativeVehicleModuleEffects>();
                moduleFx.binding = broom.moduleEffects;
                Physics.SyncTransforms();
                for (int i = 0; i < 120 && !pilot.Grounded; i++) yield return new WaitForFixedUpdate();
                pilot.driving = true;
                pilot.throttle = .5f;
                for (int i = 0; i < 300 && !pilot.ModuleReady; i++) yield return new WaitForFixedUpdate();
                bool ready = pilot.Grounded && pilot.ModuleReady;
                bool wind = effects.TryUse(catalog.Require("WindBoost"),null);
                pilot.jump = true;
                yield return new WaitForFixedUpdate();
                float before = Vector3.ProjectOnPlane(pilot.PhysicsVelocity,Vector3.up).magnitude;
                for (int i = 0; i < 60 && pilot.ModuleTriggerCount == 0; i++)
                    yield return new WaitForFixedUpdate();
                float after = Vector3.ProjectOnPlane(pilot.PhysicsVelocity,Vector3.up).magnitude;
                Add(checks,"broom-windboost-air-module",ready && wind &&
                    pilot.ModuleTriggerCount == 1 && pilot.ModuleBoostRemaining > 0f &&
                    effects.IsActive("WindBoost") && after > before + 5f &&
                    Equal(pilot.stats._topSpeed,broom.settings._topSpeed + catalog.Require("WindBoost").topSpeedAdd),
                    "charged Broom air module triggers while WindBoost is active; speed="+before+"->"+after);
                effects.ResetEffects();
                Add(checks,"broom-windboost-restored",pilot.stats == broom.settings,
                    "WindBoost reset restores Broom settings identity");
            }
            finally { UnityEngine.Object.Destroy(broomRoot); }
        }

        public static IEnumerator Run(NativeRace race, string reportDirectory)
        {
            Directory.CreateDirectory(reportDirectory);
            var checks = new List<Check>();
            var results = new List<ItemResult>();
            var captures = new List<string>();
            var catalog = race ? race.GetComponent<NativeItemCatalog>() : null;
            var world = race ? race.GetComponent<NativeItemWorld>() : null;
            var owner = race && race.racers != null && race.racers.Length > 0 ? race.racers[0] : null;
            var target = race && race.racers != null && race.racers.Length > 1 ? race.racers[1] : null;
            var inventory = owner ? owner.GetComponent<NativeKartItemInventory>() : null;
            var ownerEffects = owner ? owner.GetComponent<NativeKartItemEffects>() : null;
            var targetEffects = target ? target.GetComponent<NativeKartItemEffects>() : null;
            var ownerHit = owner ? owner.GetComponent<NativeKartHit>() : null;
            var targetHit = target ? target.GetComponent<NativeKartHit>() : null;
            float savedScale = Time.timeScale;
            bool ownerEnabled = owner && owner.enabled, targetEnabled = target && target.enabled;
            bool targetDriving = target && target.driving;
            float targetThrottle = target ? target.throttle : 0f;
            NativeKartSettings savedOwnerStats = owner ? owner.stats : null;
            Camera itemCamera = null;
            Vector3 ownerPosition = owner ? owner.transform.position : Vector3.zero;
            Vector3 targetPosition = target ? target.transform.position : Vector3.zero;
            Quaternion ownerRotation = owner ? owner.transform.rotation : Quaternion.identity;
            Quaternion targetRotation = target ? target.transform.rotation : Quaternion.identity;
            var others = race && race.racers != null ? race.racers.Skip(2).ToArray() : Array.Empty<NativeKartMotor>();
            var otherPositions = others.Select(motor => motor ? motor.transform.position : Vector3.zero).ToArray();
            var otherRotations = others.Select(motor => motor ? motor.transform.rotation : Quaternion.identity).ToArray();
            var otherEnabled = others.Select(motor => motor && motor.enabled).ToArray();
            var otherDriving = others.Select(motor => motor && motor.driving).ToArray();
            try
            {
                Time.timeScale = 1f;
                if (race) yield return race.CheckOfudaSourceEligibilityLive((ok,name)=>Add(checks,"ofuda-production-source",ok,name));
                if (race) { race.SetVisualCapture("race",true); Time.timeScale = 1f; }
                Add(checks, "runtime-wiring", catalog && catalog.Count == 25 && world && inventory && target &&
                    ownerEffects && targetEffects && ownerHit && targetHit && inventory.World == world &&
                    inventory.Effects == ownerEffects, "catalog, four-car world, inventory and effect receivers");
                if (!catalog || !world || !inventory || !target || !ownerEffects || !targetEffects ||
                    !ownerHit || !targetHit) yield break;

                owner.enabled = false;
                target.enabled = false;
                for (int i = 0; i < others.Length; i++)
                    if (others[i])
                    {
                        others[i].enabled = false;
                        Place(others[i],ownerPosition + Vector3.right * (100f + i * 10f),
                            ownerRotation);
                    }
                itemCamera = new GameObject("Native Item Verification Camera").AddComponent<Camera>();
                itemCamera.enabled = false;
                itemCamera.clearFlags = CameraClearFlags.SolidColor;
                itemCamera.backgroundColor = new Color(.07f,.11f,.18f);
                itemCamera.cullingMask = -1;
                itemCamera.fieldOfView = 65f;
                itemCamera.nearClipPlane = .03f;
                itemCamera.farClipPlane = 500f;
                for (int rank = 0; rank < 4; rank++)
                    Add(checks, "rank-4-" + rank, NativeItemSelection.PoolIndex(rank,4) == rank * 2,
                        "original eight buckets map four racers to 0,2,4,6");
                Add(checks, "rank-8", Enumerable.Range(0,8).All(rank =>
                    NativeItemSelection.PoolIndex(rank,8) == rank), "zero-based eight-racer rank");
                Add(checks, "catalog-identity", catalog.entries != null && catalog.entries.Length == Original.Length &&
                    catalog.entries.All(entry => entry != null && Original.Any(expected => expected.id == entry.id)) &&
                    catalog.entries.Select(entry => entry.id).Distinct(StringComparer.Ordinal).Count() == Original.Length,
                    "all 25 original definitions appear once");

                foreach (var expected in Original)
                {
                    var entry = catalog.Find(expected.id);
                    bool exact = entry != null && entry.minRank == expected.min && entry.maxRank == expected.max &&
                        SameOptional(entry.characterConditionId,expected.character) &&
                        SameOptional(entry.giveOnDoneId,expected.next) &&
                        Equal(entry.characterCooldown, expected.cooldown) &&
                        Equal(entry.characterFixedChance, expected.chance) && entry.icon;
                    Add(checks, "asset-" + expected.id, exact,
                        "rank=" + expected.min + ".." + expected.max + " character=" + expected.character +
                        " cooldown=" + expected.cooldown + " chance=" + expected.chance + " next=" + expected.next);
                    if (entry == null) continue;
                    for (int bucket = 0; bucket < 8; bucket++)
                    {
                        bool inRange = expected.min <= bucket && bucket <= expected.max;
                        bool eligible = NativeItemSelection.Eligible(entry,bucket,
                            expected.character ?? "Reimu",0f,100f,0f,0f,100f);
                        Add(checks, "pool-" + expected.id + "-" + bucket, eligible == inRange,
                            "inclusive rank pool and deterministic chance roll 0");
                    }
                    if (expected.character != null)
                    {
                        int bucket = Mathf.Clamp(expected.min,0,7);
                        Add(checks, "exclusive-" + expected.id,
                            !NativeItemSelection.Eligible(entry,bucket,"NoSuchCharacter",0f,100f,0f,0f) &&
                            !NativeItemSelection.Eligible(entry,bucket,expected.character,1f,100f,0f,0f) &&
                            !NativeItemSelection.Eligible(entry,bucket,expected.character,0f,100f,0f,100f),
                            "other character, cooldown and failed chance reject exclusive item");
                    }
                }
                Add(checks, "ofuda-global-gate", !NativeItemSelection.Eligible(catalog.Require("Ofuda"),2,
                    "Reimu",0f,100f,0f,0f,19f) && !NativeItemSelection.Eligible(catalog.Require("Ofuda"),2,
                    "Reimu",0f,40f,41f,0f,100f) && NativeItemSelection.Eligible(catalog.Require("Ofuda"),2,
                    "Reimu",0f,40f,0f,0f,20f), "leader time threshold 20 and global cooldown use independent clocks");
                for (int rank = 0; rank < 4; rank++)
                {
                    var chosen = NativeItemSelection.Choose(catalog,rank,4,"Reimu",0f,100f,0f,100f);
                    int bucket = NativeItemSelection.PoolIndex(rank,4);
                    Add(checks, "choose-" + rank, chosen != null && chosen.minRank <= bucket &&
                        bucket <= chosen.maxRank && NativeItemSelection.Eligible(chosen,bucket,"Reimu",0f,100f,0f,0f,100f),
                        "returned item belongs to mapped bucket");
                }

                Reset(inventory,world,ownerEffects,ownerHit);
                bool randomGiven = inventory.TryGive("Power",true);
                bool initiallyUndecided = randomGiven && !inventory.ItemDecided && !inventory.UseItem();
                yield return WaitScaled(2.15f);
                Add(checks,"two-second-decision",initiallyUndecided && inventory.ItemDecided &&
                    inventory.DecideCount == 1,"random pickup blocks use until its original two-second wait ends");
                ownerHit.FreezeFor(.5f);
                int frozenUseCount = inventory.UseCount;
                Add(checks,"frozen-use-block",inventory.Frozen && !inventory.UseItem() &&
                    inventory.UseCount == frozenUseCount,"hit freeze prevents inventory use");
                Reset(inventory,world,ownerEffects,ownerHit);
                ownerHit.FreezeFor(.5f);
                bool autoGiven = inventory.TryGive("DriftPlusPowerup",false);
                Add(checks,"frozen-use-at-start",autoGiven && inventory.State.used &&
                    ownerEffects.IsActive("DriftPlusPowerup"),
                    "original useAtStart runs when assigned during hit freeze");
                Reset(inventory,world,ownerEffects,ownerHit);

                Place(owner,ownerPosition,ownerRotation);
                Place(target,ownerPosition + owner.transform.forward * 5f,ownerRotation);
                foreach (var expected in Original)
                {
                    var entry = catalog.Find(expected.id);
                    if (entry == null) continue;
                    Reset(inventory,world,ownerEffects,ownerHit);
                    targetEffects.ResetEffects(); targetHit.ResetState();
                    int give0 = inventory.GiveCount, decide0 = inventory.DecideCount;
                    int use0 = inventory.UseCount, done0 = inventory.DoneCount;
                    var baseStats = owner.stats;
                    var result = new ItemResult { id = expected.id, family = entry.family.ToString() };
                    result.given = inventory.TryGive(expected.id,false);
                    yield return null;
                    result.decided = inventory.ItemDecided || inventory.DecideCount > decide0;
                    bool firstUse = entry.useAtStart ? inventory.UseCount > use0 : inventory.UseItem();
                    yield return null;
                    bool secondUse = false;
                    bool twoAction = entry.twoAction || entry.family == NativeItemFamily.Frog;
                    if (twoAction && inventory.HasItem)
                    {
                        var carried = Actors(owner,entry.id);
                        Add(checks,"carry-"+entry.id,carried.Any(actor => actor.Carried),
                            "first use creates a carried projectile");
                        secondUse = inventory.UseItem();
                        yield return null;
                    }
                    result.used = firstUse && (!twoAction || secondUse);
                    result.actorsOnUse = Actors(owner,entry.id).Length;
                    bool activeEffect = ownerEffects.IsActive(entry.id);
                    bool targetChanged = targetHit.Frozen || targetHit.Invulnerable || target.ControlsReversed;
                    result.interaction = activeEffect || result.actorsOnUse > 0 || targetChanged;
                    if (entry.id == "Power" || entry.id == "FullPower" || entry.id == "WindBoost" ||
                        entry.id == "YinYang" || entry.id == "YinYangHoming" ||
                        entry.id == "FreezeBomb" || entry.id == "Frog" ||
                        entry.id == "MarisaShield" || entry.id == "SwordSlash")
                        Add(checks,"capture-"+entry.id,Capture(itemCamera,owner,reportDirectory,entry.id,captures),
                            "fixed scene camera rendered original item and non-flat track/vehicle pixels");
                    if (entry.family == NativeItemFamily.Throw || entry.family == NativeItemFamily.Frog ||
                        entry.family == NativeItemFamily.Shield || entry.family == NativeItemFamily.Melee)
                        Add(checks,"spawn-"+entry.id,result.interaction,
                            "world actor, physical hit or visible status follows use");
                    if (entry.family == NativeItemFamily.Boost || entry.family == NativeItemFamily.FullPower ||
                        entry.family == NativeItemFamily.WindBoost)
                        Add(checks,"effect-"+entry.id,activeEffect && owner.stats != baseStats &&
                            Equal(owner.stats._topSpeed,baseStats._topSpeed + entry.topSpeedAdd) &&
                            Equal(owner.stats._topSpeedGrass,baseStats._topSpeedGrass + entry.topSpeedAdd) &&
                            Equal(owner.stats._acceleration,baseStats._acceleration + entry.accelAdd) &&
                            Equal(owner.stats._driftSteerGain,baseStats._driftSteerGain + entry.driftSteerGain),
                            "active modifier applies the original asset's four stat deltas");
                    float timeout = entry.duration > 0f ? entry.duration + 1.5f : 1.5f;
                    float until = Time.time + Mathf.Clamp(timeout,1.5f,22f);
                    while (Time.time < until && inventory.DoneCount == done0) yield return null;
                    result.completed = inventory.DoneCount > done0;
                    result.finalId = inventory.CurrentId;
                    result.actorsAfter = Actors(owner,entry.id).Length;
                    result.giveDelta = inventory.GiveCount - give0;
                    result.decideDelta = inventory.DecideCount - decide0;
                    result.useDelta = inventory.UseCount - use0;
                    result.doneDelta = inventory.DoneCount - done0;
                    results.Add(result);
                    Add(checks,"lifecycle-"+entry.id,result.given && result.decided && result.used &&
                        result.completed && result.giveDelta >= 1 && result.useDelta >= 1,
                        "give="+result.giveDelta+" decide="+result.decideDelta+
                        " use="+result.useDelta+" done="+result.doneDelta+" current="+result.finalId);
                    if (entry.family == NativeItemFamily.Boost || entry.family == NativeItemFamily.FullPower ||
                        entry.family == NativeItemFamily.WindBoost)
                        Add(checks,"effect-finish-"+entry.id,!ownerEffects.IsActive(entry.id) &&
                            owner.stats == baseStats,"timed modifier restores original stats on completion");
                    if (expected.next != null)
                        Add(checks,"chain-"+entry.id,result.finalId == expected.next &&
                            inventory.ItemDecided && result.giveDelta >= 2,
                            "giveOnDone starts the next item immediately");
                }

                Reset(inventory,world,ownerEffects,ownerHit);
                targetEffects.ResetEffects(); targetHit.ResetState();
                Place(target,owner.transform.position + owner.transform.forward * 4f,owner.transform.rotation);
                int damageEvents = 0;
                Action<float> damaged = _ => damageEvents++;
                targetHit.Damaged += damaged;
                inventory.TryGive("YinYang",false);
                inventory.UseItem(); inventory.UseItem();
                yield return WaitScaled(.6f);
                targetHit.Damaged -= damaged;
                Add(checks,"projectile-live-hit",damageEvents > 0 && targetHit.Frozen,
                    "launched YinYang traverses the world and damages the target racer");
                foreach (string id in new[] { "YinYangHoming", "YinYangHoming2", "Ofuda", "Insanity" })
                {
                    Reset(inventory,world,ownerEffects,ownerHit);
                    targetEffects.ResetEffects(); targetHit.ResetState();
                    Place(target,owner.transform.position + owner.transform.forward * 8f,
                        owner.transform.rotation);
                    int hits = 0;
                    Action<float> onHit = _ => hits++;
                    targetHit.Damaged += onHit;
                    var source = catalog.Require(id);
                    bool given = inventory.TryGive(id,false);
                    bool used = given && inventory.UseItem();
                    if (source.twoAction) used &= inventory.UseItem();
                    int launched = Actors(owner,id).Length;
                    float hitUntil = Time.time + Mathf.Max(4f,
                        source.projectile == null ? 4f : source.projectile.timeBeforeExplosion + 3f);
                    while (Time.time < hitUntil && hits == 0 && !target.ControlsReversed)
                        yield return null;
                    targetHit.Damaged -= onHit;
                    Add(checks,"homing-live-hit-"+id,used && launched > 0 &&
                        (id == "Insanity" ? target.ControlsReversed : hits > 0 && targetHit.Frozen),
                        "launched original projectile tracks and reaches the racer; actors="+launched+
                        " hits="+hits+" reversed="+target.ControlsReversed);
                }
                foreach (var entry in catalog.entries)
                {
                    if (entry == null || (entry.family != NativeItemFamily.Throw &&
                        entry.family != NativeItemFamily.Frog && entry.family != NativeItemFamily.Melee)) continue;
                    Reset(inventory,world,ownerEffects,ownerHit);
                    targetEffects.ResetEffects(); targetHit.ResetState();
                    float offset = entry.family == NativeItemFamily.Melee ? 20f : 10f;
                    Place(target,owner.transform.position + owner.transform.forward * offset,
                        owner.transform.rotation);
                    int hits = 0;
                    Action<float> onHit = _ => hits++;
                    targetHit.Damaged += onHit;
                    Vector3 spawn = target.transform.position + Vector3.up -
                        owner.transform.forward * (entry.family == NativeItemFamily.Melee ? 2f : 1f);
                    var actor = world.Spawn(entry.id,owner,spawn,owner.transform.rotation,
                        owner.transform.forward * 2f);
                    bool spawned = actor && actor.ItemId == entry.id;
                    if (entry.family == NativeItemFamily.Melee && actor)
                    {
                        yield return null;
                        Add(checks,"sword-outside-source-range",hits == 0,
                            "target 20 m away remains outside both original sword colliders");
                        var targetBody = target.GetComponent<CharacterController>();
                        var swordBoxes = actor.GetComponentsInChildren<BoxCollider>(true);
                        var blade = swordBoxes.OrderByDescending(box =>
                            box.bounds.size.sqrMagnitude).FirstOrDefault();
                        Add(checks,"sword-source-collider",blade && swordBoxes.Length >= 2,
                            "original sword exposes two source BoxColliders");
                        if (blade)
                            Place(target,blade.bounds.center - Vector3.up *
                                (targetBody ? targetBody.center.y : 1f),owner.transform.rotation);
                    }
                    float delay = entry.projectile != null
                        ? entry.projectile.timeBeforeExplosion + 3f : 1.5f;
                    float untilHit = Time.time + Mathf.Max(1.5f,delay);
                    while (Time.time < untilHit && hits == 0 && !target.ControlsReversed)
                        yield return null;
                    targetHit.Damaged -= onHit;
                    bool affected = entry.id == "Insanity"
                        ? target.ControlsReversed : hits > 0 && targetHit.Frozen;
                    Add(checks,"world-hit-"+entry.id,spawned && affected,
                        "real actor Update/collision path; damage events="+hits+
                        " reversed="+target.ControlsReversed);
                    var itemRecord = results.FirstOrDefault(item => item.id == entry.id);
                    if (itemRecord != null) itemRecord.interaction = spawned && affected;
                }
                Reset(inventory,world,ownerEffects,ownerHit);
                targetEffects.ResetEffects(); targetHit.ResetState();
                inventory.TryGive("MarisaShield",false);
                bool shieldUsed = inventory.UseItem();
                yield return null;
                var shields = Actors(owner,"MarisaShield").Where(actor =>
                    actor.Mode == NativeItemActorMode.Shield).ToArray();
                NativeItemActor hostile = shields.Length > 0
                    ? world.Spawn("YinYang",target,shields[0].transform.position,
                        shields[0].transform.rotation,Vector3.zero) : null;
                bool hostileSpawned = hostile;
                yield return new WaitForFixedUpdate();
                yield return null;
                Add(checks,"shield-blocks-projectile",shieldUsed && shields.Length > 0 &&
                    hostileSpawned && (!hostile || hostile.Finished) &&
                    (!shields[0] || shields[0].Finished) && !ownerHit.Frozen,
                    "a hostile world projectile is destroyed by the orbiting original shield");
                Reset(inventory,world,ownerEffects,ownerHit);
                targetEffects.ResetEffects(); targetHit.ResetState();
                inventory.TryGive("FreezeBomb",false); inventory.UseItem();
                yield return null;
                Add(checks,"freeze-area-hit",targetHit.Frozen,
                    "FreezeBomb area freezes a second racer within its original 25 m range");
                Reset(inventory,world,ownerEffects,ownerHit);
                targetEffects.ResetEffects(); targetHit.ResetState();
                Place(target,owner.transform.position + owner.transform.forward * 5f,
                    owner.transform.rotation);
                target.enabled = true;
                target.driving = true;
                target.throttle = 1f;
                yield return WaitScaled(1.2f);
                target.enabled = false;
                target.driving = false;
                target.throttle = 0f;
                float speedBeforeBomb = target.Speed;
                Place(target,owner.transform.position + owner.transform.forward * 5f,
                    owner.transform.rotation);
                inventory.TryGive("Bomb",false);
                bool bombUsed = inventory.UseItem();
                yield return null;
                bool bombStopped = speedBeforeBomb > 1f && bombUsed && target.Speed < .01f;
                var bombRecord = results.FirstOrDefault(item => item.id == "Bomb");
                if (bombRecord != null) bombRecord.interaction = bombStopped;
                Add(checks,"bomb-stops-moving-racer",bombStopped,"target starts at speed="+speedBeforeBomb+
                    " and original 25 m area stops it");
                Reset(inventory,world,ownerEffects,ownerHit);
                targetEffects.ResetEffects(); targetHit.ResetState();
                inventory.TryGive("FullPower",false); inventory.UseItem();
                yield return null;
                Add(checks,"full-power-invulnerable",ownerHit.Invulnerable &&
                    !ownerHit.TakeDamage(),"FullPower blocks incoming damage");
                Reset(inventory,world,ownerEffects,ownerHit);
                targetEffects.ResetEffects(); targetHit.ResetState();
                Add(checks,"reverse-status",targetEffects.TryReceiveHit("Insanity",owner,1f,
                    NativeItemImpactKind.Insanity) && target.ControlsReversed,
                    "Insanity hit reverses control through the shared hit receiver");
                targetEffects.ResetEffects();
                Add(checks,"reverse-cleanup",!target.ControlsReversed,
                    "replay cleanup removes reversed controls");

                var vehicles = race.vehicleRoster && race.vehicleRoster.vehicles != null
                    ? race.vehicleRoster.vehicles : Array.Empty<NativeVehicleClassEntry>();
                var wheelie = vehicles.FirstOrDefault(vehicle => vehicle != null &&
                    vehicle.settings != null && vehicle.settings._canWheelie);
                var module = vehicles.FirstOrDefault(vehicle => vehicle != null &&
                    vehicle.settings != null && vehicle.settings._moduleKind != NativeVehicleModuleKind.None);
                Add(checks,"vehicle-ability-fixtures",wheelie != null && module != null,
                    "roster contains a wheelie vehicle and a module vehicle");
                if (wheelie != null)
                {
                    Reset(inventory,world,ownerEffects,ownerHit);
                    owner.stats = wheelie.settings;
                    inventory.TryGive("Power",false);
                    bool powered = inventory.UseItem();
                    Add(checks,"wheelie-plus-power",powered && owner.stats._canWheelie &&
                        Equal(owner.stats._wheelieSpeedAdd,wheelie.settings._wheelieSpeedAdd) &&
                        Equal(owner.stats._topSpeed,wheelie.settings._topSpeed + catalog.Require("Power").topSpeedAdd),
                        "Power adds speed while the original wheelie ability parameters remain usable");
                    Reset(inventory,world,ownerEffects,ownerHit);
                    Add(checks,"wheelie-plus-power-clean",owner.stats == wheelie.settings,
                        "reset restores the exact original vehicle settings instance");
                }
                if (module != null)
                {
                    Reset(inventory,world,ownerEffects,ownerHit);
                    owner.stats = module.settings;
                    inventory.TryGive("WindBoost",false);
                    bool boosted = inventory.UseItem();
                    Add(checks,"module-plus-windboost",boosted &&
                        owner.stats._moduleKind == module.settings._moduleKind &&
                        Equal(owner.stats._moduleSpeedBoost,module.settings._moduleSpeedBoost) &&
                        Equal(owner.stats._topSpeed,module.settings._topSpeed + catalog.Require("WindBoost").topSpeedAdd),
                        "WindBoost applies with the vehicle module configuration intact");
                    Reset(inventory,world,ownerEffects,ownerHit);
                    Add(checks,"module-plus-windboost-clean",owner.stats == module.settings,
                        "reset restores the exact original module vehicle settings instance");
                }
                owner.stats = savedOwnerStats;
                yield return VerifyAbilityInteractions(race,catalog,owner,checks);

                inventory.TryGive("Power",false); inventory.UseItem();
                int pausedDone = inventory.DoneCount;
                bool pausedActive = ownerEffects.IsActive("Power");
                Time.timeScale = 0f;
                yield return new WaitForSecondsRealtime(.25f);
                Add(checks,"pause-clock",pausedActive && ownerEffects.IsActive("Power") &&
                    inventory.DoneCount == pausedDone,"scaled item duration holds while paused");
                Time.timeScale = 1f;

                Reset(inventory,world,ownerEffects,ownerHit);
                targetEffects.ResetEffects(); targetHit.ResetState();
                Vector3 isolated = owner.transform.position + Vector3.right * 1000f + Vector3.up * 20f;
                var finiteActors = new List<NativeItemActor>();
                var persistentActors = new List<NativeItemActor>();
                var persistentDurations = new List<string>();
                float longestFinite = 0f;
                var lifetimeRacers = world.racers;
                var lifetimeRoute = world.route;
                world.racers = Array.Empty<NativeKartMotor>();
                world.route = Array.Empty<Vector3>();
                foreach (var entry in catalog.entries)
                {
                    if (entry == null || (entry.family != NativeItemFamily.Throw &&
                        entry.family != NativeItemFamily.Frog && entry.family != NativeItemFamily.Melee &&
                        entry.family != NativeItemFamily.Shield)) continue;
                    int before = world.ActiveActorCount;
                    world.Spawn(entry.id,owner,isolated + Vector3.right *
                        (finiteActors.Count + persistentActors.Count) * 8f,
                        owner.transform.rotation,owner.transform.forward * 2f);
                    var spawned = Actors(owner,entry.id);
                    Add(checks,"world-spawn-"+entry.id,world.ActiveActorCount > before && spawned.Length > 0,
                        "native actor exists for an original visual prefab");
                    float duration = entry.family == NativeItemFamily.Throw
                        ? entry.projectile == null ? 6f : entry.projectile.duration
                        : entry.family == NativeItemFamily.Frog
                            ? entry.frog == null ? 12f : entry.frog.duration : entry.duration;
                    if (duration >= 10000f)
                    {
                        persistentActors.AddRange(spawned);
                        persistentDurations.Add(entry.id+":"+duration);
                    }
                    else
                    {
                        finiteActors.AddRange(spawned);
                        longestFinite = Mathf.Max(longestFinite,Mathf.Max(.1f,duration));
                    }
                }
                Add(checks,"world-finite-lifetime-budget",finiteActors.Count > 0 &&
                    longestFinite <= 999f,"maximum finite source duration="+longestFinite+" seconds; expiry clock accelerated 20x");
                float lifetimeScale = Time.timeScale;
                float lifetimeFixedStep = Time.fixedDeltaTime;
                try
                {
                    Time.timeScale = 20f;
                    Time.fixedDeltaTime = lifetimeFixedStep * 20f;
                    yield return WaitScaled(longestFinite + 1.5f);
                }
                finally
                {
                    Time.timeScale = lifetimeScale;
                    Time.fixedDeltaTime = lifetimeFixedStep;
                    world.racers = lifetimeRacers;
                    world.route = lifetimeRoute;
                }
                int finiteActive = finiteActors.Count(actor => actor && !actor.Finished);
                int persistentActive = persistentActors.Count(actor => actor && !actor.Finished);
                Add(checks,"world-natural-expiry",finiteActive == 0 &&
                    world.ActiveActorCount == persistentActive,
                    "finite actors expired="+(finiteActors.Count-finiteActive)+"/"+finiteActors.Count+
                    " world active="+world.ActiveActorCount);
                Add(checks,"world-persistent-source-duration",persistentActors.Count > 0 &&
                    persistentActive == persistentActors.Count,
                    "original long-lived actors remain active until cleanup: "+
                    string.Join(",",persistentDurations));
                world.ResetWorld();
                yield return null;
                Add(checks,"world-persistent-reset-clean",world.ActiveActorCount == 0,
                    "explicit world reset destroys all source-long-lived actors");

                var ownerActor = world.Spawn("YinYang",owner,isolated,Quaternion.identity,Vector3.zero);
                var otherActor = world.Spawn("YinYang",target,isolated + Vector3.right * 15f,
                    Quaternion.identity,Vector3.zero);
                bool bothSpawned = ownerActor && otherActor && world.ActiveActorCount >= 2;
                inventory.ResetItems();
                Add(checks,"owner-reset-isolation",bothSpawned && (!ownerActor || ownerActor.Finished) &&
                    otherActor && !otherActor.Finished && world.ActiveActorCount == 1,
                    "reset removes only the owner's actor while preserving another racer's actor");
                world.ResetWorld();
                yield return null;
                Add(checks,"world-reset-clears-all",world.ActiveActorCount == 0,
                    "world reset clears remaining actor and effects");
                race.ResetRace(false);
                yield return null;
                Add(checks,"race-restart-clean",!inventory.HasItem && ownerEffects.ActiveCount == 0 &&
                    world.ActiveActorCount == 0 && !ownerHit.Frozen && !ownerHit.Invulnerable &&
                    !target.ControlsReversed,"restart clears item slot, world actors and statuses");
            }
            finally
            {
                Time.timeScale = savedScale;
                if (inventory) inventory.ResetItems();
                if (world) world.ResetWorld();
                if (ownerEffects) ownerEffects.ResetEffects();
                if (targetEffects) targetEffects.ResetEffects();
                if (ownerHit) ownerHit.ResetState();
                if (targetHit) targetHit.ResetState();
                if (owner)
                {
                    owner.stats = savedOwnerStats;
                    Place(owner,ownerPosition,ownerRotation);
                    owner.enabled = ownerEnabled;
                }
                if (itemCamera) UnityEngine.Object.Destroy(itemCamera.gameObject);
                if (target)
                {
                    Place(target,targetPosition,targetRotation);
                    target.enabled = targetEnabled;
                    target.driving = targetDriving;
                    target.throttle = targetThrottle;
                }
                for (int i = 0; i < others.Length; i++)
                    if (others[i])
                    {
                        Place(others[i],otherPositions[i],otherRotations[i]);
                        others[i].enabled = otherEnabled[i];
                        others[i].driving = otherDriving[i];
                    }
                Add(checks,"runtime-errors",race && race.VisualErrorCount == 0,
                    "Unity logged runtime errors="+(race ? race.VisualErrorCount : -1));
                var report = new Report
                {
                    version = Application.version, track = race ? race.trackId : null,
                    catalogCount = catalog ? catalog.Count : 0,
                    checks = checks.ToArray(), items = results.ToArray(), captures = captures.ToArray(),
                    checksPassed = checks.Count(check => check.passed), checksTotal = checks.Count,
                    passed = checks.Count > 0 && checks.All(check => check.passed) && results.Count == Original.Length
                };
                File.WriteAllText(Path.Combine(reportDirectory,"native-item-coverage-report.json"),
                    JsonUtility.ToJson(report,true));
                Debug.Log("NATIVE_ITEM_COVERAGE " + (report.passed ? "PASS" : "FAIL") +
                    " items="+results.Count+" checks="+report.checksPassed+"/"+report.checksTotal);
                Application.Quit(report.passed ? 0 : 2);
            }
        }
    }
}
