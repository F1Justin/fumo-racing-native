using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartItemLifetimeVerification
    {
        public static IEnumerator Run(NativeKartItemInventory inventory, Action<string, bool, string> add)
        {
            inventory.ResetItems();
            yield return null;
            inventory.TryGive(NativeKartItemKind.YinYang);
            yield return new WaitForSeconds(2.05f);
            inventory.UseItem();
            var carried = UnityEngine.Object.FindObjectsOfType<NativeKartItemProjectile>()
                .FirstOrDefault(p => p.Owner == inventory.motor);
            var visual = carried ? carried.GetComponent<NativeKartItemVisual>() : null;
            add("original-projectile-visual-bindings", visual && visual.destroyEffect &&
                visual.rotationPerSecond == new Vector3(0f, 200f, 0f) && visual.effectDuration == 1f,
                "Original RotateOverTime and ItemDisapearFX serialized fields");
            if (!carried || !visual) yield break;
            yield return new WaitForEndOfFrame();
            Quaternion before = carried.transform.localRotation;
            float start = Time.time;
            yield return new WaitForSeconds(.1f);
            yield return new WaitForEndOfFrame();
            Quaternion expected = before * Quaternion.Euler(0f, 200f * (Time.time - start), 0f);
            add("carried-projectile-rotates", Quaternion.Angle(carried.transform.localRotation, expected) < 1f,
                "Live Update applies local Y rotation at 200 degrees per scaled second");
            Time.timeScale = 0f;
            yield return null;
            before = carried.transform.localRotation;
            for (int i = 0; i < 5; i++) yield return null;
            add("projectile-rotation-pauses", Quaternion.Angle(before, carried.transform.localRotation) < .001f,
                "Five real paused frames preserve the carried orientation");
            Time.timeScale = 1f;
            int effects = NativeKartItemVisual.EffectsCreated;
            UnityEngine.Object.Destroy(carried.gameObject);
            yield return null;
            yield return null;
            add("destroyed-carried-item-completes-slot", !inventory.HasItem && inventory.DoneCount == 1,
                "Original carried onDestroyed completes the item once");
            add("carried-destruction-creates-original-effect", NativeKartItemVisual.EffectsCreated == effects + 1,
                "Normal destruction instantiates original FX once");
            add("destroyed-carried-item-cannot-shoot", !inventory.UseItem(), "No phantom second shot after carried loss");
            inventory.TryGive(NativeKartItemKind.YinYang);
            yield return new WaitForSeconds(2.05f);
            inventory.UseItem();
            effects = NativeKartItemVisual.EffectsCreated;
            inventory.UseItem();
            yield return null;
            yield return null;
            add("throw-destroys-carried-with-effect-once", NativeKartItemVisual.EffectsCreated == effects + 1
                && !inventory.HasItem && inventory.DoneCount == 2, "Second use destroys carried instance and completes once");
            effects = NativeKartItemVisual.EffectsCreated;
            inventory.ResetItems();
            yield return null;
            yield return null;
            add("restart-suppresses-destruction-effects", NativeKartItemVisual.EffectsCreated == effects,
                "Race reset matches original LevelManager scene-cleanup suppression");
        }
    }
}
