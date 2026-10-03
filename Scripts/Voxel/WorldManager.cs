using System;
using UnityEngine;

namespace Voxel
{
    /// <summary>
    /// Управляет одним чанком: хранение данных, пересборка меша при изменениях.
    /// Позже заменим на систему чанков.
    /// </summary>
    public class WorldManager : MonoBehaviour
    {
        [SerializeField] private Material blockMaterial;

        /// <summary>Срабатывает после любого изменения блока — для системы сохранения.</summary>
        public event Action WorldChanged;

        private ChunkData _chunk;
        private MeshFilter _meshFilter;

        /// <summary>Awake выполняется раньше Start других скриптов: мир готов до загрузки сейва.</summary>
        private void Awake()
        {
            _meshFilter = gameObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.material = blockMaterial;
            renderer.material.mainTexture = VoxelTextures.Atlas;
            _chunk = GenerateTerrain();
            RebuildMesh();
        }

        /// <summary>Получить блок по мировым координатам.</summary>
        public BlockType GetBlock(Vector3Int worldPos)
        {
            return _chunk.GetBlock(worldPos.x, worldPos.y, worldPos.z);
        }
        /// <summary>В пределах ли мира координата (пока мир — один чанк).</summary>
        public bool InBounds(Vector3Int worldPos)
        {
            return worldPos.x >= 0 && worldPos.y >= 0 && worldPos.z >= 0
                && worldPos.x < ChunkData.SizeX
                && worldPos.y < ChunkData.SizeY
                && worldPos.z < ChunkData.SizeZ;
        }
        /// <summary>Установить блок, пересобрать меш, сообщить о изменении.</summary>
        public void SetBlock(Vector3Int worldPos, BlockType type)
        {
            _chunk.SetBlock(worldPos.x, worldPos.y, worldPos.z, type);
            RebuildMesh();
            WorldChanged?.Invoke();
        }

        /// <summary>Пересоздать мир с нуля (новая игра).</summary>
        public void Regenerate()
        {
            _chunk = GenerateTerrain();
            RebuildMesh();
            WorldChanged?.Invoke();
        }

        /// <summary>Байты чанка для сохранения.</summary>
        public byte[] GetBlocksBytes()
        {
            return _chunk.ToBytes();
        }

        /// <summary>Загрузить байты чанка и пересобрать меш.</summary>
        public void SetBlocksBytes(byte[] data)
        {
            _chunk.FromBytes(data);
            RebuildMesh();
        }

        /// <summary>Перестроить меш из текущих данных чанка.</summary>
        private void RebuildMesh()
        {
            Mesh mesh = ChunkMesher.BuildMesh(_chunk);
            _meshFilter.mesh = mesh;
        }

        /// <summary>Детерминированный рельеф: трава сверху, земля, камень.</summary>
        /// <summary>Детерминированный рельеф: холмы, трава, земля, камень, деревья.</summary>
        private static ChunkData GenerateTerrain()
        {
            var chunk = new ChunkData();
            var heights = new int[ChunkData.SizeX, ChunkData.SizeZ];

            for (int x = 0; x < ChunkData.SizeX; x++)
            for (int z = 0; z < ChunkData.SizeZ; z++)
            {
                int height = 6 + Mathf.RoundToInt(2f * Mathf.Sin(x * 0.35f) + 2f * Mathf.Cos(z * 0.3f));
                heights[x, z] = height;

                for (int y = 0; y <= height && y < ChunkData.SizeY; y++)
                {
                    BlockType type = y == height ? BlockType.Grass
                        : y >= height - 2 ? BlockType.Dirt
                        : BlockType.Stone;
                    chunk.SetBlock(x, y, z, type);
                }
            }

            PlantTrees(chunk, heights);
            return chunk;
        }

        /// <summary>Редкие деревья: ствол 4 блока и крона из листвы.</summary>
        /// <summary>Редкие деревья: ствол, широкая крона, узкая верхушка.</summary>
        private static void PlantTrees(ChunkData chunk, int[,] heights)
        {
            var rng = new System.Random(12345);

            for (int x = 2; x < ChunkData.SizeX - 2; x++)
            for (int z = 2; z < ChunkData.SizeZ - 2; z++)
            {
                if (rng.NextDouble() > 0.03)
                    continue;

                int top = heights[x, z] + 1;
                const int trunk = 4;

                for (int i = 0; i < trunk; i++)
                    chunk.SetBlock(x, top + i, z, BlockType.Wood);

                // Два широких яруса кроны со срезанными углами
                for (int dy = trunk - 2; dy <= trunk - 1; dy++)
                for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (Mathf.Abs(dx) == 2 && Mathf.Abs(dz) == 2)
                        continue;

                    SetIfAir(chunk, x + dx, top + dy, z + dz, BlockType.Leaves);
                }

                // Узкая верхушка крестом из пяти блоков
                SetIfAir(chunk, x, top + trunk, z, BlockType.Leaves);
                SetIfAir(chunk, x + 1, top + trunk, z, BlockType.Leaves);
                SetIfAir(chunk, x - 1, top + trunk, z, BlockType.Leaves);
                SetIfAir(chunk, x, top + trunk, z + 1, BlockType.Leaves);
                SetIfAir(chunk, x, top + trunk, z - 1, BlockType.Leaves);
            }
        }

        /// <summary>Ставит блок, только если клетка пустая и в пределах чанка.</summary>
        private static void SetIfAir(ChunkData chunk, int x, int y, int z, BlockType type)
        {
            if (y >= ChunkData.SizeY)
                return;

            if (chunk.GetBlock(x, y, z) == BlockType.Air)
                chunk.SetBlock(x, y, z, type);
        }
    }
}