// Test-only stand-ins for the handful of UnityEngine members the engine-independent gameplay
// code touches. They exist so the headless harness can exercise real source files (layout
// geometry, timing) without a Unity install; the game itself always uses the real UnityEngine.
//
// Keep this minimal: anything added here must behave exactly like the Unity method it shadows,
// or the tests will assert against behaviour the game does not have.
namespace UnityEngine
{
    public static class Mathf
    {
        public static float Max(float a, float b) => a > b ? a : b;

        public static float Abs(float value) => value < 0f ? -value : value;

        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }

        public static int FloorToInt(float value)
        {
            int truncated = (int)value;
            return truncated > value ? truncated - 1 : truncated;
        }

        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static bool Approximately(float a, float b)
        {
            return Abs(b - a) < Max(1e-06f * Max(Abs(a), Abs(b)), 1.1754944e-38f * 8f);
        }
    }
}
