using UnityEngine;

namespace Voxel
{
    /// <summary>
    /// Детерминированная гидрология мира.
    ///
    /// Реки основаны на принципе vanilla Minecraft 1.16 river_noise:
    /// крупные случайные области, их границы и последующее сглаживание.
    /// Наш мир не использует исходный Layer/Zoom pipeline Minecraft целиком,
    /// поэтому здесь перенесён именно принцип построения river noise,
    /// адаптированный к мировым X/Z координатам.
    ///
    /// Мелкие водоёмы используют ту же идею feature-генерации, что и
    /// vanilla LakeFeature: отдельный детерминированный feature-центр
    /// и несколько пересекающихся эллиптических «лепестков».
    /// </summary>
    public static class HydrologySampler
    {
        private const float RiverRegionSize = 192f;
        private const float RiverMinimumWidth = 3.5f;
        private const float RiverMaximumWidth = 8.0f;

        private const float PondCellSize = 48f;
        private const float PondFeatureChance = 0.10f;

        /// <summary>
        /// Возвращает силу реки от 0 до 1.
        /// Русло проходит по границам крупных детерминированных регионов,
        /// поэтому реки длинные, извилистые и продолжаются между чанками.
        /// </summary>
        public static float GetRiverStrength(
            int seed,
            int x,
            int z,
            float continentalness)
        {
            // Не создаём внутренние реки в океанических регионах.
            if (continentalness < 0.42f)
                return 0f;

            float cellSize = RiverRegionSize;
            int cellX = Mathf.FloorToInt(x / cellSize);
            int cellZ = Mathf.FloorToInt(z / cellSize);

            Vector2 position = new Vector2(x, z);

            float nearestDistance = float.MaxValue;
            float secondDistance = float.MaxValue;

            Vector2 nearestPoint = Vector2.zero;
            Vector2 secondPoint = Vector2.zero;

            int nearestRegion = -1;
            int secondRegion = -1;

            // Аналогично zoomed river layer из vanilla Minecraft:
            // ищем соседние крупные области, между которыми может проходить русло.
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = cellX + dx;
                    int cz = cellZ + dz;

                    float px = (cx + Hash01(seed, cx, cz, 10)) * cellSize;
                    float pz = (cz + Hash01(seed, cx, cz, 11)) * cellSize;

                    Vector2 point = new Vector2(px, pz);
                    float distance = (position - point).sqrMagnitude;

                    int region = (int)(Hash(seed, cx, cz, 12) & 3u);

                    if (distance < nearestDistance)
                    {
                        secondDistance = nearestDistance;
                        secondPoint = nearestPoint;
                        secondRegion = nearestRegion;

                        nearestDistance = distance;
                        nearestPoint = point;
                        nearestRegion = region;
                    }
                    else if (distance < secondDistance)
                    {
                        secondDistance = distance;
                        secondPoint = point;
                        secondRegion = region;
                    }
                }
            }

            // В vanilla river_noise река возникает на границе разных
            // нормализованных классов регионов.
            if (nearestRegion == secondRegion ||
                nearestRegion < 0 ||
                secondRegion < 0)
            {
                return 0f;
            }

            Vector2 edge = secondPoint - nearestPoint;
            float edgeLength = edge.magnitude;

            if (edgeLength < 0.001f)
                return 0f;

            Vector2 fromNearest = position - nearestPoint;

            // Расстояние до биссектрисы двух ближайших областей.
            float boundaryDistance = Mathf.Abs(
                edge.x * fromNearest.y -
                edge.y * fromNearest.x) / edgeLength;

            // Небольшое детерминированное разнообразие ширины.
            float widthNoise = Mathf.PerlinNoise(
                (x + GetSeedOffset(seed, 20)) * 0.0035f,
                (z + GetSeedOffset(seed, 21)) * 0.0035f);

            float halfWidth = Mathf.Lerp(
                RiverMinimumWidth,
                RiverMaximumWidth,
                widthNoise);

            float strength = 1f - Mathf.SmoothStep(
                0f,
                halfWidth,
                boundaryDistance);

            // Очень редкая часть речной сети должна полностью исчезать,
            // чтобы между крупными реками оставались большие сухие области.
            float basinNoise = Mathf.PerlinNoise(
                (x + GetSeedOffset(seed, 22)) * 0.0008f,
                (z + GetSeedOffset(seed, 23)) * 0.0008f);

            float basinMask = Mathf.SmoothStep(
                0.28f,
                0.58f,
                basinNoise);

            return strength * basinMask;
        }

        /// <summary>
        /// Сила небольшого водоёма от 0 до 1.
        /// Feature-центр генерирует несколько пересекающихся эллипсов,
        /// как старый ванильный LakeFeature. Это даёт неправильную форму
        /// вместо круглых пятен.
        /// </summary>
        public static float GetPondStrength(
            int seed,
            int x,
            int z)
        {
            int cellX = Mathf.FloorToInt(x / PondCellSize);
            int cellZ = Mathf.FloorToInt(z / PondCellSize);

            float best = 0f;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = cellX + dx;
                    int cz = cellZ + dz;

                    float chance = Hash01(
                        seed,
                        cx,
                        cz,
                        100);

                    if (chance > PondFeatureChance)
                        continue;

                    float centerX =
                        (cx + Hash01(seed, cx, cz, 101)) *
                        PondCellSize;

                    float centerZ =
                        (cz + Hash01(seed, cx, cz, 102)) *
                        PondCellSize;

                    int lobeCount =
                        4 + (int)(Hash(seed, cx, cz, 103) % 4u);

                    for (int lobe = 0;
                         lobe < lobeCount;
                         lobe++)
                    {
                        float angle =
                            Hash01(seed, cx, cz, 110 + lobe * 4) *
                            Mathf.PI * 2f;

                        float offset =
                            1.5f +
                            Hash01(seed, cx, cz, 111 + lobe * 4) * 5f;

                        float radiusX =
                            3f +
                            Hash01(seed, cx, cz, 112 + lobe * 4) * 4f;

                        float radiusZ =
                            3f +
                            Hash01(seed, cx, cz, 113 + lobe * 4) * 4f;

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
                            Mathf.Sqrt(nx * nx + nz * nz);

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
            return (Hash(seed, x, z, channel) & 0x00ffffffu) /
                   16777215f;
        }

        private static float GetSeedOffset(
            int value,
            int channel)
        {
            unchecked
            {
                uint hash = (uint)value;
                hash ^= (uint)channel * 374761393u;
                hash = hash * 668265263u;
                hash ^= hash >> 13;
                hash = hash * 1274126177u;
                hash ^= hash >> 16;

                return (hash & 0x0000ffffu) * 1f;
            }
        }
    }
}
