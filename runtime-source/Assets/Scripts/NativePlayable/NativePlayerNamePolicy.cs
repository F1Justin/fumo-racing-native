using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Fumo.NativePlayable
{
    // Both brokers use the same pinned Unicode tables; never truncate UTF-16/code points independently.
    public static class NativePlayerNamePolicy
    {
        [Serializable] sealed class Range { public int lo, hi, value; }
        [Serializable] sealed class Policy
        { public int version, maxCharacters, maxUtf8Bytes; public string unicodeVersion; public Range[] allowed, grapheme, conjunct; }
        static Policy policy;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static Policy Rules
        {
            get
            {
                if (policy != null) return policy;
                var resource = NativeContent.Load<TextAsset>("NativeSettings/player-name-policy");
                if (!resource) throw new InvalidOperationException("Player name policy resource missing");
                return policy = JsonUtility.FromJson<Policy>(resource.text);
            }
        }
        static int Lookup(Range[] ranges, int scalar)
        {
            int lo = 0, hi = ranges.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2; var range = ranges[mid];
                if (scalar < range.lo) hi = mid - 1;
                else if (scalar > range.hi) lo = mid + 1;
                else return range.value;
            }
            return 0;
        }
        static bool Conjunct(List<int> text, int index)
        {
            if (Lookup(Rules.conjunct, text[index]) != 1) return false;
            bool linker = false;
            for (int i = index - 1; i >= 0; i--)
            {
                int value = Lookup(Rules.conjunct, text[i]);
                if (value == 3) linker = true;
                else if (value != 2) return value == 1 && linker;
            }
            return false;
        }
        static int CharacterCount(List<int> text)
        {
            int count = 0, previous = 0;
            for (int i = 0; i < text.Count; i++)
            {
                int current = Lookup(Rules.grapheme, text[i]); bool join = false;
                if (i > 0 && previous != 1 && previous != 2 && previous != 3 && current != 1 && current != 2 && current != 3)
                    join = (previous == 4 && (current == 4 || current == 5 || current == 7 || current == 8))
                        || ((previous == 7 || previous == 5) && (current == 5 || current == 6))
                        || ((previous == 8 || previous == 6) && current == 6)
                        || current == 9 || current == 11 || previous == 12 || Conjunct(text, i);
                if (!join) count++;
                previous = current;
            }
            return count;
        }
        public static bool TryNormalize(string raw, out string name, out string error)
        {
            name = null; error = null;
            if (string.IsNullOrEmpty(raw)) { error = "Please enter your player name."; return false; }
            if (raw.Length > 256) { error = "Player ID is too long (32 characters, 128 UTF-8 bytes maximum)."; return false; }
            try { name = raw.Normalize(NormalizationForm.FormC); Utf8.GetByteCount(name); }
            catch (ArgumentException) { error = "Player ID contains invalid Unicode."; return false; }
            var scalars = new List<int>(); bool hasLetterOrDigit = false, markBase = false;
            for (int i = 0; i < name.Length; i++)
            {
                int scalar = char.ConvertToUtf32(name, i); if (scalar > 0xFFFF) i++;
                int kind = Lookup(Rules.allowed, scalar);
                bool punctuation = scalar == '_' || scalar == '-' || scalar == '.';
                if (kind == 0 && !punctuation)
                { error = "Player ID allows letters, digits, underscore, hyphen and dot; no spaces, emoji or invisible characters."; return false; }
                if (kind == 3 && !markBase)
                { error = "Accents must follow a letter in the player ID."; return false; }
                if (kind == 1 || kind == 2) hasLetterOrDigit = true;
                if (kind != 3) markBase = kind == 1;
                scalars.Add(scalar);
            }
            if (!hasLetterOrDigit) { error = "Player ID must contain a letter or digit."; return false; }
            if (CharacterCount(scalars) > Rules.maxCharacters || Utf8.GetByteCount(name) > Rules.maxUtf8Bytes)
            { error = "Player ID is too long (32 characters, 128 UTF-8 bytes maximum)."; return false; }
            return true;
        }
        public static string Display(string raw) => TryNormalize(raw, out var name, out _) ? name : "Player";
    }
}
