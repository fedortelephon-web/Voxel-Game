using System;
using UnityEngine;

namespace Voxel
{
    /// <summary>
    /// Сохраняет только изменённые чанки бесконечного процедурного мира,
    /// позицию игрока, инвентарь и характеристики.
    /// </summary>
    public class SaveSystem : MonoBehaviour
    {
        private const string SaveKey = "voxel_save_v23";

        [Header("Сохранение")]
        [SerializeField] private float saveDelay = 1f;
        [SerializeField] private float autoSaveInterval = 5f;

        private WorldManager _world;
        private InventorySystem _inventory;
        private Transform _player;
        private SurvivalStats _stats;

        private float _saveTimer = -1f;
        private float _autoSaveTimer;

        private Vector3 _lastSafePlayerPosition;
        private bool _hasSafePlayerPosition;

        [Serializable]
        private class SaveData
        {
            public bool hasWorldSeed;
            public int worldSeed;
            public WorldManager.SavedChunk[] changedChunks;

            public Vector3 playerPosition;

            public int[] slotTypes;
            public int[] slotCounts;
            public int selected;

            public float health;
            public float hunger;
        }

        private void Start()
        {
            _world = FindObjectOfType<WorldManager>();
            _inventory = FindObjectOfType<InventorySystem>();
            _stats = FindObjectOfType<SurvivalStats>();

            PlayerController controller =
                FindObjectOfType<PlayerController>();

            if (_world == null ||
                _inventory == null ||
                _stats == null ||
                controller == null)
            {
                Debug.LogError(
                    "SaveSystem: не найдены необходимые компоненты.");
                enabled = false;
                return;
            }

            _player = controller.transform;
            _world.WorldChanged += RequestSave;

            Load();

            if (IsSafePlayerPosition(
                    _player.position))
            {
                _lastSafePlayerPosition = _player.position;
                _hasSafePlayerPosition = true;
            }
            else
            {
                _lastSafePlayerPosition =
                    new Vector3(8f, 12f, 8f);

                _player.position =
                    _lastSafePlayerPosition;

                _hasSafePlayerPosition = true;
            }

            _autoSaveTimer =
                autoSaveInterval;
        }

        private void Update()
        {
            if (IsSafePlayerPosition(
                    _player.position))
            {
                _lastSafePlayerPosition =
                    _player.position;
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

            if (Input.GetKeyDown(
                    KeyCode.Backspace))
            {
                PlayerPrefs.DeleteKey(SaveKey);

                _inventory.Clear();
                _stats.ResetStats();

                int newSeed =
                    Guid.NewGuid().GetHashCode();

                _world.Regenerate(newSeed);

                _player.position =
                    new Vector3(8f, 12f, 8f);

                _lastSafePlayerPosition =
                    _player.position;

                _hasSafePlayerPosition = true;
                _autoSaveTimer =
                    autoSaveInterval;
            }
        }

        private void OnApplicationQuit()
        {
            DoSave();
        }

        private void OnApplicationPause(
            bool pause)
        {
            if (pause)
                DoSave();
        }

        private void RequestSave()
        {
            _saveTimer = saveDelay;
        }

        private static bool IsSafePlayerPosition(
            Vector3 position)
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

        private void DoSave()
        {
            _saveTimer = -1f;

            WorldManager.SavedChunk[] changedChunks =
                _world.GetSaveChunks();

            Vector3 positionToSave =
                _hasSafePlayerPosition
                    ? _lastSafePlayerPosition
                    : _player.position;

            var data = new SaveData
            {
                hasWorldSeed = true,
                worldSeed = _world.WorldSeed,
                changedChunks = changedChunks,
                playerPosition = positionToSave,
            };

            (data.slotTypes,
             data.slotCounts,
             data.selected) =
                _inventory.GetSaveData();

            (data.health,
             data.hunger) =
                _stats.GetSaveStats();

            string json =
                JsonUtility.ToJson(data);

            PlayerPrefs.SetString(
                SaveKey,
                json);

            PlayerPrefs.Save();

            Debug.Log(
                $"SaveSystem: сохранено. " +
                $"Изменённых чанков: {changedChunks.Length}. " +
                $"JSON: {json.Length} символов.");

            _autoSaveTimer =
                autoSaveInterval;
        }

        private void Load()
        {
            if (!PlayerPrefs.HasKey(
                    SaveKey))
                return;

            try
            {
                string json =
                    PlayerPrefs.GetString(
                        SaveKey);

                SaveData data =
                    JsonUtility.FromJson<SaveData>(
                        json);

                if (data == null)
                    return;

                if (data.hasWorldSeed)
                {
                    _world.SetSeedForLoading(
                        data.worldSeed);
                }

                if (!_inventory.IsValidSaveData(
                        data.slotTypes,
                        data.slotCounts,
                        data.selected))
                {
                    Debug.LogWarning(
                        "SaveSystem: данные инвентаря повреждены.");
                    return;
                }

                _world.LoadSaveChunks(
                    data.changedChunks);

                _player.position =
                    data.playerPosition;

                if (!_inventory.ApplySaveData(
                        data.slotTypes,
                        data.slotCounts,
                        data.selected))
                    return;

                _stats.ApplySaveStats(
                    data.health,
                    data.hunger);

                Debug.Log(
                    $"SaveSystem: загружено изменённых чанков: " +
                    $"{data.changedChunks?.Length ?? 0}");
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"SaveSystem: ошибка загрузки: {e}");
            }
        }
    }
}