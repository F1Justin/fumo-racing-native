using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativePlayerBoxSoundData : ScriptableObject
    {
        public AudioClip move;
        public AudioClip characterMove;
        public AudioClip characterSelect, playerBoxSelect, playerBoxCancel;
        static NativePlayerBoxSoundData original;
        public static AudioClip Move
        {
            get
            {
                if (!original) original = NativeContent.Load<NativePlayerBoxSoundData>("NativeSelectionPreview/PlayerBoxSounds");
                if (!original || !original.move) throw new System.InvalidOperationException("Original PlayerBox CharacterSubMove clip is missing");
                return original.move;
            }
        }
        public static AudioClip CharacterMove
        {
            get
            {
                if (!original) original = NativeContent.Load<NativePlayerBoxSoundData>("NativeSelectionPreview/PlayerBoxSounds");
                if (!original || !original.characterMove) throw new System.InvalidOperationException("Original CharacterMove clip is missing");
                return original.characterMove;
            }
        }
        static NativePlayerBoxSoundData Original
        {
            get
            {
                if (!original) original = NativeContent.Load<NativePlayerBoxSoundData>("NativeSelectionPreview/PlayerBoxSounds");
                if (!original) throw new System.InvalidOperationException("Original selection UI sound bindings are missing");
                return original;
            }
        }
        public static AudioClip CharacterSelect => Require(Original.characterSelect, "CharacterSelect");
        public static AudioClip PlayerBoxSelect => Require(Original.playerBoxSelect, "CharacterSubSelect");
        public static AudioClip PlayerBoxCancel => Require(Original.playerBoxCancel, "MenuMove");
        static AudioClip Require(AudioClip clip, string name)
        {
            if (!clip) throw new System.InvalidOperationException("Original selection clip is missing: " + name);
            return clip;
        }
    }
}
