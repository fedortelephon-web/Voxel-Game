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
        [SerializeField] private int seed = 12345;

        [Header("Terrain")]
        [SerializeField] private int baseHeight = 9;
        [SerializeField] private int heightVariation = 9;
        [SerializeField] private float terrainScale = 0.025f;
        [SerializeField] private float detailScale = 0.08f;

        [Header("Climate")]
        [SerializeField] private float temperatureScale = 0.07f;
        [SerializeField] private float humidityScale = 0.065f;
        [SerializeField] private float continentalnessScale = 0.04f;
        [SerializeField] private float erosionScale = 0.06f;

        [Header("Layers")]
        [SerializeField] private int dirtDepth = 3;

        [Header("Trees")]
        [SerializeField] private bool generateTrees = true;


        /// <summary>Текущий seed генератора.</summary>
        public int Seed => seed;

        /// <summary>
        /// Сгенерировать чанк по его координатам в мире.
        /// </summary>
        public ChunkData GenerateChunk(int chunkX, int chunkZ)
        {
            var chunk = new ChunkData();
            var heights = new int[ChunkData.SizeX, ChunkData.SizeZ];
            var biomes = new BiomeDefinition[
                ChunkData.SizeX,
                ChunkData.SizeZ];

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

                    BiomeDefinition biome =
                        BiomeResolver.Resolve(climate);

                    biomes[x, z] = biome;

                    int height = GetTerrainHeight(
                        worldX,
                        worldZ,
                        climate);

                    height = Mathf.Clamp(
                        height,
                        1,
                        ChunkData.SizeY - 1);

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
        /// Рассчитать высоту поверхности по мировым координатам.
        /// </summary>
        private int GetTerrainHeight(
            int x,
            int z,
            ClimatePoint climate)
        {
            float offsetX = GetSeedOffset(seed, 0);
            float offsetZ = GetSeedOffset(seed, 1);

            float largeNoise = Mathf.PerlinNoise(
                (x + offsetX) * terrainScale,
                (z + offsetZ) * terrainScale
            );

            float mediumNoise = Mathf.PerlinNoise(
                (x + offsetX) * detailScale + 1000f,
                (z + offsetZ) * detailScale + 1000f
            );

            float fineNoise = Mathf.PerlinNoise(
                (x + offsetX) * detailScale * 2f + 2000f,
                (z + offsetZ) * detailScale * 2f + 2000f
            );

            float combinedNoise =
                largeNoise * 0.60f +
                mediumNoise * 0.25f +
                fineNoise * 0.15f;

            float erosionStrength = Mathf.Lerp(
                1.35f,
                0.45f,
                climate.Erosion);

            float continentalOffset =
                (climate.Continentalness - 0.5f) * 6f;

            return baseHeight + Mathf.RoundToInt(
                (combinedNoise - 0.5f) *
                heightVariation *
                erosionStrength
                + continentalOffset
            );
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