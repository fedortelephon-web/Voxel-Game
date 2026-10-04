#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.Profiling;

namespace Voxel
{
    /// <summary>
    /// Встроенный отладочный экран для профилирования производительности.
    /// В релизной сборке код не компилируется.
    /// F3 — показать/скрыть панель.
    /// </summary>
    public class PerformanceDebugOverlay : MonoBehaviour
    {
        private WorldManager world;
        private Transform player;

        private bool visible = true;
        private float fps;
        private float frameMs;
        private float managedMemoryMb;
        private float allocatedMemoryMb;
        private float reservedMemoryMb;
        private float sampleTimer;

        private GUIStyle labelStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (FindObjectOfType<PerformanceDebugOverlay>() != null)
                return;

            GameObject go = new GameObject("PerformanceDebugOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<PerformanceDebugOverlay>();
        }

        private void Start()
        {
            world = FindObjectOfType<WorldManager>();

            PlayerController playerController =
                FindObjectOfType<PlayerController>();

            if (playerController != null)
                player = playerController.transform;

            fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3))
                visible = !visible;

            // Считаем FPS каждый кадр без GC-аллокаций.
            float delta = Time.unscaledDeltaTime;

            if (delta > 0.0001f)
            {
                float currentFps = 1f / delta;
                fps = Mathf.Lerp(fps, currentFps, 0.08f);
            }

            frameMs = delta * 1000f;

            // Память читаем не каждый кадр, чтобы сама диагностика
            // практически не влияла на измерения.
            sampleTimer -= delta;

            if (sampleTimer <= 0f)
            {
                sampleTimer = 0.25f;

                managedMemoryMb =
                    System.GC.GetTotalMemory(false) /
                    (1024f * 1024f);

                allocatedMemoryMb =
                    Profiler.GetTotalAllocatedMemoryLong() /
                    (1024f * 1024f);

                reservedMemoryMb =
                    Profiler.GetTotalReservedMemoryLong() /
                    (1024f * 1024f);
            }
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            EnsureStyles();

            GUI.Box(
                new Rect(10f, 10f, 430f, 248f),
                GUIContent.none);

            GUILayout.BeginArea(
                new Rect(18f, 15f, 414f, 238f));

            GUILayout.Label(
                $"FPS: {fps:F1}   Frame: {frameMs:F2} ms",
                labelStyle);

            GUILayout.Label(
                $"Managed memory: {managedMemoryMb:F1} MB",
                labelStyle);

            GUILayout.Label(
                $"Unity allocated: {allocatedMemoryMb:F1} MB",
                labelStyle);

            GUILayout.Label(
                $"Unity reserved: {reservedMemoryMb:F1} MB",
                labelStyle);

            if (world != null)
            {
                GUILayout.Label(
                    $"Chunks RAM: {world.ChunkCount}   Visible: {world.VisibleChunkCount}",
                    labelStyle);

                GUILayout.Label(
                    $"Render: {world.RenderDistance}   Load: {world.LoadDistance}   " +
                    $"Queues: C {world.ChunkLoadQueueCount} / M {world.MeshBuildQueueCount}",
                    labelStyle);

                GUILayout.Label(
                    $"World generation: {world.LastWorldGenerationMs:F1} ms",
                    labelStyle);

                GUILayout.Label(
                    $"Visible mesh build: {world.LastWorldMeshBuildMs:F1} ms",
                    labelStyle);

                GUILayout.Label(
                    $"Last chunk generation: {world.LastChunkGenerationMs:F2} ms",
                    labelStyle);

                GUILayout.Label(
                    $"Max chunk generation: {world.MaxChunkGenerationMs:F2} ms",
                    labelStyle);

                GUILayout.Label(
                    $"Last mesh build: {world.LastMeshRebuildMs:F2} ms",
                    labelStyle);

                GUILayout.Label(
                    $"Max mesh build: {world.MaxMeshRebuildMs:F2} ms",
                    labelStyle);

                GUILayout.Label(
                    $"Mesh: {world.TotalMeshVertices:N0} verts / " +
                    $"{world.TotalMeshTriangles:N0} tris",
                    labelStyle);
            }

            if (player != null)
            {
                Vector3 p = player.position;

                GUILayout.Label(
                    $"Player: X {p.x:F1}  Z {p.z:F1}",
                    labelStyle);
            }

            GUILayout.Label(
                "F3 — show/hide",
                labelStyle);

            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (labelStyle != null)
                return;

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                richText = false,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                fixedHeight = 12f
            };
        }
    }
}

#endif
