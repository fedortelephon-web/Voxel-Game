using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voxel
{
    /// <summary>
    /// Управляет несколькими чанками мира, их генерацией и пересборкой мешей.
    /// </summary>
    public class WorldManager : MonoBehaviour
    {
        [SerializeField] private Material blockMaterial;
        [SerializeField] private WorldGenerator worldGenerator;

        /// <summary>Срабатывает после любого изменения блока — для системы сохранения.</summary>
        public event Action WorldChanged;

        private readonly Dictionary<Vector2Int, ChunkData> _chunks =
            new Dictionary<Vector2Int, ChunkData>();

        private readonly Dictionary<Vector2Int, MeshFilter> _meshFilters =
            new Dictionary<Vector2Int, MeshFilter>();

        private readonly Dictionary<Vector2Int, Mesh> _chunkMeshes =
            new Dictionary<Vector2Int, Mesh>();

        private const int WorldRadius = 1;

        /// <summary>Awake выполняется раньше Start других скриптов: мир готов до загрузки сейва.</summary>
        private void Awake()
        {
            GenerateWorld();
        }

        /// <summary>Создать мир из чанков вокруг центрального чанка.</summary>
        private void GenerateWorld()
        {
            for (int chunkX = -WorldRadius; chunkX <= WorldRadius; chunkX++)
            {
                for (int chunkZ = -WorldRadius; chunkZ <= WorldRadius; chunkZ++)
                {
                    CreateChunk(chunkX, chunkZ);
                }
            }

            RebuildAllMeshes();
        }

        /// <summary>Создать один чанк и его объект в сцене.</summary>
        private void CreateChunk(int chunkX, int chunkZ)
        {
            var coord = new Vector2Int(chunkX, chunkZ);

            ChunkData chunk = worldGenerator.GenerateChunk(chunkX, chunkZ);
            _chunks[coord] = chunk;

            GameObject chunkObject = new GameObject($"Chunk_{chunkX}_{chunkZ}");
            chunkObject.transform.SetParent(transform);
            chunkObject.transform.localPosition = new Vector3(
                chunkX * ChunkData.SizeX,
                0f,
                chunkZ * ChunkData.SizeZ
            );

            MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = chunkObject.AddComponent<MeshRenderer>();

            renderer.material = blockMaterial;
            renderer.material.mainTexture = VoxelTextures.Atlas;

            _meshFilters[coord] = meshFilter;
        }

        /// <summary>Получить блок по мировым координатам.</summary>
        public BlockType GetBlock(Vector3Int worldPos)
        {
            if (worldPos.y < 0 || worldPos.y >= ChunkData.SizeY)
                return BlockType.Air;

            Vector2Int chunkCoord = WorldToChunkCoord(worldPos);
            Vector3Int localPos = WorldToLocal(worldPos);

            if (!_chunks.TryGetValue(chunkCoord, out ChunkData chunk))
                return BlockType.Air;

            return chunk.GetBlock(localPos.x, localPos.y, localPos.z);
        }

        /// <summary>В пределах ли мира координата.</summary>
        public bool InBounds(Vector3Int worldPos)
        {
            if (worldPos.y < 0 || worldPos.y >= ChunkData.SizeY)
                return false;

            Vector2Int chunkCoord = WorldToChunkCoord(worldPos);
            return _chunks.ContainsKey(chunkCoord);
        }

        /// <summary>Установить блок и пересобрать затронутые чанки.</summary>
        public void SetBlock(Vector3Int worldPos, BlockType type)
        {
            if (!InBounds(worldPos))
                return;

            Vector2Int chunkCoord = WorldToChunkCoord(worldPos);
            Vector3Int localPos = WorldToLocal(worldPos);

            ChunkData chunk = _chunks[chunkCoord];
            chunk.SetBlock(localPos.x, localPos.y, localPos.z, type);

            RebuildChunk(chunkCoord);

            if (localPos.x == 0)
                RebuildChunk(chunkCoord + Vector2Int.left);

            if (localPos.x == ChunkData.SizeX - 1)
                RebuildChunk(chunkCoord + Vector2Int.right);

            if (localPos.z == 0)
                RebuildChunk(chunkCoord + new Vector2Int(0, -1));

            if (localPos.z == ChunkData.SizeZ - 1)
                RebuildChunk(chunkCoord + new Vector2Int(0, 1));

            WorldChanged?.Invoke();
        }

        /// <summary>Пересоздать мир с нуля.</summary>
        public void Regenerate()
        {
            ClearWorld();

            GenerateWorld();
            WorldChanged?.Invoke();
        }

        /// <summary>Байты всех чанков для сохранения.</summary>
        public byte[] GetBlocksBytes()
        {
            int chunkCount = _chunks.Count;
            int chunkSize =
                ChunkData.SizeX *
                ChunkData.SizeY *
                ChunkData.SizeZ;

            var result = new byte[chunkCount * chunkSize];

            int offset = 0;

            for (int chunkX = -WorldRadius; chunkX <= WorldRadius; chunkX++)
            {
                for (int chunkZ = -WorldRadius; chunkZ <= WorldRadius; chunkZ++)
                {
                    Vector2Int coord = new Vector2Int(chunkX, chunkZ);

                    if (!_chunks.TryGetValue(coord, out ChunkData chunk))
                        continue;

                    byte[] data = chunk.ToBytes();
                    Buffer.BlockCopy(data, 0, result, offset, data.Length);
                    offset += data.Length;
                }
            }

            return result;
        }

        /// <summary>Загрузить байты всех чанков и пересобрать меши.</summary>
        public bool SetBlocksBytes(byte[] data)
{
    int chunkSize =
        ChunkData.SizeX *
        ChunkData.SizeY *
        ChunkData.SizeZ;

    int expectedSize = _chunks.Count * chunkSize;

    Debug.Log(
        $"WorldManager: загрузка мира. " +
        $"Получено байт: {data?.Length ?? -1}, " +
        $"ожидалось: {expectedSize}, " +
        $"чанков: {_chunks.Count}");

    if (data == null || data.Length != expectedSize)
        return false;

    int offset = 0;

    for (int chunkX = -WorldRadius; chunkX <= WorldRadius; chunkX++)
    {
        for (int chunkZ = -WorldRadius; chunkZ <= WorldRadius; chunkZ++)
        {
            Vector2Int coord = new Vector2Int(chunkX, chunkZ);

            if (!_chunks.TryGetValue(coord, out ChunkData chunk))
                return false;

            byte[] chunkData = new byte[chunkSize];
            Buffer.BlockCopy(data, offset, chunkData, 0, chunkSize);

            if (!chunk.FromBytes(chunkData))
                return false;

            int nonAir = 0;

            for (int y = 0; y < ChunkData.SizeY; y++)
            {
                for (int z = 0; z < ChunkData.SizeZ; z++)
                {
                    for (int x = 0; x < ChunkData.SizeX; x++)
                    {
                        if (chunk.GetBlock(x, y, z) != BlockType.Air)
                            nonAir++;
                    }
                }
            }

            Debug.Log(
                $"WorldManager: чанк ({chunkX}, {chunkZ}) " +
                $"загружен. Непустых блоков: {nonAir}");

            offset += chunkSize;
        }
    }

    RebuildAllMeshes();

    Debug.Log("WorldManager: все 9 чанков загружены и меши пересобраны.");

    return true;
}
        /// <summary>Перевести мировую координату в координату чанка.</summary>
        private static Vector2Int WorldToChunkCoord(Vector3Int worldPos)
        {
            int chunkX = FloorDiv(worldPos.x, ChunkData.SizeX);
            int chunkZ = FloorDiv(worldPos.z, ChunkData.SizeZ);

            return new Vector2Int(chunkX, chunkZ);
        }

        /// <summary>Перевести мировую координату в локальную координату чанка.</summary>
        private static Vector3Int WorldToLocal(Vector3Int worldPos)
        {
            int localX = Mod(worldPos.x, ChunkData.SizeX);
            int localZ = Mod(worldPos.z, ChunkData.SizeZ);

            return new Vector3Int(localX, worldPos.y, localZ);
        }

        /// <summary>Целочисленное деление с округлением вниз.</summary>
        private static int FloorDiv(int value, int divisor)
        {
            int result = value / divisor;

            if (value < 0 && value % divisor != 0)
                result--;

            return result;
        }

        /// <summary>Положительный остаток для отрицательных координат.</summary>
        private static int Mod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        /// <summary>Перестроить меш одного чанка.</summary>
        private void RebuildChunk(Vector2Int coord)
        {
            if (!_chunks.TryGetValue(coord, out ChunkData chunk))
                return;

            if (!_meshFilters.TryGetValue(coord, out MeshFilter meshFilter))
                return;

            Mesh newMesh = ChunkMesher.BuildMesh(chunk);

            if (_chunkMeshes.TryGetValue(coord, out Mesh oldMesh))
            {
                if (oldMesh != null)
                    Destroy(oldMesh);
            }

            _chunkMeshes[coord] = newMesh;
            meshFilter.sharedMesh = newMesh;
        }

        /// <summary>Перестроить меши всех чанков.</summary>
        private void RebuildAllMeshes()
        {
            foreach (Vector2Int coord in _chunks.Keys)
                RebuildChunk(coord);
        }

        /// <summary>Удалить все объекты чанков.</summary>
        private void ClearWorld()
        {
            foreach (Mesh mesh in _chunkMeshes.Values)
            {
                if (mesh != null)
                    Destroy(mesh);
            }

            _chunkMeshes.Clear();
            _meshFilters.Clear();
            _chunks.Clear();

            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }
    }
}