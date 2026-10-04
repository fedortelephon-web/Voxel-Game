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

        [Header("Chunk Streaming")]
        [SerializeField, Min(1)] private int renderDistanceChunks = 4;
        [SerializeField, Range(1, 4)] private int maxMeshBuildsPerFrame = 1;

        /// <summary>Срабатывает после любого изменения блока — для системы сохранения.</summary>
        public event Action WorldChanged;

        private readonly Dictionary<Vector2Int, ChunkData> _chunks =
            new Dictionary<Vector2Int, ChunkData>();

        private readonly Dictionary<Vector2Int, MeshFilter> _meshFilters =
            new Dictionary<Vector2Int, MeshFilter>();

        private readonly Dictionary<Vector2Int, Mesh> _chunkMeshes =
            new Dictionary<Vector2Int, Mesh>();

        private readonly Dictionary<Vector2Int, MeshRenderer> _meshRenderers =
            new Dictionary<Vector2Int, MeshRenderer>();

        private readonly Queue<Vector2Int> _meshBuildQueue =
            new Queue<Vector2Int>();

        private readonly HashSet<Vector2Int> _queuedMeshBuilds =
            new HashSet<Vector2Int>();

        private Material _runtimeBlockMaterial;
        private Transform _player;
        private Vector2Int _currentPlayerChunk;
        private bool _streamingInitialized;
        private bool _initialVisibleBuildPending;
        private float _initialVisibleBuildStartTime;

        private const int WorldSizeX = 32;
        private const int WorldSizeZ = 32;
        private const int WorldMinChunkX = -WorldSizeX / 2;
        private const int WorldMinChunkZ = -WorldSizeZ / 2;

        public int ChunkCount => _chunks.Count;
        public int VisibleChunkCount => _chunkMeshes.Count;
        public int MeshBuildQueueCount => _meshBuildQueue.Count;
        public int RenderDistance => renderDistanceChunks;

        // Диагностика производительности. Значения нужны для профилирования
        // WebGL/Yandex Games и не влияют на генерацию мира.
        public float LastWorldGenerationMs { get; private set; }
        public float LastWorldMeshBuildMs { get; private set; }
        public float LastChunkGenerationMs { get; private set; }
        public float MaxChunkGenerationMs { get; private set; }
        public float LastMeshRebuildMs { get; private set; }
        public float MaxMeshRebuildMs { get; private set; }
        public int TotalMeshVertices { get; private set; }
        public int TotalMeshTriangles { get; private set; }

        /// <summary>Awake выполняется раньше Start других скриптов: мир готов до загрузки сейва.</summary>
        private void Awake()
        {
            EnsureRuntimeMaterial();
            GenerateWorld();
        }

        private void Start()
        {
            // Даём SaveSystem и остальным Start() загрузить состояние мира,
            // после чего начинаем строить только видимые чанки.
            _streamingInitialized = false;
        }

        private void Update()
        {
            if (!_streamingInitialized)
            {
                FindPlayer();

                // Первый Update уже выполняется после Start() всех объектов,
                // поэтому сейв успевает восстановить позицию игрока.
                if (_player != null || Time.frameCount > 2)
                {
                    UpdateVisibleChunks(true);
                    _streamingInitialized = true;
                }
            }
            else
            {
                if (_player == null)
                    FindPlayer();

                if (_player != null)
                    UpdateVisibleChunks(false);
            }

            if (_streamingInitialized)
                ProcessMeshBuildQueue();
        }

        /// <summary>Создать мир из чанков вокруг центрального чанка.</summary>
        private void GenerateWorld()
        {
            float startTime = Time.realtimeSinceStartup;
            MaxChunkGenerationMs = 0f;
            MaxMeshRebuildMs = 0f;

            for (int chunkX = WorldMinChunkX; chunkX < WorldMinChunkX + WorldSizeX; chunkX++)
            {
                for (int chunkZ = WorldMinChunkZ; chunkZ < WorldMinChunkZ + WorldSizeZ; chunkZ++)
                {
                    CreateChunk(chunkX, chunkZ);
                }
            }

            LastWorldGenerationMs =
                (Time.realtimeSinceStartup - startTime) * 1000f;

            // Меши не строим здесь: только данные мира.
            // Видимые чанки будут созданы порциями в Update().
            LastWorldMeshBuildMs = 0f;

            worldGenerator.LogBiomeMap(
                WorldMinChunkX * ChunkData.SizeX,
                (WorldMinChunkX + WorldSizeX) * ChunkData.SizeX,
                WorldMinChunkZ * ChunkData.SizeZ,
                (WorldMinChunkZ + WorldSizeZ) * ChunkData.SizeZ,
                16);
        }

        /// <summary>Создать один чанк и его объект в сцене.</summary>
        private void CreateChunk(int chunkX, int chunkZ)
        {
            var coord = new Vector2Int(chunkX, chunkZ);

            float startTime = Time.realtimeSinceStartup;
            ChunkData chunk = worldGenerator.GenerateChunk(chunkX, chunkZ);
            LastChunkGenerationMs =
                (Time.realtimeSinceStartup - startTime) * 1000f;
            MaxChunkGenerationMs = Mathf.Max(
                MaxChunkGenerationMs,
                LastChunkGenerationMs);

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

            renderer.sharedMaterial = _runtimeBlockMaterial;
            renderer.enabled = false;

            _meshFilters[coord] = meshFilter;
            _meshRenderers[coord] = renderer;
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

        /// <summary>Пересоздать мир с нуля с текущим seed.</summary>
        public void Regenerate()
        {
            ClearWorld();

            GenerateWorld();
            WorldChanged?.Invoke();
        }

        /// <summary>Пересоздать мир с нуля с указанным seed.</summary>
        public void Regenerate(int newSeed)
        {
            worldGenerator.SetSeed(newSeed);
            Regenerate();
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

            for (int chunkX = WorldMinChunkX; chunkX < WorldMinChunkX + WorldSizeX; chunkX++)
            {
                for (int chunkZ = WorldMinChunkZ; chunkZ < WorldMinChunkZ + WorldSizeZ; chunkZ++)
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

    for (int chunkX = WorldMinChunkX; chunkX < WorldMinChunkX + WorldSizeX; chunkX++)
    {
        for (int chunkZ = WorldMinChunkZ; chunkZ < WorldMinChunkZ + WorldSizeZ; chunkZ++)
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

    RebuildVisibleMeshes();

    Debug.Log(
        $"WorldManager: данные всех {_chunks.Count} чанков загружены. " +
        $"Меши видимых чанков поставлены в очередь.");

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

        /// <summary>Перестроить меш одного видимого чанка.</summary>
        private void RebuildChunk(Vector2Int coord)
        {
            if (!_chunks.ContainsKey(coord))
                return;

            if (!_meshFilters.ContainsKey(coord))
                return;

            if (!IsChunkCurrentlyVisible(coord))
                return;

            BuildChunkMesh(coord);
        }

        private void BuildChunkMesh(Vector2Int coord)
        {
            if (!_chunks.TryGetValue(coord, out ChunkData chunk))
                return;

            if (!_meshFilters.TryGetValue(coord, out MeshFilter meshFilter))
                return;

            if (!_meshRenderers.TryGetValue(coord, out MeshRenderer renderer))
                return;

            float startTime = Time.realtimeSinceStartup;

            Mesh newMesh = ChunkMesher.BuildMesh(
                chunk,
                worldGenerator,
                coord.x,
                coord.y);

            LastMeshRebuildMs =
                (Time.realtimeSinceStartup - startTime) * 1000f;

            MaxMeshRebuildMs = Mathf.Max(
                MaxMeshRebuildMs,
                LastMeshRebuildMs);

            RemoveMeshMetrics(coord);

            if (_chunkMeshes.TryGetValue(coord, out Mesh oldMesh))
            {
                if (oldMesh != null)
                    Destroy(oldMesh);
            }

            _chunkMeshes[coord] = newMesh;
            meshFilter.sharedMesh = newMesh;
            renderer.enabled = true;

            AddMeshMetrics(newMesh);
        }

        private void ProcessMeshBuildQueue()
        {
            int buildsThisFrame = 0;

            while (buildsThisFrame < maxMeshBuildsPerFrame &&
                   _meshBuildQueue.Count > 0)
            {
                Vector2Int coord = _meshBuildQueue.Dequeue();
                _queuedMeshBuilds.Remove(coord);

                if (!IsChunkCurrentlyVisible(coord))
                    continue;

                if (_chunkMeshes.ContainsKey(coord))
                    continue;

                BuildChunkMesh(coord);
                buildsThisFrame++;
            }

            if (_initialVisibleBuildPending &&
                _meshBuildQueue.Count == 0)
            {
                LastWorldMeshBuildMs =
                    (Time.realtimeSinceStartup -
                     _initialVisibleBuildStartTime) * 1000f;

                _initialVisibleBuildPending = false;
            }
        }

        private void UpdateVisibleChunks(bool force)
        {
            if (_player == null)
                FindPlayer();

            Vector2Int playerChunk;

            if (_player != null)
            {
                playerChunk = WorldToChunkCoord(
                    Vector3Int.FloorToInt(_player.position));
            }
            else
            {
                playerChunk = new Vector2Int(0, 0);
            }

            if (!force && playerChunk == _currentPlayerChunk)
                return;

            _currentPlayerChunk = playerChunk;

            _meshBuildQueue.Clear();
            _queuedMeshBuilds.Clear();

            var desired = new List<Vector2Int>();

            foreach (Vector2Int coord in _meshFilters.Keys)
            {
                if (IsWithinRenderDistance(coord, playerChunk))
                    desired.Add(coord);
                else
                    UnloadChunkMesh(coord);
            }

            desired.Sort(
                (a, b) =>
                {
                    int da = GetChunkDistanceSquared(a, playerChunk);
                    int db = GetChunkDistanceSquared(b, playerChunk);
                    return da.CompareTo(db);
                });

            int missingBefore = 0;

            foreach (Vector2Int coord in desired)
            {
                if (_meshRenderers.TryGetValue(
                        coord,
                        out MeshRenderer renderer))
                {
                    renderer.enabled = _chunkMeshes.ContainsKey(coord);
                }

                if (!_chunkMeshes.ContainsKey(coord))
                {
                    EnqueueMeshBuild(coord);
                    missingBefore++;
                }
            }

            if (force && missingBefore > 0)
            {
                _initialVisibleBuildStartTime =
                    Time.realtimeSinceStartup;
                _initialVisibleBuildPending = true;
            }
        }

        private void EnqueueMeshBuild(Vector2Int coord)
        {
            if (!_queuedMeshBuilds.Add(coord))
                return;

            _meshBuildQueue.Enqueue(coord);
        }

        private bool IsChunkCurrentlyVisible(Vector2Int coord)
        {
            return IsWithinRenderDistance(
                coord,
                _currentPlayerChunk);
        }

        private bool IsWithinRenderDistance(
            Vector2Int coord,
            Vector2Int center)
        {
            return GetChunkDistanceSquared(coord, center) <=
                   renderDistanceChunks * renderDistanceChunks;
        }

        private static int GetChunkDistanceSquared(
            Vector2Int a,
            Vector2Int b)
        {
            int dx = a.x - b.x;
            int dz = a.y - b.y;
            return dx * dx + dz * dz;
        }

        private void RebuildVisibleMeshes()
        {
            _meshBuildQueue.Clear();
            _queuedMeshBuilds.Clear();

            foreach (Vector2Int coord in _meshFilters.Keys)
            {
                if (!IsChunkCurrentlyVisible(coord))
                    continue;

                UnloadChunkMesh(coord);
                EnqueueMeshBuild(coord);
            }
        }

        private void UnloadChunkMesh(Vector2Int coord)
        {
            if (_meshRenderers.TryGetValue(
                    coord,
                    out MeshRenderer renderer))
            {
                renderer.enabled = false;
            }

            if (_meshFilters.TryGetValue(
                    coord,
                    out MeshFilter meshFilter))
            {
                meshFilter.sharedMesh = null;
            }

            if (_chunkMeshes.TryGetValue(
                    coord,
                    out Mesh mesh))
            {
                RemoveMeshMetrics(coord);

                if (mesh != null)
                    Destroy(mesh);

                _chunkMeshes.Remove(coord);
            }
        }

        private void AddMeshMetrics(Mesh mesh)
        {
            if (mesh == null)
                return;

            TotalMeshVertices += mesh.vertexCount;

            if (mesh.subMeshCount > 0)
                TotalMeshTriangles +=
                    (int)(mesh.GetIndexCount(0) / 3);
        }

        private void RemoveMeshMetrics(Vector2Int coord)
        {
            if (!_chunkMeshes.TryGetValue(
                    coord,
                    out Mesh mesh) ||
                mesh == null)
                return;

            TotalMeshVertices -= mesh.vertexCount;

            if (mesh.subMeshCount > 0)
                TotalMeshTriangles -=
                    (int)(mesh.GetIndexCount(0) / 3);

            TotalMeshVertices = Mathf.Max(
                TotalMeshVertices,
                0);

            TotalMeshTriangles = Mathf.Max(
                TotalMeshTriangles,
                0);
        }

        private void FindPlayer()
        {
            PlayerController controller =
                FindObjectOfType<PlayerController>();

            if (controller != null)
                _player = controller.transform;
        }

        private void EnsureRuntimeMaterial()
        {
            if (_runtimeBlockMaterial != null)
                return;

            if (blockMaterial == null)
                return;

            _runtimeBlockMaterial = new Material(blockMaterial)
            {
                name = blockMaterial.name + " (Runtime)"
            };

            _runtimeBlockMaterial.mainTexture =
                VoxelTextures.Atlas;
        }

        /// <summary>Удалить все объекты чанков.</summary>
        private void ClearWorld()
        {
            foreach (Mesh mesh in _chunkMeshes.Values)
            {
                if (mesh != null)
                    Destroy(mesh);
            }

            _meshBuildQueue.Clear();
            _queuedMeshBuilds.Clear();
            _chunkMeshes.Clear();
            _meshFilters.Clear();
            _meshRenderers.Clear();
            _chunks.Clear();
            TotalMeshVertices = 0;
            TotalMeshTriangles = 0;
            _streamingInitialized = false;
            _initialVisibleBuildPending = false;

            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }

        private void OnDestroy()
        {
            if (_runtimeBlockMaterial != null)
                Destroy(_runtimeBlockMaterial);
        }
    }
}