using System;
using System.Collections.Generic;
using System.Linq;

namespace Fumo.NativePlayable
{
    public sealed class NativeLocalSelectionState
    {
        public enum Stage { Character, Vehicle, Skin, Ready }
        public sealed class Box
        {
            public int InputUserId, Character, Vehicle, Skin;
            public int[] DeviceIds;
            public Stage Selection;
            public Box Copy() => new Box { InputUserId = InputUserId, Character = Character, Vehicle = Vehicle, Skin = Skin,
                DeviceIds = (int[])DeviceIds.Clone(), Selection = Selection };
        }
        readonly Box[] boxes = new Box[4];
        readonly Func<int, int[], bool> actualOwnerPaired;
        readonly Func<int, bool> characterEligible;
        readonly Func<int, int> vehicleCount, skinCount;
        readonly Func<int, int, int> characterNeighbor;
        bool advanced;
        public bool JoiningEnabled { get; private set; }
        public int MaxHumans { get; private set; }
        public int JoinedCount => boxes.Count(box => box != null);
        public bool AllReady => JoinedCount > 0 && boxes.Where(box => box != null).All(box => box.Selection == Stage.Ready);
        public event Action<int> Changed;
        public event Action<int, int> Arrow;
        public event Action<int, int> SoundRequested;
        public event Action AllReadyNextModule;
        public NativeLocalSelectionState(Func<int, int[], bool> actualOwnerPaired, Func<int, bool> characterEligible,
            Func<int, int> vehicleCount, Func<int, int> skinCount, Func<int, int, int> characterNeighbor)
        {
            this.actualOwnerPaired = actualOwnerPaired ?? throw new ArgumentNullException(nameof(actualOwnerPaired));
            this.characterEligible = characterEligible ?? throw new ArgumentNullException(nameof(characterEligible));
            this.vehicleCount = vehicleCount ?? throw new ArgumentNullException(nameof(vehicleCount));
            this.skinCount = skinCount ?? throw new ArgumentNullException(nameof(skinCount));
            this.characterNeighbor = characterNeighbor ?? throw new ArgumentNullException(nameof(characterNeighbor));
        }
        public Box ReadBox(int slot) => slot >= 0 && slot < boxes.Length ? boxes[slot]?.Copy() : null;
        public void Loaded(bool timeTrials, bool firstLoad)
        {
            MaxHumans = timeTrials ? 1 : 4;
            if (JoinedCount > MaxHumans) throw new InvalidOperationException("Original mode human limit violated by provider");
            advanced = false;
            JoiningEnabled = !timeTrials;
            for (int slot = 0; slot < boxes.Length; slot++)
                if (boxes[slot] != null)
                {
                    boxes[slot].Selection = Stage.Character;
                    if (firstLoad) boxes[slot].Skin = 0;
                    Changed?.Invoke(slot);
                }
        }
        public int JoinedOwner(int inputUserId) => Array.FindIndex(boxes, box => box != null && box.InputUserId == inputUserId);
        public bool JoinActualOwner(int inputUserId, int[] deviceIds, int startingCharacter, int retainedVehicle)
            => JoiningEnabled && AttachLoadedOwner(inputUserId, deviceIds, startingCharacter, retainedVehicle);
        public bool AttachLoadedOwner(int inputUserId, int[] deviceIds, int startingCharacter, int retainedVehicle, int retainedSkin = 0)
        {
            if (JoinedCount >= MaxHumans || JoinedOwner(inputUserId) >= 0 || inputUserId < 0
                || deviceIds == null || deviceIds.Length == 0 || deviceIds.Any(id => id < 0) || deviceIds.Distinct().Count() != deviceIds.Length
                || boxes.Where(box => box != null).Any(box => box.DeviceIds.Intersect(deviceIds).Any())
                || !actualOwnerPaired(inputUserId, deviceIds) || !characterEligible(startingCharacter)
                || retainedVehicle < 0 || retainedVehicle >= vehicleCount(startingCharacter) || retainedSkin < 0 || retainedSkin >= skinCount(startingCharacter)) return false;
            int slot = Array.FindIndex(boxes, box => box == null);
            boxes[slot] = new Box { InputUserId = inputUserId, DeviceIds = (int[])deviceIds.Clone(), Character = startingCharacter,
                Vehicle = retainedVehicle, Skin = retainedSkin, Selection = Stage.Character };
            advanced = false; Changed?.Invoke(slot); return true;
        }
        Box Owned(int inputUserId, out int slot)
        {
            slot = JoinedOwner(inputUserId);
            var box = slot < 0 ? null : boxes[slot];
            return box != null && actualOwnerPaired(inputUserId, box.DeviceIds) ? box : null;
        }
        public bool FocusCharacter(int inputUserId, int character)
        {
            var box = Owned(inputUserId, out int slot);
            if (box == null || box.Selection != Stage.Character || !characterEligible(character)) return false;
            box.Character = character; box.Skin = 0; Changed?.Invoke(slot); return true;
        }
        public bool MoveStarted(int inputUserId, int direction)
        {
            var box = Owned(inputUserId, out int slot);
            if (box == null || direction < 0 || direction > 3 || box.Selection == Stage.Ready) return false;
            if (box.Selection == Stage.Character)
            {
                int target = characterNeighbor(box.Character, direction);
                if (target < 0 || !characterEligible(target)) return false;
                box.Character = target; box.Skin = 0; SoundRequested?.Invoke(slot, 4);
            }
            else
            {
                int step = direction == 1 ? 1 : -1;
                if (box.Selection == Stage.Vehicle) box.Vehicle = (box.Vehicle + step + vehicleCount(box.Character)) % vehicleCount(box.Character);
                else box.Skin = (box.Skin + step + skinCount(box.Character)) % skinCount(box.Character);
                Arrow?.Invoke(slot, step); SoundRequested?.Invoke(slot, 7);
            }
            Changed?.Invoke(slot); return true;
        }
        public bool SubmitStarted(int inputUserId, bool emitSound = true)
        {
            var box = Owned(inputUserId, out int slot);
            if (box == null) return false;
            if(emitSound)SoundRequested?.Invoke(slot, box.Selection == Stage.Character ? 5 : 8);
            if (box.Selection == Stage.Ready) return true;
            box.Selection = box.Selection == Stage.Character ? Stage.Vehicle : box.Selection == Stage.Vehicle ? Stage.Skin : Stage.Ready;
            Changed?.Invoke(slot); return true;
        }
        public bool CancelPlayerBoxStarted(int inputUserId)
        {
            var box = Owned(inputUserId, out int slot);
            if (box == null || box.Selection == Stage.Character) return false;
            SoundRequested?.Invoke(slot, 0);
            if (box.Selection == Stage.Vehicle) { box.Selection = Stage.Character; box.Skin = 0; }
            else box.Selection = box.Selection == Stage.Skin ? Stage.Vehicle : Stage.Character;
            if (box.Selection == Stage.Character) box.Skin = 0;
            advanced = false; Changed?.Invoke(slot); return true;
        }
        public bool ActualPlayerLeft(int inputUserId)
        {
            int slot = JoinedOwner(inputUserId);
            if (slot < 0) return false;
            boxes[slot] = null; advanced = false; Changed?.Invoke(slot); return true;
        }
        public void ApplyActualPlayerOrder(int[] inputUserOrder)
        {
            if (inputUserOrder == null || inputUserOrder.Length != JoinedCount || inputUserOrder.Distinct().Count() != JoinedCount
                || inputUserOrder.Any(owner => JoinedOwner(owner) < 0)) throw new InvalidOperationException("Actual PlayerInput order differs from joined owners");
            var ordered = inputUserOrder.Select(owner => boxes[JoinedOwner(owner)]).ToArray();
            Array.Clear(boxes, 0, boxes.Length);
            for (int i = 0; i < ordered.Length; i++) boxes[i] = ordered[i];
            for (int i = 0; i < boxes.Length; i++) Changed?.Invoke(i);
        }
        public void Ended() { JoiningEnabled = false; }
        public bool UpdateAllReady()
        {
            if (advanced || !AllReady) return false;
            advanced = true; JoiningEnabled = false; AllReadyNextModule?.Invoke(); return true;
        }
    }
}
