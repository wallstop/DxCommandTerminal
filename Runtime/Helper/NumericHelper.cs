namespace CommandTerminal.Helper
{
    public static class NumericHelper
    {
        public static float NormalizeToZero(ref float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                value = 0f;
            }

            return value;
        }
    }
}
