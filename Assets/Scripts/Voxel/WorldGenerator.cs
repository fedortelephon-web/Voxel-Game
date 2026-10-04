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

        [Tooltip("Уровень воды в океанах.")]
        [SerializeField] private int oceanWaterLevel = 50;

        [Tooltip("Уровень небольших водоёмов в болотах.")]
        [SerializeField] private int swampWaterLevel = 53;

        [Tooltip("Размер крупных форм рельефа. Меньше = крупнее формы.")]
        [SerializeField, Min(0.0001f)] private float terrainScale = 0.008f;

        [Tooltip("Размер мелких деталей рельефа.")]
        [SerializeField, Min(0.0001f)] private float detailScale = 0.035f;

        [Tooltip("Размер горных массивов. Меньше = крупнее горы.")]
        [SerializeField, Min(0.0001f)] private float mountainScale = 0.0008f;

        [Tooltip("Максимальное отклонение высоты при определении биома.")]
        [SerializeField, Min(0f)] private float biomeHeightVariation = 20f;

        [Tooltip("Размер областей отклонения границ биомов. Меньше = крупнее области.")]
        [SerializeField, Min(0.0001f)] private float biomeHeightVariationScale = 0.0024f;

        [Header("Climate Noise")]
        [Tooltip("Размер температурных регионов. Меньше = крупнее регионы.")]
        [SerializeField, Min(0.0001f)] private float temperatureScale = 0.003f;

        [Tooltip("Размер влажностных регионов. Меньше = крупнее регионы.")]
        [SerializeField, Min(0.0001f)] private float humidityScale = 0.0024f;

        [Tooltip("Размер материков и крупных географических регионов. Меньше = крупнее регионы.")]
        [SerializeField, Min(0.0001f)] private float continentalnessScale = 0.0012f;

        [Tooltip("Размер областей рельефа. Меньше = крупнее горные/ровные регионы.")]
        [SerializeField, Min(0.0001f)] private float erosionScale = 0.0024f;

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

                    int biomeHeight = GetBiomeHeightForBiome(
                        worldX,
                        worldZ,
                        height);

                    // Вода определяется физической высотой рельефа,
                    // поэтому суша никогда не может получить Ocean из-за
                    // случайного смещения границы биома.
                    BiomeDefinition biome;

                    if (height < oceanWaterLevel)
                    {
                        biome = GetBiome(BiomeType.Ocean);
                    }
                    else
                    {
                        biomeHeight = Mathf.Max(
                            biomeHeight,
                            oceanWaterLevel);

                        biome = BiomeResolver.Resolve(
                            climate,
                            biomeHeight);
                    }

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

                    // Океаны — полноценные водоёмы. В болотах вода
                    // появляется только в самых низких участках.
                    int waterLevel = -1;

                    if (height < oceanWaterLevel)
                    {
                        waterLevel = oceanWaterLevel;
                    }
                    else if (biome.Type == BiomeType.Swamp &&
                             height < swampWaterLevel)
                    {
                        waterLevel = swampWaterLevel;
                    }

                    if (waterLevel >= 0)
                    {
                        for (int y = height + 1;
                             y <= waterLevel && y < ChunkData.SizeY;
                             y++)
                        {
                            chunk.SetBlock(
                                x,
                                y,
                                z,
                                BlockType.Water);
                        }
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
            {
                PlantTrees(
                    chunk,
                    heights,
                    biomes,
                    chunkX,
                    chunkZ);

                PlaceMountainRocks(
                    chunk,
                    heights,
                    biomes,
                    chunkX,
                    chunkZ);
            }

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

                    int biomeHeight = GetBiomeHeightForBiome(
                        sampleX,
                        sampleZ,
                        height);

                    BiomeDefinition biome;

                    if (height < oceanWaterLevel)
                    {
                        biome = GetBiome(BiomeType.Ocean);
                    }
                    else
                    {
                        biomeHeight = Mathf.Max(
                            biomeHeight,
                            oceanWaterLevel);

                        biome = BiomeResolver.Resolve(
                            climate,
                            biomeHeight);
                    }

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
        /// Даёт биомной системе высоту с плавным детерминированным
        /// отклонением в пределах ±biomeHeightVariation.
        /// Это делает границы высотных биомов неровными и не привязанными
        /// к одной конкретной отметке рельефа.
        /// </summary>
        private int GetBiomeHeightForBiome(
            int x,
            int z,
            int terrainHeight)
        {
            float offsetX = GetSeedOffset(seed, 20);
            float offsetZ = GetSeedOffset(seed, 21);

            float variationNoise = Mathf.PerlinNoise(
                (x + offsetX) * biomeHeightVariationScale,
                (z + offsetZ) * biomeHeightVariationScale);

            float variation =
                (variationNoise * 2f - 1f) *
                biomeHeightVariation;

            return Mathf.RoundToInt(
                terrainHeight + variation);
        }

        private static BiomeDefinition GetBiome(BiomeType type)
        {
            foreach (BiomeDefinition biome in BiomeRegistry.All)
            {
                if (biome.Type == type)
                    return biome;
            }

            return BiomeRegistry.All[0];
        }

        /// <summary>
        /// Получить цвет растительности для мировой колонки в зависимости от биома.
        /// Как в Minecraft, трава и листья получают биомный оттенок при рендере.
        /// </summary>
        public Color GetVegetationTint(int worldX, int worldZ)
        {
            // Смешиваем цвета по ближайшим 5 блокам, чтобы граница биомов
            // не давала резкого цветового перехода.
            const int radius = 5;
            Color sum = Color.black;
            int count = 0;

            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                BiomeDefinition biome = GetBiomeAtWorldPosition(
                    worldX + dx,
                    worldZ + dz);

                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance > radius)
                    continue;

                float weight = 1f - distance / radius;
                sum += GetBaseVegetationTint(biome.Type) * weight;
                count += 1;
            }

            if (count == 0)
                return Color.white;

            // Нормализуем по весам повторно, чтобы оттенок не темнел
            // около границ мира.
            float weightSum = 0f;
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dz * dz <= radius * radius)
                    weightSum += 1f - Mathf.Sqrt(dx * dx + dz * dz) / radius;
            }

            return sum / Mathf.Max(0.001f, weightSum);
        }

        private static Color GetBaseVegetationTint(BiomeType type)
        {
            switch (type)
            {
                case BiomeType.Forest:
                    return new Color(0.78f, 1.00f, 0.72f);
                case BiomeType.Plains:
                    return new Color(1.00f, 1.00f, 1.00f);
                case BiomeType.Taiga:
                    return new Color(0.72f, 0.88f, 1.00f);
                case BiomeType.Desert:
                    return new Color(1.00f, 0.94f, 0.68f);
                case BiomeType.Swamp:
                    return new Color(0.68f, 0.92f, 0.58f);
                case BiomeType.Mountains:
                    return new Color(0.86f, 0.94f, 0.82f);
                default:
                    return Color.white;
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

            // Континентальность задаёт крупную высотную структуру:
            // океанические впадины -> низменности -> широкие равнины ->
            // возвышенности. Она не должна сама по себе создавать резкий склон.
            float landFactor = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    0.30f,
                    0.56f,
                    climate.Continentalness));

            float highlandFactor = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    0.62f,
                    0.82f,
                    climate.Continentalness));

            // Средняя часть суши имеет естественную «полку» около Y=64.
            // Это создаёт широкие равнины, но не запрещает промежуточные высоты.
            float plainsFactor = 1f - Mathf.Clamp01(
                Mathf.Abs(climate.Continentalness - 0.54f) / 0.18f);

            plainsFactor = Mathf.SmoothStep(
                0f,
                1f,
                plainsFactor);

            float oceanFloorHeight = Mathf.Max(
                8f,
                oceanHeight - 22f);

            float baseTerrain = Mathf.Lerp(
                oceanFloorHeight,
                plainsHeight,
                landFactor);

            baseTerrain = Mathf.Lerp(
                baseTerrain,
                Mathf.Min(plainsHeight + 20f, maxTerrainHeight - 20f),
                highlandFactor);

            // В центре материков рельеф намного спокойнее:
            // равнина занимает площадь, а не превращается в склон.
            float rollingAmplitude = Mathf.Lerp(
                18f,
                5f,
                plainsFactor);

            float detailAmplitude = Mathf.Lerp(
                8f,
                2f,
                plainsFactor);

            float rollingTerrain =
                (largeNoise - 0.5f) * rollingAmplitude +
                (detailNoise - 0.5f) * detailAmplitude;

            // Горы вырастают из обычного рельефа постепенно.
            // Нет отдельного «переключателя горного этажа».
            float erosionMountainFactor = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    0.72f,
                    0.20f,
                    climate.Erosion));

            float highlandMountainFactor = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    0.48f,
                    0.72f,
                    climate.Continentalness));

            float mountainPotential =
                erosionMountainFactor *
                highlandMountainFactor;

            // Ridged-подобная форма даёт хребты и седловины,
            // а не большой плоский «куб» на одной высоте.
            float ridge = 1f - Mathf.Abs(
                mountainNoise * 2f - 1f);

            ridge = Mathf.SmoothStep(
                0.18f,
                0.82f,
                ridge);

            float mountainElevation =
                mountainPotential *
                ridge *
                58f;

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
            // Проверяем также несколько колонок за пределами чанка.
            // Это нужно потому, что крона дерева может заходить в соседний чанк.
            const int FeatureRadius = 2;

            int minWorldX = chunkX * ChunkData.SizeX - FeatureRadius;
            int maxWorldX = chunkX * ChunkData.SizeX +
                            ChunkData.SizeX - 1 +
                            FeatureRadius;

            int minWorldZ = chunkZ * ChunkData.SizeZ - FeatureRadius;
            int maxWorldZ = chunkZ * ChunkData.SizeZ +
                            ChunkData.SizeZ - 1 +
                            FeatureRadius;

            for (int worldX = minWorldX;
                 worldX <= maxWorldX;
                 worldX++)
            {
                for (int worldZ = minWorldZ;
                     worldZ <= maxWorldZ;
                     worldZ++)
                {
                    int centerLocalX =
                        worldX - chunkX * ChunkData.SizeX;

                    int centerLocalZ =
                        worldZ - chunkZ * ChunkData.SizeZ;

                    BiomeDefinition biome;

                    if (centerLocalX >= 0 &&
                        centerLocalX < ChunkData.SizeX &&
                        centerLocalZ >= 0 &&
                        centerLocalZ < ChunkData.SizeZ)
                    {
                        biome = biomes[
                            centerLocalX,
                            centerLocalZ];
                    }
                    else
                    {
                        biome = GetBiomeAtWorldPosition(
                            worldX,
                            worldZ);
                    }

                    if (biome.TreeChance <= 0f)
                        continue;

                    int treeSeed = GetWorldColumnSeed(
                        seed,
                        worldX,
                        worldZ);

                    var rng = new System.Random(treeSeed);

                    float densityNoise = Mathf.PerlinNoise(
                        (worldX + GetSeedOffset(seed, 30)) * 0.035f,
                        (worldZ + GetSeedOffset(seed, 31)) * 0.035f);

                    float localChance = biome.TreeChance *
                        Mathf.Lerp(0.65f, 1.35f, densityNoise);

                    if (rng.NextDouble() > localChance)
                        continue;

                    int terrainHeight;

                    if (centerLocalX >= 0 &&
                        centerLocalX < ChunkData.SizeX &&
                        centerLocalZ >= 0 &&
                        centerLocalZ < ChunkData.SizeZ)
                    {
                        terrainHeight = heights[
                            centerLocalX,
                            centerLocalZ];
                    }
                    else
                    {
                        ClimatePoint climate = ClimateSampler.Sample(
                            seed,
                            worldX,
                            worldZ,
                            temperatureScale,
                            humidityScale,
                            continentalnessScale,
                            erosionScale);

                        terrainHeight = GetTerrainHeight(
                            worldX,
                            worldZ,
                            climate);
                    }

                    int localX =
                        worldX - chunkX * ChunkData.SizeX;

                    int localZ =
                        worldZ - chunkZ * ChunkData.SizeZ;

                    int top = terrainHeight + 1;

                    int trunk;
                    int canopyRadius;
                    int canopyBottom;

                    switch (biome.Type)
                    {
                        case BiomeType.Forest:
                            trunk = 5 + rng.Next(0, 3);
                            canopyRadius = 2;
                            canopyBottom = 2;
                            break;

                        case BiomeType.Taiga:
                            trunk = 7 + rng.Next(0, 3);
                            canopyRadius = 2;
                            canopyBottom = 3;
                            break;

                        case BiomeType.Swamp:
                            trunk = 4 + rng.Next(0, 2);
                            canopyRadius = 2;
                            canopyBottom = 2;
                            break;

                        case BiomeType.Plains:
                            // На равнинах деревья редкие, но сильно различаются
                            // по размеру и форме кроны.
                            if (rng.NextDouble() < 0.45f)
                            {
                                trunk = 3 + rng.Next(0, 2);
                                canopyRadius = 1;
                                canopyBottom = 2;
                            }
                            else if (rng.NextDouble() < 0.70f)
                            {
                                trunk = 5 + rng.Next(0, 3);
                                canopyRadius = 2;
                                canopyBottom = 2;
                            }
                            else
                            {
                                trunk = 7 + rng.Next(0, 3);
                                canopyRadius = 3;
                                canopyBottom = 3;
                            }
                            break;

                        default:
                            trunk = 4;
                            canopyRadius = 1;
                            canopyBottom = 2;
                            break;
                    }

                    if (top + trunk + 2 >= ChunkData.SizeY)
                        continue;

                    // Ствол принадлежит только чанку, в котором находится
                    // центр дерева. Это не создаёт дубликатов между чанками.
                    if (centerLocalX >= 0 &&
                        centerLocalX < ChunkData.SizeX &&
                        centerLocalZ >= 0 &&
                        centerLocalZ < ChunkData.SizeZ)
                    {
                        for (int i = 0; i < trunk; i++)
                        {
                            SetIfAir(
                                chunk,
                                localX,
                                top + i,
                                localZ,
                                BlockType.Wood);
                        }

                        // Верхний блок ствола скрываем листвой,
                        // чтобы ствол не торчал из верхушки кроны.
                        chunk.SetBlock(
                            localX,
                            top + trunk - 1,
                            localZ,
                            BlockType.Leaves);
                    }

                    // Листва генерируется всеми чанками, на которые она
                    // действительно попадает. Поэтому дерево на границе
                    // чанка больше не «разрезается».
                    for (int dy = canopyBottom;
                         dy <= trunk;
                         dy++)
                    {
                        float t = Mathf.InverseLerp(
                            canopyBottom,
                            trunk,
                            dy);

                        // Крона не должна заканчиваться плоским слоем.
                        // Радиус сначала увеличивается к середине кроны,
                        // а затем уменьшается к верхушке.
                        float crownShape = Mathf.Sin(t * Mathf.PI);
                        int radius = Mathf.RoundToInt(
                            canopyRadius * crownShape);

                        // Нижняя часть кроны не должна исчезать полностью.
                        if (dy < trunk && radius < 1)
                            radius = 1;

                        // Верхний слой — одна точка, поэтому дерево получает
                        // округлую/заострённую верхушку вместо плоской шапки.
                        if (dy == trunk)
                            radius = 0;

                        if (biome.Type == BiomeType.Plains)
                        {
                            // Разная форма кроны по высоте:
                            // маленькие деревья компактные, большие шире.
                            if (trunk >= 7 && dy <= canopyBottom + 1)
                                radius = Mathf.Min(radius, 2);

                            if (trunk <= 4 && dy < trunk)
                                radius = 1;
                        }

                        if (biome.Type == BiomeType.Taiga)
                        {
                            radius = Mathf.Max(
                                1,
                                Mathf.RoundToInt(
                                    Mathf.Lerp(
                                        2f,
                                        1f,
                                        t)));

                            if (dy == trunk)
                                radius = 0;
                        }
                        else if (biome.Type == BiomeType.Swamp)
                        {
                            radius = dy <= canopyBottom + 1
                                ? 2
                                : 1;

                            if (dy == trunk)
                                radius = 0;
                        }

                        for (int dx = -radius;
                             dx <= radius;
                             dx++)
                        {
                            for (int dz = -radius;
                                 dz <= radius;
                                 dz++)
                            {
                                if (Mathf.Abs(dx) == radius &&
                                    Mathf.Abs(dz) == radius)
                                    continue;

                                SetIfAir(
                                    chunk,
                                    localX + dx,
                                    top + dy,
                                    localZ + dz,
                                    BlockType.Leaves);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Получить биом в произвольной мировой колонке.
        /// Используется для генерации особенностей, которые пересекают границы чанков.
        /// </summary>
        private BiomeDefinition GetBiomeAtWorldPosition(
            int worldX,
            int worldZ)
        {
            ClimatePoint climate = ClimateSampler.Sample(
                seed,
                worldX,
                worldZ,
                temperatureScale,
                humidityScale,
                continentalnessScale,
                erosionScale);

            int height = GetTerrainHeight(
                worldX,
                worldZ,
                climate);

            int biomeHeight = GetBiomeHeightForBiome(
                worldX,
                worldZ,
                height);

            if (height < oceanWaterLevel)
                return GetBiome(BiomeType.Ocean);

            biomeHeight = Mathf.Max(
                biomeHeight,
                oceanWaterLevel);

            return BiomeResolver.Resolve(
                climate,
                biomeHeight);
        }

        /// <summary>
        /// Добавляет редкие каменные валуны в горных биомах.
        /// </summary>
        private void PlaceMountainRocks(
            ChunkData chunk,
            int[,] heights,
            BiomeDefinition[,] biomes,
            int chunkX,
            int chunkZ)
        {
            int seedValue = GetChunkSeed(seed, chunkX, chunkZ) ^ 0x45D9F3B;

            var rng = new System.Random(seedValue);

            for (int x = 2; x < ChunkData.SizeX - 2; x++)
            {
                for (int z = 2; z < ChunkData.SizeZ - 2; z++)
                {
                    if (biomes[x, z].Type != BiomeType.Mountains)
                        continue;

                    if (rng.NextDouble() > 0.025f)
                        continue;

                    int y = heights[x, z] + 1;

                    PlaceRockColumn(
                        chunk,
                        x,
                        y,
                        z,
                        rng.Next(1, 3));
                }
            }
        }

        /// <summary>Поставить маленький неровный валун из булыжника.</summary>
        private static void PlaceRockColumn(
            ChunkData chunk,
            int x,
            int y,
            int z,
            int height)
        {
            for (int i = 0; i < height; i++)
            {
                SetIfAir(
                    chunk,
                    x,
                    y + i,
                    z,
                    BlockType.Cobblestone);

                if (i == 0)
                {
                    SetIfAir(
                        chunk,
                        x + 1,
                        y,
                        z,
                        BlockType.Cobblestone);

                    SetIfAir(
                        chunk,
                        x - 1,
                        y,
                        z,
                        BlockType.Cobblestone);
                }
            }
        }

        /// <summary>
        /// Получить детерминированный seed одной мировой колонки X/Z.
        /// Используется для генерации деревьев без привязки к границам чанков.
        /// </summary>
        private static int GetWorldColumnSeed(
            int worldSeed,
            int worldX,
            int worldZ)
        {
            unchecked
            {
                int hash = worldSeed;

                hash ^= worldX * 374761393;
                hash = hash * 668265263;

                hash ^= worldZ * 1274126177;
                hash = hash * unchecked((int)2246822519);

                hash ^= hash >> 13;
                hash *= unchecked((int)3266489917);
                hash ^= hash >> 16;

                return hash;
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