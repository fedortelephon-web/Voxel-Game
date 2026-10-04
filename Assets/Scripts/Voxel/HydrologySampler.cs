using UnityEngine;

namespace Voxel
{
    /// <summary>
    /// Детерминированная гидрология мира.
    /// Реки и мелкие водоёмы вычисляются только по seed + мировым координатам,
    /// поэтому не зависят от границ чанков.
    /// </summary>
    public static class HydrologySampler
    {
        private const float RiverScale = 0.0018f;
        private const float RiverWarpScale = 0.0045f;
        private const float PondScale = 0.012f;

        /// <summary>
        /// Возвращает силу реки от 0 до 1.
        /// Нулевая зона находится вокруг изолинии шума, поэтому реки
        /// получаются длинными и извилистыми, а не короткими пятнами.
        /// </summary>
        public static float GetRiverStrength(
            int seed,
            int x,
            int z,
            float continentalness)
        {
            // Река не появляется в океане и очень глубоких низинах.
            if (continentalness < 0.42f)
                return 0f;

            float warpX = Mathf.PerlinNoise(
                (x + GetSeedOffset(seed, 20)) * RiverWarpScale,
                (z + GetSeedOffset(seed, 21)) * RiverWarpScale);

            float warpZ = Mathf.PerlinNoise(
                (x + GetSeedOffset(seed, 22)) * RiverWarpScale,
                (z + GetSeedOffset(seed, 23)) * RiverWarpScale);

            float warpedX = x + (warpX - 0.5f) * 180f;
            float warpedZ = z + (warpZ - 0.5f) * 180f;

            float riverNoise = Mathf.PerlinNoise(
                (warpedX + GetSeedOffset(seed, 24)) * RiverScale,
                (warpedZ + GetSeedOffset(seed, 25)) * RiverScale);

            // Чем ближе noise к 0.5, тем ближе к центру реки.
            float distance = Mathf.Abs(riverNoise - 0.5f) * 2f;

            // Оставляем узкие полосы и мягко затухаем к берегам.
            float strength = 1f - Mathf.SmoothStep(
                0.035f,
                0.095f,
                distance);

            if (strength <= 0f)
                return 0f;

            // Не превращаем весь мир в сеть рек.
            float basinNoise = Mathf.PerlinNoise(
                (x + GetSeedOffset(seed, 26)) * 0.00075f,
                (z + GetSeedOffset(seed, 27)) * 0.00075f);

            float basinFactor = Mathf.SmoothStep(
                0.36f,
                0.58f,
                basinNoise);

            return strength * basinFactor;
        }

        /// <summary>
        /// Сила мелкого водоёма. Сам шум задаёт форму пятна,
        /// а проверка локальной низины выполняется WorldGenerator.
        /// </summary>
        public static float GetPondStrength(int seed, int x, int z)
        {
            float noise = Mathf.PerlinNoise(
                (x + GetSeedOffset(seed, 30)) * PondScale,
                (z + GetSeedOffset(seed, 31)) * PondScale);

            return Mathf.SmoothStep(
                0.72f,
                0.88f,
                noise);
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
