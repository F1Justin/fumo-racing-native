namespace Fumo.NativePlayable
{
    public sealed partial class NativeKartMotor
    {
        int insanityReverseGeneration;
        internal int AcquireInsanityReverse()
        { ControlReverseCount++; return insanityReverseGeneration; }
        internal void ReleaseInsanityReverse(int generation)
        { if (generation == insanityReverseGeneration) ControlReverseCount--; }
    }
}
