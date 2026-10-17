using System;
using System.Collections.Generic;

namespace Fumo.NativePlayable
{
    public static class NativeUfoRules
    {
        [Serializable] public sealed class Recipe { public string id, itemId; public int[] colors; public bool exclusive; }
        [Serializable] public sealed class State
        {
            public int[] colors;
            public int count;
            public string previewItemId;
            public int collected, used;
            public State(int capacity) { colors = new int[capacity]; for (int i = 0; i < capacity; i++) colors[i] = -1; }
        }
        public static bool Matches(int[] collected, int[] requirements)
        {
            if (collected == null || requirements == null) return false;
            var remaining = new List<int>(); foreach (int color in collected) if (color >= 0) remaining.Add(color);
            if (remaining.Count != requirements.Length) return false;
            foreach (int color in requirements) if (!remaining.Remove(color)) return false;
            return true;
        }
        public static string Resolve(Recipe[] recipes, int[] colors, string exclusiveId, string defaultId, out bool matched)
        {
            foreach (var recipe in recipes)
                if (recipe != null && Matches(colors, recipe.colors))
                {
                    string item = recipe.exclusive ? exclusiveId : recipe.itemId;
                    matched = !string.IsNullOrEmpty(item); return matched ? item : defaultId;
                }
            matched = false; return defaultId;
        }
        public static bool Collect(State state, int color, int colorCount, Recipe[] recipes, string exclusiveId, string defaultId)
        {
            if (state == null || state.colors == null || state.count >= state.colors.Length || color < 0 || color >= colorCount) return false;
            state.colors[state.count++] = color; state.collected++;
            string result = Resolve(recipes, state.colors, exclusiveId, defaultId, out bool matched);
            // PlayerUFOS retains the most recent successful recipe as additional colors arrive.
            if (string.IsNullOrEmpty(state.previewItemId) || matched) state.previewItemId = result;
            return true;
        }
        public static bool Use(State state, bool ordinaryItemOccupied, Func<string, bool> give)
        {
            if (state == null || state.count == 0 || ordinaryItemOccupied || give == null || !give(state.previewItemId)) return false;
            for (int i = 0; i < state.colors.Length; i++) state.colors[i] = -1;
            state.count = 0; state.previewItemId = null; state.used++; return true;
        }
        public static int NextColor(int current, int colorCount) => current + 1 >= colorCount ? 0 : current + 1;
    }
}
