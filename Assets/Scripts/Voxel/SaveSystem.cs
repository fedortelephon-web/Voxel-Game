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
        private const string SaveKey = "voxel_save_v5";

        [Header("Сохранение")]
        [SerializeField] private float saveDelay = 1f; // задержка, чтобы не писать на каждый клик

        private WorldManager _world;
        private InventorySystem _inventory;
        private Transform _player;
        private float _saveTimer = -1f;

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
        }

        private void Update()
        {
            // Отложенное сохранение после изменений мира
            if (_saveTimer > 0f)
            {
                _saveTimer -= Time.deltaTime;
                if (_saveTimer <= 0f)
                    DoSave();
            }

            // Backspace — новая игра: удаляем сейв и пересоздаём мир
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                PlayerPrefs.DeleteKey(SaveKey);
                _inventory.Clear();
                _stats.ResetStats();
                _world.Regenerate();
                _player.position = new Vector3(8f, 12f, 8f);
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

        /// <summary>Собирает данные и пишет сейв немедленно.</summary>
        private void DoSave()
        {
            _saveTimer = -1f;

            var data = new SaveData
            {
                blocksBase64 = Convert.ToBase64String(_world.GetBlocksBytes()),
                playerPosition = _player.position,
            };

            (data.slotTypes, data.slotCounts, data.selected) = _inventory.GetSaveData();
            (data.health, data.hunger) = _stats.GetSaveStats();
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
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

                int expectedBlockBytes =
                    ChunkData.SizeX * ChunkData.SizeY * ChunkData.SizeZ;

                if (blockBytes.Length != expectedBlockBytes)
                {
                    Debug.LogWarning(
                        $"SaveSystem: неверный размер данных мира: " +
                        $"{blockBytes.Length}, ожидалось {expectedBlockBytes}.");
                    return;
                }

                if (!_world.SetBlocksBytes(blockBytes))
                {
                    Debug.LogWarning("SaveSystem: не удалось загрузить данные мира.");
                    return;
                }

                _player.position = data.playerPosition;
                if (!_inventory.ApplySaveData(data.slotTypes, data.slotCounts, data.selected))
                {
                    Debug.LogWarning("SaveSystem: данные инвентаря повреждены.");
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