using System;
using UnityEngine;

namespace Voxel
{
    /// <summary>
    /// Генерирует содержимое чанков на основе seed и многослойного шума.
    /// Один и тот же seed и координаты чанка всегда создают одинаковый рельеф.
    /// </summary>
    public class WorldGenerator : MonoBehaviour
    {
        [Header("Seed")]
        [SerializeField] private int seed = 1;

        [Header("Terrain")]
        [Tooltip("Средний уровень равнин.")]
        [SerializeField] private int plainsHeight = 64;

        [Tooltip("Ниже этой высоты рельеф считается океанической впадиной.")]
        [SerializeField] private int oceanHeight = 50;

        [Tooltip("С этой высоты начинаются горные биомы.")]
        [SerializeField] private int mountainHeight = 88;

        [Tooltip("Максимальная высота поверхности мира.")]
        [SerializeField] private int maxTerrainHeight = 128;

        [Tooltip("Размер крупных форм рельефа. Меньше = крупнее формы.")]
        [SerializeField, Min(0.0001f)] private float terrainScale = 0.008f;

        [Tooltip("Размер мелких деталей рельефа.")]
        [SerializeField, Min(0.0001f)] private float detailScale = 0.035f;

        [Tooltip("Размер горных массивов. Меньше = крупнее горы.")]
        [SerializeField, Min(0.0001f)] private float mountainScale = 0.004f;

        [Header("Climate Noise")]
        [Tooltip("Размер температурных регионов. Меньше = крупнее регионы.")]
        [SerializeField, Min(0.0001f)] private float temperatureScale = 0.015f;

        [Tooltip("Размер влажностных регионов. Меньше = крупнее регионы.")]
        [SerializeField, Min(0.0001f)] private float humidityScale = 0.012f;

        [Tooltip("Размер материков и крупных географических регионов. Меньше = крупнее регионы.")]
        [SerializeField, Min(0.0001f)] private float continentalnessScale = 0.006f;

        [Tooltip("Размер областей рельефа. Меньше = крупнее горные/ровные регионы.")]
        [SerializeField, Min(0.0001f)] private float erosionScale = 0.012f;

        [Header("Layers")]
        [SerializeField] private int dirtDepth = 3;

        [Header("Trees")]
        [SerializeField] private bool generateTrees = false;


        /// <summary>Текущий seed генератора.</summary>
        public int Seed => seed;

        /// <summary>Изменить seed для следующей генерации мира.</summary>
        public void SetSeed(int newSeed)
        {
            seed = newSeed;
            Debug.Log($"WorldGenerator: установлен новый seed = {seed}");
        }

        /// <summary>
        /// Сгенерировать чанк по его координатам в мире.
        /// </summary>
        public ChunkData GenerateChunk(int chunkX, int chunkZ)
        {
            Debug.Log(
                $"WorldGenerator: генерация чанка ({chunkX}, {chunkZ}). " +
                $"Seed={seed}, " +
                $"TemperatureScale={temperatureScale}, " +
                $"HumidityScale={humidityScale}, " +
                $"ContinentalnessScale={continentalnessScale}, " +
                $"ErosionScale={erosionScale}");

            var chunk = new ChunkData();
            var heights = new int[ChunkData.SizeX, ChunkData.SizeZ];
            var biomes = new BiomeDefinition[
                ChunkData.SizeX,
                ChunkData.SizeZ];

            int[] biomeCounts =
                new int[Enum.GetValues(typeof(BiomeType)).Length];

            float minTemperature = 1f;
            float maxTemperature = 0f;
            float minHumidity = 1f;
            float maxHumidity = 0f;
            float minContinentalness = 1f;
            float maxContinentalness = 0f;
            float minErosion = 1f;
            float maxErosion = 0f;

            for (int x = 0; x < ChunkData.SizeX; x++)
            {
                for (int z = 0; z < ChunkData.SizeZ; z++)
                {
                    int worldX = chunkX * ChunkData.SizeX + x;
                    int worldZ = chunkZ * ChunkData.SizeZ + z;

                    ClimatePoint climate = ClimateSampler.Sample(
                        seed,
                        worldX,
                        worldZ,
                        temperatureScale,
                        humidityScale,
                        continentalnessScale,
                        erosionScale);

                    minTemperature = Mathf.Min(
                        minTemperature,
                        climate.Temperature);

                    maxTemperature = Mathf.Max(
                        maxTemperature,
                        climate.Temperature);

                    minHumidity = Mathf.Min(
                        minHumidity,
                        climate.Humidity);

                    maxHumidity = Mathf.Max(
                        maxHumidity,
                        climate.Humidity);

                    minContinentalness = Mathf.Min(
                        minContinentalness,
                        climate.Continentalness);

                    maxContinentalness = Mathf.Max(
                        maxContinentalness,
                        climate.Continentalness);

                    minErosion = Mathf.Min(
                        minErosion,
                        climate.Erosion);

                    maxErosion = Mathf.Max(
                        maxErosion,
                        climate.Erosion);

                    int height = GetTerrainHeight(
                        worldX,
                        worldZ,
                        climate);

                    BiomeDefinition biome =
                        BiomeResolver.Resolve(
                            climate,
                            height);

                    biomeCounts[(int)biome.Type]++;

                    biomes[x, z] = biome;

                    height = Mathf.Clamp(
                        height,
                        1,
                        maxTerrainHeight);

                    heights[x, z] = height;

                    for (int y = 0; y <= height; y++)
                    {
                        BlockType type;

                        if (y == height)
                        {
                            type = biome.SurfaceBlock;
                        }
                        else if (y >= height - biome.DirtDepth)
                        {
                            type = biome.FillerBlock;
                        }
                        else
                        {
                            type = BlockType.Stone;
                        }

                        chunk.SetBlock(x, y, z, type);
                    }
                }
            }

            Debug.Log(
                $"WorldGenerator: чанк ({chunkX}, {chunkZ}) — " +
                $"Plains={biomeCounts[(int)BiomeType.Plains]}, " +
                $"Forest={biomeCounts[(int)BiomeType.Forest]}, " +
                $"Desert={biomeCounts[(int)BiomeType.Desert]}, " +
                $"Taiga={biomeCounts[(int)BiomeType.Taiga]}, " +
                $"Mountains={biomeCounts[(int)BiomeType.Mountains]}, " +
                $"Swamp={biomeCounts[(int)BiomeType.Swamp]}, " +
                $"Ocean={biomeCounts[(int)BiomeType.Ocean]}. " +
                $"Climate: " +
                $"T={minTemperature:F2}-{maxTemperature:F2}, " +
                $"H={minHumidity:F2}-{maxHumidity:F2}, " +
                $"C={minContinentalness:F2}-{maxContinentalness:F2}, " +
                $"E={minErosion:F2}-{maxErosion:F2}");

            if (generateTrees)
                PlantTrees(
                    chunk,
                    heights,
                    biomes,
                    chunkX,
                    chunkZ);

            return chunk;
        }

        /// <summary>
        /// Вывести в Console карту биомов по мировым координатам.
        /// Один символ показывает участок мира размером step × step блоков.
        /// </summary>
        public void LogBiomeMap(
            int minX,
            int maxX,
            int minZ,
            int maxZ,
            int step)
        {
            step = Mathf.Max(step, 1);

            var map = new System.Text.StringBuilder();

            map.AppendLine(
                "========== BIOME MAP ==========");

            map.AppendLine(
                "P=Plains F=Forest D=Desert T=Taiga M=Mountains S=Swamp O=Ocean");

            for (int z = maxZ - step; z >= minZ; z -= step)
            {
                for (int x = minX; x < maxX; x += step)
                {
                    int sampleX = x + step / 2;
                    int sampleZ = z + step / 2;

                    ClimatePoint climate = ClimateSampler.Sample(
                        seed,
                        sampleX,
                        sampleZ,
                        temperatureScale,
                        humidityScale,
                        continentalnessScale,
                        erosionScale);

                    int height = GetTerrainHeight(
                        sampleX,
                        sampleZ,
                        climate);

                    BiomeDefinition biome =
                        BiomeResolver.Resolve(
                            climate,
                            height);

                    map.Append(GetBiomeSymbol(biome.Type));
                }

                map.AppendLine();
            }

            map.AppendLine(
                "================================");

            Debug.Log(map.ToString());
        }

        /// <summary>
        /// Символ для карты биомов.
        /// </summary>
        private static char GetBiomeSymbol(BiomeType type)
        {
            switch (type)
            {
                case BiomeType.Plains:
                    return 'P';

                case BiomeType.Forest:
                    return 'F';

                case BiomeType.Desert:
                    return 'D';

                case BiomeType.Taiga:
                    return 'T';

                case BiomeType.Mountains:
                    return 'M';

                case BiomeType.Swamp:
                    return 'S';

                case BiomeType.Ocean:
                    return 'O';

                default:
                    return '?';
            }
        }

        /// <summary>
        /// Рассчитать высоту поверхности по мировым координатам.
        /// </summary>
        private int GetTerrainHeight(
            int x,
            int z,
            ClimatePoint climate)
        {
            float terrainOffsetX = GetSeedOffset(seed, 10);
            float terrainOffsetZ = GetSeedOffset(seed, 11);

            float largeNoise = Mathf.PerlinNoise(
                (x + terrainOffsetX) * terrainScale,
                (z + terrainOffsetZ) * terrainScale);

            float detailNoise = Mathf.PerlinNoise(
                (x + terrainOffsetX) * detailScale + 1000f,
                (z + terrainOffsetZ) * detailScale + 1000f);

            float mountainNoise = Mathf.PerlinNoise(
                (x + terrainOffsetX) * mountainScale + 2000f,
                (z + terrainOffsetZ) * mountainScale + 2000f);

            // Continentalness задаёт общий уровень местности,
            // но не фиксирует мир на нескольких высотах.
            // Низкие значения дают океанические впадины,
            // средние — низменности и равнины,
            // высокие — возвышенности.
            float continentalElevation = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    0.20f,
                    0.70f,
                    climate.Continentalness));

            float baseTerrain = Mathf.Lerp(
                6f,
                96f,
                continentalElevation);

            // Непрерывные волны рельефа.
            // Они работают везде: и на дне океана, и на равнинах, и в горах.
            float rollingTerrain =
                (largeNoise - 0.5f) * 24f +
                (detailNoise - 0.5f) * 10f;

            // Низкая эрозия повышает вероятность крупного рельефа,
            // но горы не включаются одним резким порогом.
            float mountainPotential = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    0.75f,
                    0.25f,
                    climate.Erosion));

            // Ridged-подобная форма даёт хребты вместо плоских плато.
            float ridge = 1f - Mathf.Abs(
                mountainNoise * 2f - 1f);

            ridge = Mathf.SmoothStep(
                0.20f,
                0.82f,
                ridge);

            // Чем сильнее горный потенциал и чем выраженнее хребет,
            // тем выше поверхность. При этом вклад остаётся плавным.
            float mountainElevation =
                mountainPotential *
                ridge *
                42f;

            int height = Mathf.RoundToInt(
                baseTerrain +
                rollingTerrain +
                mountainElevation);

            return Mathf.Clamp(
                height,
                1,
                maxTerrainHeight);
        }
        /// <summary>
        /// Получить детерминированное смещение из seed.
        /// </summary>
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

        /// <summary>
        /// Создать деревья детерминированно из seed и координат чанка.
        /// </summary>
        private void PlantTrees(
            ChunkData chunk,
            int[,] heights,
            BiomeDefinition[,] biomes,
            int chunkX,
            int chunkZ)
        {
            int chunkSeed = GetChunkSeed(seed, chunkX, chunkZ);
            var rng = new System.Random(chunkSeed);

            for (int x = 2; x < ChunkData.SizeX - 2; x++)
            {
                for (int z = 2; z < ChunkData.SizeZ - 2; z++)
                {
                    BiomeDefinition biome = biomes[x, z];

                    if (rng.NextDouble() > biome.TreeChance)
                        continue;

                    int top = heights[x, z] + 1;
                    const int trunk = 4;

                    if (top + trunk + 1 >= ChunkData.SizeY)
                        continue;

                    for (int i = 0; i < trunk; i++)
                        chunk.SetBlock(
                            x,
                            top + i,
                            z,
                            BlockType.Wood
                        );

                    for (int dy = trunk - 2; dy <= trunk - 1; dy++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            for (int dz = -2; dz <= 2; dz++)
                            {
                                if (Mathf.Abs(dx) == 2 &&
                                    Mathf.Abs(dz) == 2)
                                    continue;

                                SetIfAir(
                                    chunk,
                                    x + dx,
                                    top + dy,
                                    z + dz,
                                    BlockType.Leaves
                                );
                            }
                        }
                    }

                    SetIfAir(
                        chunk,
                        x,
                        top + trunk,
                        z,
                        BlockType.Leaves
                    );

                    SetIfAir(
                        chunk,
                        x + 1,
                        top + trunk,
                        z,
                        BlockType.Leaves
                    );

                    SetIfAir(
                        chunk,
                        x - 1,
                        top + trunk,
                        z,
                        BlockType.Leaves
                    );

                    SetIfAir(
                        chunk,
                        x,
                        top + trunk,
                        z + 1,
                        BlockType.Leaves
                    );

                    SetIfAir(
                        chunk,
                        x,
                        top + trunk,
                        z - 1,
                        BlockType.Leaves
                    );
                }
            }
        }

        /// <summary>
        /// Получить уникальный детерминированный seed для чанка.
        /// </summary>
        private static int GetChunkSeed(int worldSeed, int chunkX, int chunkZ)
        {
            unchecked
            {
                int hash = worldSeed;

                hash ^= chunkX * 374761393;
                hash = hash * 668265263;

                hash ^= chunkZ * 1274126177;
                hash = hash * unchecked((int)2246822519);

                hash ^= hash >> 13;
                hash *= unchecked((int)3266489917);
                hash ^= hash >> 16;

                return hash;
            }
        }

        /// <summary>
        /// Поставить блок только в пустую клетку внутри чанка.
        /// </summary>
        private static void SetIfAir(
            ChunkData chunk,
            int x,
            int y,
            int z,
            BlockType type)
        {
            if (chunk.GetBlock(x, y, z) == BlockType.Air)
                chunk.SetBlock(x, y, z, type);
        }
    }
}