using System;
using UnityEngine;

namespace Voxel
{
    /// <summary>
    /// Локальное сохранение мира, игрока и инвентаря через PlayerPrefs
    /// (в редакторе — реестр/файлы, в WebGL — localStorage).
    /// Сохраняемся с задержкой после изменений и при выходе/паузе.
    /// </summary>
    public class SaveSystem : MonoBehaviour
    {
        private const string SaveKey = "voxel_save_v6";

        [Header("Сохранение")]
        [SerializeField] private float saveDelay = 1f; // задержка, чтобы не писать на каждый клик

        private WorldManager _world;
        private InventorySystem _inventory;
        private Transform _player;
        private float _saveTimer = -1f;

        [SerializeField] private float autoSaveInterval = 5f;
        private float _autoSaveTimer;

        private Vector3 _lastSafePlayerPosition;
        private bool _hasSafePlayerPosition;

        /// <summary>Формат сохранения.</summary>
        [Serializable]
        private class SaveData
        {
            public string blocksBase64;
            public Vector3 playerPosition;
            public int[] slotTypes;
            public int[] slotCounts;
            public int selected;
            public float health;
            public float hunger;
        }

        private SurvivalStats _stats;

        private void Start()
        {
            _world = FindObjectOfType<WorldManager>();
            _inventory = FindObjectOfType<InventorySystem>();
            _stats = FindObjectOfType<SurvivalStats>();

            var playerController = FindObjectOfType<PlayerController>();

            if (_world == null || _inventory == null || _stats == null || playerController == null)
            {
                Debug.LogError("SaveSystem: не найдены необходимые компоненты.");
                enabled = false;
                return;
            }
            _player = playerController.transform;

            _world.WorldChanged += RequestSave;

            Load();

            if (IsSafePlayerPosition(_player.position))
            {
                _lastSafePlayerPosition = _player.position;
                _hasSafePlayerPosition = true;
            }
            else
            {
                _lastSafePlayerPosition = new Vector3(8f, 12f, 8f);
                _hasSafePlayerPosition = true;

                _player.position = _lastSafePlayerPosition;

                Debug.LogWarning(
                    $"SaveSystem: позиция игрока была слишком высокой или некорректной. " +
                    $"Игрок перемещён в безопасную точку {_lastSafePlayerPosition}.");
            }

            _autoSaveTimer = autoSaveInterval;
        }
        private void Update()
        {
            if (IsSafePlayerPosition(_player.position))
            {
                _lastSafePlayerPosition = _player.position;
                _hasSafePlayerPosition = true;
            }

            if (_saveTimer > 0f)
            {
                _saveTimer -= Time.deltaTime;

                if (_saveTimer <= 0f)
                    DoSave();
            }

            _autoSaveTimer -= Time.deltaTime;

            if (_autoSaveTimer <= 0f)
                DoSave();

            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                PlayerPrefs.DeleteKey(SaveKey);

                _inventory.Clear();
                _stats.ResetStats();

                _world.Regenerate();

                _player.position = new Vector3(8f, 12f, 8f);

                _lastSafePlayerPosition = _player.position;
                _hasSafePlayerPosition = true;

                _autoSaveTimer = autoSaveInterval;
            }
        }

        private void OnApplicationQuit()
        {
            DoSave();
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause)
                DoSave();
        }

        /// <summary>Помечает, что через saveDelay нужно сохраниться.</summary>
        private void RequestSave()
        {
            _saveTimer = saveDelay;
        }
        
        private static bool IsSafePlayerPosition(Vector3 position)
        {
            return
                !float.IsNaN(position.x) &&
                !float.IsNaN(position.y) &&
                !float.IsNaN(position.z) &&
                !float.IsInfinity(position.x) &&
                !float.IsInfinity(position.y) &&
                !float.IsInfinity(position.z) &&
                position.y >= 0f &&
                position.y <= ChunkData.SizeY + 8f;
        }


        /// <summary>Собирает данные и пишет сейв немедленно.</summary>
        private void DoSave()
        {
            _saveTimer = -1f;

            byte[] worldBytes = _world.GetBlocksBytes();

            Vector3 positionToSave = _hasSafePlayerPosition
                ? _lastSafePlayerPosition
                : _player.position;

            Debug.Log(
                $"SaveSystem: сохраняю мир. " +
                $"Байт: {worldBytes.Length}. " +
                $"Позиция игрока: {positionToSave}");

            var data = new SaveData
            {
                blocksBase64 = Convert.ToBase64String(worldBytes),
                playerPosition = positionToSave,
            };

            (data.slotTypes, data.slotCounts, data.selected) =
                _inventory.GetSaveData();

            (data.health, data.hunger) =
                _stats.GetSaveStats();

            PlayerPrefs.SetString(
                SaveKey,
                JsonUtility.ToJson(data));

            PlayerPrefs.Save();

            _autoSaveTimer = autoSaveInterval;
        }

        /// <summary>Загружает сейв, если он есть.</summary>
        private void Load()
        {
            if (!PlayerPrefs.HasKey(SaveKey))
                return;

            try
            {
                var json = PlayerPrefs.GetString(SaveKey);
                var data = JsonUtility.FromJson<SaveData>(json);

                if (data == null || string.IsNullOrEmpty(data.blocksBase64))
                {
                    Debug.LogWarning("SaveSystem: сохранение повреждено или пустое.");
                    return;
                }

                byte[] blockBytes = Convert.FromBase64String(data.blocksBase64);

                const int WorldChunkCount = 9;

                int expectedBlockBytes =
                    ChunkData.SizeX *
                    ChunkData.SizeY *
                    ChunkData.SizeZ *
                    WorldChunkCount;

                if (blockBytes.Length != expectedBlockBytes)
                {
                    Debug.LogWarning(
                        $"SaveSystem: неверный размер данных мира: " +
                        $"{blockBytes.Length}, ожидалось {expectedBlockBytes}.");
                    return;
                }

                if (!_inventory.IsValidSaveData(
                        data.slotTypes,
                        data.slotCounts,
                        data.selected))
                {
                    Debug.LogWarning("SaveSystem: данные инвентаря повреждены.");
                    return;
                }

                if (!_world.SetBlocksBytes(blockBytes))
                {
                    Debug.LogError(
                        $"SaveSystem: не удалось загрузить данные мира. " +
                        $"Получено байт: {blockBytes.Length}.");
                    return;
                }

                Debug.Log($"SaveSystem: загружена позиция игрока {data.playerPosition}");
                _player.position = data.playerPosition;
                if (!_inventory.ApplySaveData(
                        data.slotTypes,
                        data.slotCounts,
                        data.selected))
                {
                    Debug.LogWarning("SaveSystem: не удалось загрузить инвентарь.");
                    return;
                }

                _stats.ApplySaveStats(data.health, data.hunger);
            }
            catch (Exception e)
            {
                Debug.LogError($"SaveSystem: ошибка загрузки сохранения: {e.Message}");
            }
        }
    }
}