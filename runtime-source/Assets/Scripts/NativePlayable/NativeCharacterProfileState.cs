using System;
namespace Fumo.NativePlayable
{
    public enum NativeCharacterProfileResult { Ignored, Opened, Moved, Applied, Rejected, Cancelled }
    [Serializable] public sealed class NativeCharacterProfileState
    {
        public readonly int HumanIndex;
        public bool Open { get; private set; }
        public int CurrentIndex { get; private set; } = -1;
        public int SelectedIndex { get; private set; } = -1;
        public NativeMenuPage ReturnPage { get; private set; }
        public int ReturnIndex { get; private set; }
        public NativeCharacterProfileState(int human) { if (human < 0 || human >= 4) throw new ArgumentOutOfRangeException(nameof(human)); HumanIndex = human; }
        public NativeCharacterProfileResult Config(bool joined, bool ready, int count, NativeMenuPage target, int targetIndex, Func<int,bool> apply)
        {
            if (ready || !joined) return NativeCharacterProfileResult.Ignored;
            if (count < 0) return NativeCharacterProfileResult.Rejected;
            if (Open) return Confirm(apply);
            if (CurrentIndex >= count) CurrentIndex = -1;
            ReturnPage = target; ReturnIndex = targetIndex; Open = true;
            return NativeCharacterProfileResult.Opened;
        }
        public NativeCharacterProfileResult Navigate(int direction, int count)
        {
            if (!Open || count <= 0 || (direction != 1 && direction != 3)) return NativeCharacterProfileResult.Ignored;
            CurrentIndex += direction == 1 ? 1 : -1;
            if (CurrentIndex < -1) CurrentIndex = count - 1;
            if (CurrentIndex >= count) CurrentIndex = -1;
            return NativeCharacterProfileResult.Moved;
        }
        public NativeCharacterProfileResult Confirm(Func<int,bool> apply)
        {
            if (!Open) return NativeCharacterProfileResult.Ignored;
            if (!apply(CurrentIndex)) return NativeCharacterProfileResult.Rejected;
            SelectedIndex = CurrentIndex; Open = false; return NativeCharacterProfileResult.Applied;
        }
        public NativeCharacterProfileResult Cancel()
        {
            if (!Open) return NativeCharacterProfileResult.Ignored;
            Open = false; return NativeCharacterProfileResult.Cancelled;
        }
    }
}
