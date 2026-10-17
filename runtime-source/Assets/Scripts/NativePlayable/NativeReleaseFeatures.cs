namespace Fumo.NativePlayable
{
    // The build-only define leaves the Editor and later broadcast development enabled.
    public static class NativeReleaseFeatures
    {
        public static bool BroadcastEnabled
        {
            get
            {
#if FUMO_RELEASE_CORE
                return false;
#else
                return true;
#endif
            }
        }
        public static string Profile => BroadcastEnabled ? "development" : "core-first-release";
    }
}
