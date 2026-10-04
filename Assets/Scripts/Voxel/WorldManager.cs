using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voxel
{
    /// <summary>
    /// Управляет чанками бесконечного процедурного мира.
    /// Загружает только чанки вокруг игрока, меши строит постепенно,
    /// далёкие чанки выгружает из RAM.
    /// </summary>
    public class WorldManager : MonoBehaviour
    {
        [SerializeField] private Material blockMaterial;
        [SerializeField] private WorldGenerator worldGenerator;

        [Header("Chunk Streaming")]
        [SerializeField, Min(1)] private int renderDistanceChunks = 4;
        [SerializeField, Min(1)] private int loadDistanceChunks = 5;
        [SerializeField, Range(1, 4)] private int maxChunkGenerationsPerFrame = 1;
        [SerializeField, Range(1, 4)] private int maxMeshBuildsPerFrame = 1;

        public event Action WorldChanged;

        [Serializable]
        public class SavedChunk
        {
            public int x;
            public int z;
            public string blocksBase64;
        }

        private readonly Dictionary<Vector2Int, ChunkData> _chunks =
            new Dictionary<Vector2Int, ChunkData>();

        private readonly Dictionary<Vector2Int, MeshFilter> _meshFilters =
            new Dictionary<Vector2Int, MeshFilter>();

        private readonly Dictionary<Vector2Int, MeshRenderer> _meshRenderers =
            new Dictionary<Vector2Int, MeshRenderer>();

        private readonly Dictionary<Vector2Int, Mesh> _chunkMeshes =
            new Dictionary<Vector2Int, Mesh>();

        private readonly Dictionary<Vector2Int, byte[]> _savedChunkData =
            new Dictionary<Vector2Int, byte[]>();

        private readonly HashSet<Vector2Int> _dirtyChunks =
            new HashSet<Vector2Int>();

        private readonly Queue<Vector2Int> _chunkLoadQueue =
            new Queue<Vector2Int>();

        private readonly HashSet<Vector2Int> _queuedChunkLoads =
            new HashSet<Vector2Int>();

        private readonly Queue<Vector2Int> _meshBuildQueue =
            new Queue<Vector2Int>();

        private readonly HashSet<Vector2Int> _queuedMeshBuilds =
            new HashSet<Vector2Int>();

        private Material _runtimeBlockMaterial;
        private Transform _player;
        private Vector2Int _currentPlayerChunk;
        private bool _streamingInitialized;

        private bool _initialWorldGenerationPending;
        private float _initialWorldGenerationStartTime;

        private bool _initialVisibleMeshBuildPending;
        private bool _initialVisibleMeshTimingStarted;
        private float _initialVisibleMeshBuildStartTime;

        public int ChunkCount => _chunks.Count;
        public int VisibleChunkCount => _chunkMeshes.Count;
        public int MeshBuildQueueCount => _meshBuildQueue.Count;
        public int ChunkLoadQueueCount => _chunkLoadQueue.Count;
        public int RenderDistance => renderDistanceChunks;
        public int LoadDistance => loadDistanceChunks;
        public int WorldSeed => worldGenerator != null ? worldGenerator.Seed : 0;

        public float LastWorldGenerationMs { get; private set; }
        public float LastWorldMeshBuildMs { get; private set; }
        public float LastChunkGenerationMs { get; private set; }
        public float MaxChunkGenerationMs { get; private set; }
        public float LastMeshRebuildMs { get; private set; }
        public float MaxMeshRebuildMs { get; private set; }
        public int TotalMeshVertices { get; private set; }
        public int TotalMeshTriangles { get; private set; }

        private void Awake()
        {
            EnsureRuntimeMaterial();
            ResetPerformanceMetrics();
        }

        private void Start()
        {
            _streamingInitialized = false;
        }

        private void Update()
        {
            if (!_streamingInitialized)
            {
                FindPlayer();

                if (_player != null)
                {
                    UpdateChunkStreaming(true);
                    _streamingInitialized = true;
                }
            }
            else
            {
                if (_player == null)
                    FindPlayer();

                if (_player != null)
                    UpdateChunkStreaming(false);
            }

            if (_streamingInitialized)
            {
                ProcessChunkLoadQueue();
                ProcessMeshBuildQueue();
            }
        }

        private void UpdateChunkStreaming(bool force)
        {
            if (_player == null)
                return;

            Vector2Int playerChunk = WorldToChunkCoord(
                Vector3Int.FloorToInt(_player.position));

            if (!force && playerChunk == _currentPlayerChunk)
                return;

            _currentPlayerChunk = playerChunk;

            _chunkLoadQueue.Clear();
            _queuedChunkLoads.Clear();

            var unload = new List<Vector2Int>();

            foreach (Vector2Int coord in _chunks.Keys)
            {
                if (!IsWithinLoadDistance(coord, playerChunk))
                    unload.Add(coord);
            }

            foreach (Vector2Int coord in unload)
                UnloadChunk(coord);

            var desired = GetChunkCoordsInRadius(
                playerChunk,
                loadDistanceChunks);

            desired.Sort((a, b) =>
            {
                int da = GetChunkDistanceSquared(a, playerChunk);
                int db = GetChunkDistanceSquared(b, playerChunk);

                bool av = da <= renderDistanceChunks * renderDistanceChunks;
                bool bv = db <= renderDistanceChunks * renderDistanceChunks;

                if (av != bv)
                    return av ? -1 : 1;

                return da.CompareTo(db);
            });

            foreach (Vector2Int coord in desired)
            {
                if (!_chunks.ContainsKey(coord))
                    EnqueueChunkLoad(coord);
            }

            if (force)
            {
                _initialWorldGenerationStartTime =
                    Time.realtimeSinceStartup;
                _initialWorldGenerationPending = true;

                _initialVisibleMeshBuildPending = true;
                _initialVisibleMeshTimingStarted = false;
                LastWorldGenerationMs = 0f;
                LastWorldMeshBuildMs = 0f;
            }
        }

        private static List<Vector2Int> GetChunkCoordsInRadius(
            Vector2Int center,
            int radius)
        {
            var result = new List<Vector2Int>();
            int radiusSquared = radius * radius;

            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dz * dz > radiusSquared)
                    continue;

                result.Add(new Vector2Int(
                    center.x + dx,
                    center.y + dz));
            }

            return result;
        }

        private void EnqueueChunkLoad(Vector2Int coord)
        {
            if (_queuedChunkLoads.Add(coord))
                _chunkLoadQueue.Enqueue(coord);
        }

        private void ProcessChunkLoadQueue()
        {
            int generated = 0;

            while (generated < maxChunkGenerationsPerFrame &&
                   _chunkLoadQueue.Count > 0)
            {
                Vector2Int coord = _chunkLoadQueue.Dequeue();
                _queuedChunkLoads.Remove(coord);

                if (!IsWithinLoadDistance(
                        coord,
                        _currentPlayerChunk))
                    continue;

                if (_chunks.ContainsKey(coord))
                    continue;

                CreateChunk(coord.x, coord.y);
                generated++;
            }

            if (_initialWorldGenerationPending &&
                _chunkLoadQueue.Count == 0)
            {
                LastWorldGenerationMs =
                    (Time.realtimeSinceStartup -
                     _initialWorldGenerationStartTime) * 1000f;

                _initialWorldGenerationPending = false;
            }
        }

        private void CreateChunk(int chunkX, int chunkZ)
        {
            Vector2Int coord =
                new Vector2Int(chunkX, chunkZ);

            float start = Time.realtimeSinceStartup;

            ChunkData chunk =
                worldGenerator.GenerateChunk(
                    chunkX,
                    chunkZ);

            LastChunkGenerationMs =
                (Time.realtimeSinceStartup - start) * 1000f;

            MaxChunkGenerationMs =
                Mathf.Max(
                    MaxChunkGenerationMs,
                    LastChunkGenerationMs);

            if (_savedChunkData.TryGetValue(
                    coord,
                    out byte[] saved))
            {
                chunk.FromBytes(saved);
            }

            _chunks[coord] = chunk;

            GameObject go =
                new GameObject(
                    $"Chunk_{chunkX}_{chunkZ}");

            go.transform.SetParent(transform);
            go.transform.localPosition =
                new Vector3(
                    chunkX * ChunkData.SizeX,
                    0f,
                    chunkZ * ChunkData.SizeZ);

            MeshFilter filter =
                go.AddComponent<MeshFilter>();

            MeshRenderer renderer =
                go.AddComponent<MeshRenderer>();

            renderer.sharedMaterial =
                _runtimeBlockMaterial;
            renderer.enabled = false;

            _meshFilters[coord] = filter;
            _meshRenderers[coord] = renderer;

            if (IsWithinRenderDistance(
                    coord,
                    _currentPlayerChunk))
            {
                EnqueueMeshBuild(coord);
            }

            for (int i = 0; i < 4; i++)
            {
                Vector2Int neighbor =
                    coord + GetHorizontalDirection(i);

                if (!IsWithinRenderDistance(
                        neighbor,
                        _currentPlayerChunk))
                    continue;

                if (_chunkMeshes.ContainsKey(neighbor))
                    EnqueueMeshBuild(neighbor);
            }
        }

        private static Vector2Int GetHorizontalDirection(int index)
        {
            switch (index)
            {
                case 0: return Vector2Int.left;
                case 1: return Vector2Int.right;
                case 2: return new Vector2Int(0, -1);
                default: return new Vector2Int(0, 1);
            }
        }

        public BlockType GetBlock(Vector3Int worldPos)
        {
            if (worldPos.y < 0 ||
                worldPos.y >= ChunkData.SizeY)
                return BlockType.Air;

            Vector2Int chunkCoord =
                WorldToChunkCoord(worldPos);

            Vector3Int local =
                WorldToLocal(worldPos);

            if (!_chunks.TryGetValue(
                    chunkCoord,
                    out ChunkData chunk))
                return BlockType.Air;

            return chunk.GetBlock(
                local.x,
                local.y,
                local.z);
        }

        /// <summary>
        /// Получить биомный оттенок растительности через генератор мира.
        /// Кэширование выполняется внутри WorldGenerator.
        /// </summary>
        public Color GetVegetationTint(
            int worldX,
            int worldZ)
        {
            return worldGenerator.GetVegetationTint(
                worldX,
                worldZ);
        }

        public bool InBounds(Vector3Int worldPos)
        {
            if (worldPos.y < 0 ||
                worldPos.y >= ChunkData.SizeY)
                return false;

            return _chunks.ContainsKey(
                WorldToChunkCoord(worldPos));
        }

        public void SetBlock(
            Vector3Int worldPos,
            BlockType type)
        {
            if (!InBounds(worldPos))
                return;

            Vector2Int chunkCoord =
                WorldToChunkCoord(worldPos);

            Vector3Int local =
                WorldToLocal(worldPos);

            _chunks[chunkCoord].SetBlock(
                local.x,
                local.y,
                local.z,
                type);

            _dirtyChunks.Add(chunkCoord);

            RebuildChunk(chunkCoord);

            if (local.x == 0)
                RebuildChunk(
                    chunkCoord + Vector2Int.left);

            if (local.x == ChunkData.SizeX - 1)
                RebuildChunk(
                    chunkCoord + Vector2Int.right);

            if (local.z == 0)
                RebuildChunk(
                    chunkCoord + new Vector2Int(0, -1));

            if (local.z == ChunkData.SizeZ - 1)
                RebuildChunk(
                    chunkCoord + new Vector2Int(0, 1));

            WorldChanged?.Invoke();
        }

        public void Regenerate()
        {
            ClearWorld();
            ResetPerformanceMetrics();
            _streamingInitialized = false;
            WorldChanged?.Invoke();
        }

        public void Regenerate(int newSeed)
        {
            worldGenerator.SetSeed(newSeed);
            Regenerate();
        }

        public void SetSeedForLoading(int newSeed)
        {
            worldGenerator.SetSeed(newSeed);
        }

        /// <summary>
        /// Сериализуются только реально изменённые чанки.
        /// Неизменённый процедурный мир восстанавливается заново по seed.
        /// </summary>
        public SavedChunk[] GetSaveChunks()
        {
            var result = new List<SavedChunk>();
            var added = new HashSet<Vector2Int>();

            foreach (KeyValuePair<Vector2Int, byte[]> pair
                     in _savedChunkData)
            {
                result.Add(CreateSavedChunk(
                    pair.Key,
                    pair.Value));
                added.Add(pair.Key);
            }

            foreach (Vector2Int coord in _dirtyChunks)
            {
                if (added.Contains(coord))
                    continue;

                if (!_chunks.TryGetValue(
                        coord,
                        out ChunkData chunk))
                    continue;

                result.Add(CreateSavedChunk(
                    coord,
                    chunk.ToBytes()));

                added.Add(coord);
            }

            return result.ToArray();
        }

        private static SavedChunk CreateSavedChunk(
            Vector2Int coord,
            byte[] data)
        {
            return new SavedChunk
            {
                x = coord.x,
                z = coord.y,
                blocksBase64 =
                    Convert.ToBase64String(data)
            };
        }

        public void LoadSaveChunks(
            SavedChunk[] savedChunks)
        {
            _savedChunkData.Clear();
            _dirtyChunks.Clear();

            if (savedChunks == null)
                return;

            int expected =
                ChunkData.SizeX *
                ChunkData.SizeY *
                ChunkData.SizeZ;

            foreach (SavedChunk saved in savedChunks)
            {
                if (saved == null ||
                    string.IsNullOrEmpty(
                        saved.blocksBase64))
                    continue;

                try
                {
                    byte[] data =
                        Convert.FromBase64String(
                            saved.blocksBase64);

                    if (data.Length != expected)
                        continue;

                    Vector2Int coord =
                        new Vector2Int(
                            saved.x,
                            saved.z);

                    _savedChunkData[coord] = data;

                    if (_chunks.TryGetValue(
                            coord,
                            out ChunkData loaded))
                    {
                        loaded.FromBytes(data);
                        EnqueueMeshBuild(coord);
                    }
                }
                catch (FormatException)
                {
                    Debug.LogWarning(
                        $"WorldManager: повреждён Base64 чанка " +
                        $"({saved.x}, {saved.z}).");
                }
            }
        }

        private void RebuildChunk(
            Vector2Int coord)
        {
            if (!_chunks.ContainsKey(coord) ||
                !IsChunkCurrentlyVisible(coord))
                return;

            EnqueueMeshBuild(coord);
        }

        private void EnqueueMeshBuild(
            Vector2Int coord)
        {
            if (!_chunks.ContainsKey(coord) ||
                !_meshFilters.ContainsKey(coord))
                return;

            if (_queuedMeshBuilds.Add(coord))
            {
                _meshBuildQueue.Enqueue(coord);

                if (_initialVisibleMeshBuildPending &&
                    !_initialVisibleMeshTimingStarted &&
                    IsWithinRenderDistance(
                        coord,
                        _currentPlayerChunk))
                {
                    _initialVisibleMeshBuildStartTime =
                        Time.realtimeSinceStartup;
                    _initialVisibleMeshTimingStarted = true;
                }
            }
        }

        private void ProcessMeshBuildQueue()
        {
            int built = 0;

            while (built < maxMeshBuildsPerFrame &&
                   _meshBuildQueue.Count > 0)
            {
                Vector2Int coord =
                    _meshBuildQueue.Dequeue();

                _queuedMeshBuilds.Remove(coord);

                if (!IsChunkCurrentlyVisible(coord) ||
                    !_chunks.ContainsKey(coord))
                    continue;

                BuildChunkMesh(coord);
                built++;
            }

            if (_initialVisibleMeshBuildPending &&
                _initialVisibleMeshTimingStarted &&
                _meshBuildQueue.Count == 0 &&
                CountVisibleMeshesAroundPlayer() ==
                    CountChunksInRadius(renderDistanceChunks))
            {
                LastWorldMeshBuildMs =
                    (Time.realtimeSinceStartup -
                     _initialVisibleMeshBuildStartTime) *
                    1000f;

                _initialVisibleMeshBuildPending = false;
            }
        }

        private void BuildChunkMesh(
            Vector2Int coord)
        {
            if (!_chunks.TryGetValue(
                    coord,
                    out ChunkData chunk) ||
                !_meshFilters.TryGetValue(
                    coord,
                    out MeshFilter filter) ||
                !_meshRenderers.TryGetValue(
                    coord,
                    out MeshRenderer renderer))
                return;

            float start =
                Time.realtimeSinceStartup;

            Mesh mesh =
                ChunkMesher.BuildMesh(
                    chunk,
                    this,
                    coord.x,
                    coord.y);

            LastMeshRebuildMs =
                (Time.realtimeSinceStartup - start) *
                1000f;

            MaxMeshRebuildMs =
                Mathf.Max(
                    MaxMeshRebuildMs,
                    LastMeshRebuildMs);

            RemoveMeshMetrics(coord);

            if (_chunkMeshes.TryGetValue(
                    coord,
                    out Mesh old) &&
                old != null)
            {
                Destroy(old);
            }

            _chunkMeshes[coord] = mesh;
            filter.sharedMesh = mesh;
            renderer.enabled = true;

            AddMeshMetrics(mesh);
        }

        private void UnloadChunk(
            Vector2Int coord)
        {
            if (!_chunks.TryGetValue(
                    coord,
                    out ChunkData chunk))
                return;

            if (_dirtyChunks.Contains(coord))
            {
                _savedChunkData[coord] =
                    chunk.ToBytes();

                _dirtyChunks.Remove(coord);
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

            if (_meshFilters.TryGetValue(
                    coord,
                    out MeshFilter filter))
            {
                filter.sharedMesh = null;
            }

            for (int i = 0; i < 4; i++)
            {
                Vector2Int neighbor =
                    coord + GetHorizontalDirection(i);

                if (IsWithinRenderDistance(
                        neighbor,
                        _currentPlayerChunk) &&
                    _chunkMeshes.ContainsKey(neighbor))
                {
                    EnqueueMeshBuild(neighbor);
                }
            }

            if (_meshFilters.TryGetValue(
                    coord,
                    out MeshFilter chunkFilter) &&
                chunkFilter != null)
            {
                Destroy(chunkFilter.gameObject);
            }

            _meshFilters.Remove(coord);
            _meshRenderers.Remove(coord);
            _chunks.Remove(coord);
        }

        private int CountVisibleMeshesAroundPlayer()
        {
            int count = 0;

            foreach (Vector2Int coord in _chunkMeshes.Keys)
            {
                if (IsWithinRenderDistance(
                        coord,
                        _currentPlayerChunk))
                    count++;
            }

            return count;
        }

        private static int CountChunksInRadius(
            int radius)
        {
            int count = 0;
            int radiusSquared = radius * radius;

            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dz * dz <= radiusSquared)
                    count++;
            }

            return count;
        }

        private bool IsChunkCurrentlyVisible(
            Vector2Int coord)
        {
            return IsWithinRenderDistance(
                coord,
                _currentPlayerChunk);
        }

        private bool IsWithinRenderDistance(
            Vector2Int coord,
            Vector2Int center)
        {
            return GetChunkDistanceSquared(
                coord,
                center) <=
                renderDistanceChunks *
                renderDistanceChunks;
        }

        private bool IsWithinLoadDistance(
            Vector2Int coord,
            Vector2Int center)
        {
            return GetChunkDistanceSquared(
                coord,
                center) <=
                loadDistanceChunks *
                loadDistanceChunks;
        }

        private static int GetChunkDistanceSquared(
            Vector2Int a,
            Vector2Int b)
        {
            int dx = a.x - b.x;
            int dz = a.y - b.y;
            return dx * dx + dz * dz;
        }

        private static Vector2Int WorldToChunkCoord(
            Vector3Int worldPos)
        {
            return new Vector2Int(
                FloorDiv(
                    worldPos.x,
                    ChunkData.SizeX),
                FloorDiv(
                    worldPos.z,
                    ChunkData.SizeZ));
        }

        private static Vector3Int WorldToLocal(
            Vector3Int worldPos)
        {
            return new Vector3Int(
                Mod(worldPos.x, ChunkData.SizeX),
                worldPos.y,
                Mod(worldPos.z, ChunkData.SizeZ));
        }

        private static int FloorDiv(
            int value,
            int divisor)
        {
            int result = value / divisor;

            if (value < 0 &&
                value % divisor != 0)
                result--;

            return result;
        }

        private static int Mod(
            int value,
            int divisor)
        {
            int result = value % divisor;
            return result < 0
                ? result + divisor
                : result;
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
            if (_runtimeBlockMaterial != null ||
                blockMaterial == null)
                return;

            _runtimeBlockMaterial =
                new Material(blockMaterial);

            _runtimeBlockMaterial.name =
                blockMaterial.name +
                " (Runtime)";

            _runtimeBlockMaterial.mainTexture =
                VoxelTextures.Atlas;
        }

        private void ResetPerformanceMetrics()
        {
            LastWorldGenerationMs = 0f;
            LastWorldMeshBuildMs = 0f;
            LastChunkGenerationMs = 0f;
            MaxChunkGenerationMs = 0f;
            LastMeshRebuildMs = 0f;
            MaxMeshRebuildMs = 0f;
            TotalMeshVertices = 0;
            TotalMeshTriangles = 0;
        }

        private void ClearWorld()
        {
            foreach (Mesh mesh in _chunkMeshes.Values)
            {
                if (mesh != null)
                    Destroy(mesh);
            }

            _meshBuildQueue.Clear();
            _queuedMeshBuilds.Clear();
            _chunkLoadQueue.Clear();
            _queuedChunkLoads.Clear();

            _chunkMeshes.Clear();
            _meshFilters.Clear();
            _meshRenderers.Clear();
            _chunks.Clear();

            _savedChunkData.Clear();
            _dirtyChunks.Clear();

            _currentPlayerChunk = Vector2Int.zero;
            _streamingInitialized = false;
            _initialWorldGenerationPending = false;
            _initialVisibleMeshBuildPending = false;
            _initialVisibleMeshTimingStarted = false;

            TotalMeshVertices = 0;
            TotalMeshTriangles = 0;

            for (int i = transform.childCount - 1;
                 i >= 0;
                 i--)
            {
                Destroy(
                    transform.GetChild(i).gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_runtimeBlockMaterial != null)
                Destroy(_runtimeBlockMaterial);
        }
    }
}