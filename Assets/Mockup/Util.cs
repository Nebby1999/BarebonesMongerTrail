public static class Util
{
    public static float Remap(float value, float inMin, float inMax, float outMin, float outMax)
    {
        return outMin + (value - inMin) / (inMax - inMin) * (outMax - outMin);
    }
}