using System;

namespace Fumo.NativePlayable
{
    public enum NativeKartItemKind
    {
        None, Power, YinYang, Power2, Power3, FullPower, YinYang2, YinYangHoming,
        YinYangHoming2, BlueStar, YellowStar, Frog, JumperFrog, WindBoost, Bomb,
        FreezeBomb, Ofuda, RockThrow, Insanity, Bnuuy, FantasySeal, SanaeSnakes,
        Shangai, DriftPlusPowerup, MarisaShield, SwordSlash
    }

    public enum NativeKartItemAction { None, StartPower, CarryYinYang, ThrowYinYang }

    [Serializable]
    public struct NativeKartItemState
    {
        public NativeKartItemKind kind;
        public float decideRemaining;
        public float powerRemaining;
        public bool decided;
        public bool used;
        public bool carried;
        public int giveCount, decideCount, useCount, doneCount;

        public bool HasItem => kind != NativeKartItemKind.None;

        public bool Give(NativeKartItemKind value, bool random = true)
        {
            if (HasItem || value == NativeKartItemKind.None) return false;
            kind = value;
            decideRemaining = random ? 2f : 0f;
            decided = !random;
            used = carried = false;
            if (!random) decideCount++;
            giveCount++;
            return true;
        }

        public bool Advance(float deltaTime)
        {
            if (deltaTime <= 0f) return false;
            if (HasItem && !decided)
            {
                decideRemaining -= deltaTime;
                if (decideRemaining <= 0f)
                {
                    decideRemaining = 0f;
                    decided = true;
                    decideCount++;
                }
            }
            if (kind == NativeKartItemKind.Power && used)
            {
                powerRemaining -= deltaTime;
                if (powerRemaining <= 0f)
                {
                    ClearCompleted();
                    return true;
                }
            }
            return false;
        }

        public NativeKartItemAction Use(bool frozen)
        {
            if (!HasItem || !decided || frozen) return NativeKartItemAction.None;
            if (kind == NativeKartItemKind.Power)
            {
                if (used) return NativeKartItemAction.None;
                used = true;
                powerRemaining = 3f;
                useCount++;
                return NativeKartItemAction.StartPower;
            }
            if (kind == NativeKartItemKind.YinYang)
            {
                useCount++;
                if (!carried)
                {
                    carried = true;
                    return NativeKartItemAction.CarryYinYang;
                }
                ClearCompleted();
                return NativeKartItemAction.ThrowYinYang;
            }
            return NativeKartItemAction.None;
        }

        public void ClearCompleted()
        {
            kind = NativeKartItemKind.None;
            decideRemaining = powerRemaining = 0f;
            decided = used = carried = false;
            doneCount++;
        }

        public bool BeginUse(bool frozen, bool carrying = false)
        {
            if (!HasItem || !decided || frozen || (used && !carried)) return false;
            used = true;
            carried = carrying;
            useCount++;
            return true;
        }

        public bool CompleteCarriedLoss()
        {
            if (kind != NativeKartItemKind.YinYang || !carried) return false;
            ClearCompleted();
            return true;
        }

        public void Reset()
        {
            kind = NativeKartItemKind.None;
            decideRemaining = powerRemaining = 0f;
            decided = used = carried = false;
            giveCount = decideCount = useCount = doneCount = 0;
        }
    }
}
