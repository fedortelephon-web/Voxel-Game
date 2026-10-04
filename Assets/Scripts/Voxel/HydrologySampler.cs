using UnityEngine;

namespace Voxel
{
    /// <summary>
    /// Детерминированная гидрология мира.
    ///
    /// В vanilla Minecraft 1.16 отдельный NoiseToRiverLayer формирует
    /// непрерывную river-mask на этапе слоёв биомов. Здесь сохраняем этот
    /// принцип, но строим mask напрямую по мировым координатам.
    ///
    /// Для игры нам важна именно непрерывная геометрическая маска:
    /// одно русло должно проходить через соседние блоки, а не распадаться
    /// на отдельные точки.
    /// </summary>
    public static class HydrologySampler
    {
        // Большой масштаб даёт длинные реки.
        private const float RiverScale = 0.0025f;

        // Искажает координаты русла, чтобы линии не были идеально прямыми.
        private const float RiverWarpScale = 0.0055f;
        private const float RiverWarpStrength = 90f;

        // Максимальная ширина русла от центра до берега.
        private const float RiverMinHalfWidth = 4f;
        private const float RiverMaxHalfWidth = 9f;

        // Мелкие водоёмы.
        private const float PondCellSize = 48f;
        private const float PondFeatureChance = 0.10f;

        /// <summary>
        /// Непрерывная сила реки от 0 до 1.
        /// Русло получается как плавная изолиния низкочастотного шума.
        /// </summary>
        public static float GetRiverStrength(
            int seed,
            int x,
            int z,
            float continentalness)
        {
            if (continentalness < 0.42f)
                return 0f;

            float warpX = Mathf.PerlinNoise(
                (x + GetSeedOffset(seed, 20)) * RiverWarpScale,
                (z + GetSeedOffset(seed, 21)) * RiverWarpScale);

            float warpZ = Mathf.PerlinNoise(
                (x + GetSeedOffset(seed, 22)) * RiverWarpScale,
                (z + GetSeedOffset(seed, 23)) * RiverWarpScale);

            float sampleX =
                x + (warpX - 0.5f) * RiverWarpStrength;

            float sampleZ =
                z + (warpZ - 0.5f) * RiverWarpStrength;

            float riverNoise = Mathf.PerlinNoise(
                (sampleX + GetSeedOffset(seed, 24)) * RiverScale,
                (sampleZ + GetSeedOffset(seed, 25)) * RiverScale);

            // Изолиния около 0.5 превращается в длинную непрерывную реку.
            float centerDistance =
                Mathf.Abs(riverNoise - 0.5f) * 2f;

            float widthNoise = Mathf.PerlinNoise(
                (sampleX + GetSeedOffset(seed, 26)) * 0.004f,
                (sampleZ + GetSeedOffset(seed, 27)) * 0.004f);

            float halfWidth = Mathf.Lerp(
                RiverMinHalfWidth,
                RiverMaxHalfWidth,
                widthNoise);

            // Чем шире конкретная река, тем дальше от её центральной
            // изолинии начинается берег. Маска остаётся непрерывной.
            float normalizedWidth =
                Mathf.InverseLerp(
                    RiverMinHalfWidth,
                    RiverMaxHalfWidth,
                    halfWidth);

            float widthThreshold = Mathf.Lerp(
                0.065f,
                0.14f,
                normalizedWidth);

            float strength =
                1f - Mathf.SmoothStep(
                    0.015f,
                    widthThreshold,
                    centerDistance);

            // Не применяем дополнительную маску к готовому руслу:
            // она могла бы обнулить отдельные участки и визуально разорвать
            // реку на цепочку независимых фрагментов.

            return Mathf.Clamp01(strength);
        }

        /// <summary>
        /// Сила небольшого водоёма от 0 до 1.
        /// Форма собирается из нескольких пересекающихся эллипсов,
        /// поэтому получается неровный пруд, а не квадрат/круг.
        /// </summary>
        public static float GetPondStrength(
            int seed,
            int x,
            int z)
        {
            int cellX = Mathf.FloorToInt(
                x / PondCellSize);

            int cellZ = Mathf.FloorToInt(
                z / PondCellSize);

            float best = 0f;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = cellX + dx;
                    int cz = cellZ + dz;

                    if (Hash01(seed, cx, cz, 100) >
                        PondFeatureChance)
                    {
                        continue;
                    }

                    float centerX =
                        (cx + Hash01(seed, cx, cz, 101)) *
                        PondCellSize;

                    float centerZ =
                        (cz + Hash01(seed, cx, cz, 102)) *
                        PondCellSize;

                    int lobeCount =
                        4 + (int)(
                            Hash(
                                seed,
                                cx,
                                cz,
                                103) % 4u);

                    for (int lobe = 0;
                         lobe < lobeCount;
                         lobe++)
                    {
                        float angle =
                            Hash01(
                                seed,
                                cx,
                                cz,
                                110 + lobe * 4) *
                            Mathf.PI * 2f;

                        float offset =
                            1.5f +
                            Hash01(
                                seed,
                                cx,
                                cz,
                                111 + lobe * 4) * 5f;

                        float radiusX =
                            3f +
                            Hash01(
                                seed,
                                cx,
                                cz,
                                112 + lobe * 4) * 4f;

                        float radiusZ =
                            3f +
                            Hash01(
                                seed,
                                cx,
                                cz,
                                113 + lobe * 4) * 4f;

                        float lobeCenterX =
                            centerX +
                            Mathf.Cos(angle) * offset;

                        float lobeCenterZ =
                            centerZ +
                            Mathf.Sin(angle) * offset;

                        float nx =
                            (x - lobeCenterX) / radiusX;

                        float nz =
                            (z - lobeCenterZ) / radiusZ;

                        float distance =
                            Mathf.Sqrt(
                                nx * nx +
                                nz * nz);

                        float strength =
                            1f - Mathf.SmoothStep(
                                0.55f,
                                1f,
                                distance);

                        if (strength > best)
                            best = strength;
                    }
                }
            }

            return Mathf.Clamp01(best);
        }

        private static uint Hash(
            int seed,
            int x,
            int z,
            int channel)
        {
            unchecked
            {
                uint h = (uint)seed;

                h ^= (uint)x * 374761393u;
                h = h * 668265263u;

                h ^= (uint)z * 1274126177u;
                h = h * 2246822519u;

                h ^= (uint)channel * 3266489917u;

                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;

                return h;
            }
        }

        private static float Hash01(
            int seed,
            int x,
            int z,
            int channel)
        {
            return (Hash(
                seed,
                x,
                z,
                channel) & 0x00ffffffu) /
                16777215f;
        }

        private static float GetSeedOffset(
            int value,
            int channel)
        {
            unchecked
            {
                uint hash = (uint)value;

                hash ^= (uint)channel *
                        374761393u;

                hash = hash * 668265263u;
                hash ^= hash >> 13;
                hash = hash * 1274126177u;
                hash ^= hash >> 16;

                return (hash & 0x0000ffffu);
            }
        }
    }
}
