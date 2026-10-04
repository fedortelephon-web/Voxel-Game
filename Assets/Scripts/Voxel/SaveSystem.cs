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
        private const string SaveKey = "voxel_save_v14";

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
            Debug.Log("========== SaveSystem START ==========");

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
                Debug.LogWarning(
                    "SaveSystem: НАЖАТ BACKSPACE — сохранение удаляется!");

                PlayerPrefs.DeleteKey(SaveKey);

                _inventory.Clear();
                _stats.ResetStats();

                int newSeed = Guid.NewGuid().GetHashCode();

                Debug.Log(
                    $"SaveSystem: новый случайный seed мира = {newSeed}");

                _world.Regenerate(newSeed);

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
    Debug.Log(
        $"SaveSystem: LOAD start. " +
        $"SaveKey = {SaveKey}, " +
        $"HasKey = {PlayerPrefs.HasKey(SaveKey)}");

    if (!PlayerPrefs.HasKey(SaveKey))
    {
        Debug.LogWarning(
            "SaveSystem: сохранение НЕ найдено в PlayerPrefs.");
        return;
    }

    try
    {
        var json = PlayerPrefs.GetString(SaveKey);

        Debug.Log(
            $"SaveSystem: сейв найден. " +
            $"Длина JSON: {json.Length}");

        var data = JsonUtility.FromJson<SaveData>(json);

        if (data == null)
        {
            Debug.LogWarning("SaveSystem: FromJson вернул null.");
            return;
        }

        if (string.IsNullOrEmpty(data.blocksBase64))
        {
            Debug.LogWarning(
                "SaveSystem: blocksBase64 отсутствует или пуст.");
            return;
        }

        Debug.Log(
            $"SaveSystem: JSON разобран. " +
            $"blocksBase64 длина: {data.blocksBase64.Length}");

        byte[] blockBytes = Convert.FromBase64String(data.blocksBase64);

        Debug.Log(
            $"SaveSystem: Base64 декодирован. " +
            $"Байт мира: {blockBytes.Length}");

        int expectedBlockBytes =
            ChunkData.SizeX *
            ChunkData.SizeY *
            ChunkData.SizeZ *
            _world.ChunkCount;

        if (blockBytes.Length != expectedBlockBytes)
        {
            Debug.LogWarning(
                $"SaveSystem: неверный размер данных мира. " +
                $"Получено: {blockBytes.Length}, " +
                $"ожидалось: {expectedBlockBytes}");
            return;
        }

        Debug.Log("SaveSystem: размер мира корректный.");

        bool inventoryValid = _inventory.IsValidSaveData(
            data.slotTypes,
            data.slotCounts,
            data.selected);

        Debug.Log(
            $"SaveSystem: проверка инвентаря: {inventoryValid}");

        if (!inventoryValid)
        {
            Debug.LogWarning(
                "SaveSystem: данные инвентаря повреждены.");
            return;
        }

        Debug.Log(
            "SaveSystem: вызываю WorldManager.SetBlocksBytes().");

        if (!_world.SetBlocksBytes(blockBytes))
        {
            Debug.LogError(
                $"SaveSystem: не удалось загрузить данные мира. " +
                $"Получено байт: {blockBytes.Length}.");
            return;
        }

        Debug.Log(
            $"SaveSystem: загружена позиция игрока " +
            $"{data.playerPosition}");

        _player.position = data.playerPosition;

        if (!_inventory.ApplySaveData(
            data.slotTypes,
            data.slotCounts,
            data.selected))
        {
            Debug.LogWarning(
                "SaveSystem: не удалось загрузить инвентарь.");
            return;
        }

        _stats.ApplySaveStats(
            data.health,
            data.hunger);

        Debug.Log(
            "SaveSystem: LOAD успешно завершён.");
    }
    catch (Exception e)
    {
        Debug.LogError(
            $"SaveSystem: ошибка загрузки сохранения: {e}");
    }
}
    }
}