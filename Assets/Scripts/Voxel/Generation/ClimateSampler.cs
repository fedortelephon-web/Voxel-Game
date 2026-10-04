using UnityEngine;

namespace Voxel
{
    /// <summary>
    /// Получает независимые климатические Noise Map по мировым координатам.
    /// </summary>
    public static class ClimateSampler
    {
        public static ClimatePoint Sample(
            int seed,
            int x,
            int z,
            float temperatureScale,
            float humidityScale,
            float continentalnessScale,
            float erosionScale)
        {
            float temperature = SampleNoise(
                seed,
                x,
                z,
                temperatureScale,
                0);

            float humidity = SampleNoise(
                seed,
                x,
                z,
                humidityScale,
                1);

            float continentalness = SampleNoise(
                seed,
                x,
                z,
                continentalnessScale,
                2);

            float erosion = SampleNoise(
                seed,
                x,
                z,
                erosionScale,
                3);

            return new ClimatePoint(
                temperature,
                humidity,
                continentalness,
                erosion);
        }

        private static float SampleNoise(
            int seed,
            int x,
            int z,
            float scale,
            int channel)
        {
            scale = Mathf.Max(scale, 0.0001f);

            float offsetX = GetSeedOffset(seed, channel * 2);
            float offsetZ = GetSeedOffset(seed, channel * 2 + 1);

            return Mathf.PerlinNoise(
                (x + offsetX) * scale,
                (z + offsetZ) * scale);
        }

        private static float GetSeedOffset(int value, int channel)
        {
            unchecked
            {
                int hash = value;

                hash ^= channel * 374761393;
                hash = hash * 668265263;
                hash ^= hash >> 13;
                hash = hash * 1274126177;
                hash ^= hash >> 16;

                return Mathf.Abs(hash % 100000);
            }
        }
    }
}