using System;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed class NativeWaterSourceState : MonoBehaviour
    {
        [Serializable] public sealed class Scalar { public string name; public float value; }
        [Serializable] public sealed class Tint { public string name; public Color value; }
        [Serializable] public sealed class Image { public string name; public Texture value; public Vector2 scale, offset; }
        public Material material;
        public string sourcePath, sourceSHA256, expectedShader;
        public int renderQueue;
        public string[] keywords;
        public Scalar[] scalars;
        public Tint[] tints;
        public Image[] images;
    }

}
