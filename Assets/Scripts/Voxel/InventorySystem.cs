using System.Collections.Generic;
using UnityEngine;

namespace Voxel
{
/// <summary>
/// Инвентарь: 27 слотов основного хранилища + 9 слотов хотбара.
/// Крафт-сетка 2×2 справа от инвентаря.
/// Открытие/закрытие окна по E или Escape.
///
/// Мышь:
///  - клик по предмету — взять стак (ЛКМ) или половину (ПКМ);
///  - клик по пустому/тому же — положить (ЛКМ весь, ПКМ один);
///  - зажать и вести — равномерное распределение:
///      ЛКМ раскладывает всё поровну, ПКМ по одному предмету на ячейку;
///  - двойной клик ЛКМ — собрать предметы того же типа до 64;
///  - Shift+клик по слоту — телепорт между хотбаром и инвентарем;
///  - Shift+клик по крафт-слоту — пересыпать его в хотбар/инвентарь;
///  - Shift+клик по результату — крафт по максимуму.
/// </summary>
[DefaultExecutionOrder(100)]
public class InventorySystem : MonoBehaviour
{
    public const int MainCount = 27;
    public const int HotbarCount = 9;
    public const int SlotCount = MainCount + HotbarCount;
    public const int HotbarStart = MainCount;
    public const int MaxStack = 64;

    // Двойной клик для сбора предметов
    private const float DoubleClickWindow = 0.5f;
    private float _lastClickTime = -1f;
    private ItemType _lastClickType = ItemType.None;

    // Крафт-слоты (не сохраняются): тип + количество.
    // Размер сетки меняется: 2×2 в инвентаре, 3×3 у верстака.
    private const int MaxCraftSize = 3;
    private const int MaxCraftSlots = MaxCraftSize * MaxCraftSize;
    private int _craftSize = 2;
    private int CraftSlotCount => _craftSize * _craftSize;
    private readonly ItemType[] _craftSlots = new ItemType[MaxCraftSlots];
    private readonly int[] _craftCounts = new int[MaxCraftSlots];
    private ItemType _craftResult;
    private int _craftResultCount;

    private readonly ItemType[] _slots = new ItemType[SlotCount];
    private readonly int[] _counts = new int[SlotCount];
    private int _selected;
    private bool _isOpen;

    // Предмет «в руке» (курсор) при открытом инвентаре
    private ItemType _heldType = ItemType.None;
    private int _heldCount;

    // ---- Состояние протяжки (распределения) ----
    private bool _mousePending;        // нажата кнопка, ждём: клик или протяжка
    private bool _pendingOverResult;   // нажатие пришлось на слот результата крафта
    private bool _dragWasPickup;       // это нажатие было «взять», а не «положить»
    private bool _isDragging;          // движение мыши перевело нажатие в протяжку
    private int _pendingButton;        // 0 = ЛКМ, 1 = ПКМ
    private bool _pendingShift;
    private int _pendingInvSlot = -1;
    private int _pendingCraftSlot = -1;
    private ItemType _dragType = ItemType.None;
    private int _dragTotal;            // сколько предметов было на курсоре в начале протяжки
    private readonly List<DragCell> _dragCells = new List<DragCell>();

    /// <summary>Одна ячейка, участвующая в протяжке.</summary>
    private struct DragCell
    {
        public bool isCraft;
        public int index;
        public int originalCount; // сколько лежало ДО протяжки
    }

    // ---- Геометрия окна (общая для рисования и для попаданий мыши) ----
    // Масштаб окна уменьшен в 1.5 раза; крафт поднят над правой половиной инвентаря
    private const float SlotSize = 40f / 1.5f;
    private const float SlotGap = 4f / 1.5f;
    private const int GridCols = 9;
    private const int MainRows = 3;
    private const float WinPad = 14f / 1.5f;
    private const float SectionGap = 10f / 1.5f;
    private const float RowGap = 10f / 1.5f;
    private const float IconPad = 6f / 1.5f;

    private float GridWidth => GridCols * SlotSize + (GridCols - 1) * SlotGap;
    private float CraftGridSize => _craftSize * SlotSize + (_craftSize - 1) * SlotGap;
    private float WinWidth => GridWidth + WinPad * 2f;
    private float WinHeight => WinPad + CraftGridSize + SectionGap
        + MainRows * (SlotSize + SlotGap) + RowGap + SlotSize + WinPad;
    private float WinX => (Screen.width - WinWidth) / 2f;
    private float WinY => (Screen.height - WinHeight) / 2f;
    private float GridX => WinX + WinPad;
    private float GridY => WinY + WinPad + CraftGridSize + SectionGap;
    private float HotbarRowY => GridY + MainRows * (SlotSize + SlotGap) + RowGap;
    // Крафт над правой половиной окна, привязан к столбцам инвентаря:
    // сетка 2×2 — над столбцами 6-7, сетка 3×3 — над 5-7,
    // стрелка — над 8-м столбцом, результат — над 9-м
    private float CraftY => WinY + WinPad;
    private float CraftX => GridX + (7 - _craftSize) * (SlotSize + SlotGap);
    private float ArrowX => GridX + 7 * (SlotSize + SlotGap);
    private float ResultX => GridX + 8 * (SlotSize + SlotGap);
    private float ResultY => CraftY + CraftGridSize / 2f - SlotSize / 2f;

    private readonly Dictionary<ItemType, Texture2D> _icons = new Dictionary<ItemType, Texture2D>();
    private Texture2D _slotBg;
    private Texture2D _border;
    private Texture2D _windowBg;
    private Texture2D _arrowBg;
    private Texture2D _screenDarken;
    private GUIStyle _countStyle;
    private GUIStyle _windowCountStyle;
    private GUIStyle _nameStyle;
    private GUIStyle _nameShadowStyle;

    /// <summary>Предмет в выбранном слоте хотбара; None, если слот пуст.</summary>
    public ItemType SelectedItem
    {
        get
        {
            int idx = HotbarStart + _selected;
            return _counts[idx] > 0 ? _slots[idx] : ItemType.None;
        }
    }

    /// <summary>Открыто ли окно полного инвентаря.</summary>
    public bool IsOpen => _isOpen;

    // =====================================================================
    //  Публичное API инвентаря
    // =====================================================================

    /// <summary>Добавляет один предмет. Приоритет: неполный стак → хотбар → инвентарь.</summary>
    public bool Add(ItemType type)
    {
        if (type == ItemType.None)
            return false;

        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i] == type && _counts[i] > 0 && _counts[i] < MaxStack)
            {
                _counts[i]++;
                return true;
            }
        }
        for (int i = HotbarStart; i < SlotCount; i++)
        {
            if (_counts[i] == 0)
            {
                _slots[i] = type;
                _counts[i] = 1;
                return true;
            }
        }
        for (int i = 0; i < MainCount; i++)
        {
            if (_counts[i] == 0)
            {
                _slots[i] = type;
                _counts[i] = 1;
                return true;
            }
        }
        return false;
    }

    /// <summary>Добавляет несколько предметов. Возвращает, сколько влезло.</summary>
    public int AddMultiple(ItemType type, int count)
    {
        if (type == ItemType.None || count <= 0)
            return 0;
        int added = 0;

        for (int i = 0; i < SlotCount && count > 0; i++)
        {
            if (_slots[i] == type && _counts[i] > 0 && _counts[i] < MaxStack)
            {
                int add = Mathf.Min(MaxStack - _counts[i], count);
                _counts[i] += add;
                count -= add;
                added += add;
            }
        }
        for (int i = HotbarStart; i < SlotCount && count > 0; i++)
        {
            if (_counts[i] == 0)
            {
                int add = Mathf.Min(MaxStack, count);
                _slots[i] = type;
                _counts[i] = add;
                count -= add;
                added += add;
            }
        }
        for (int i = 0; i < MainCount && count > 0; i++)
        {
            if (_counts[i] == 0)
            {
                int add = Mathf.Min(MaxStack, count);
                _slots[i] = type;
                _counts[i] = add;
                count -= add;
                added += add;
            }
        }
        return added;
    }

    /// <summary>Сколько предметов данного типа влезет в инвентарь.</summary>
    public int CapacityFor(ItemType type)
    {
        int capacity = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i] == type && _counts[i] > 0 && _counts[i] < MaxStack)
                capacity += MaxStack - _counts[i];
            else if (_counts[i] == 0)
                capacity += MaxStack;
        }
        return capacity;
    }

    /// <summary>Тратит один предмет из выбранного слота хотбара.</summary>
    public bool TryConsumeSelected(out ItemType type)
    {
        type = SelectedItem;
        if (type == ItemType.None)
            return false;
        _counts[HotbarStart + _selected]--;
        return true;
    }

    /// <summary>Данные инвентаря для сохранения.</summary>
    public (int[] types, int[] counts, int selected) GetSaveData()
    {
        var types = new int[SlotCount];
        var counts = new int[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            types[i] = (int)_slots[i];
            counts[i] = _counts[i];
        }
        return (types, counts, _selected);
    }

    /// <summary>Восстановить инвентарь из сохранения.</summary>
    public void ApplySaveData(int[] types, int[] counts, int selected)
    {
        Clear();
        if (types == null)
            return;
        int n = Mathf.Min(types.Length, SlotCount);
        for (int i = 0; i < n; i++)
        {
            _slots[i] = (ItemType)types[i];
            _counts[i] = (counts != null && i < counts.Length) ? counts[i] : 0;
        }
        _selected = Mathf.Clamp(selected, 0, HotbarCount - 1);
    }

    /// <summary>Очистить все слоты (для новой игры).</summary>
    public void Clear()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            _slots[i] = ItemType.None;
            _counts[i] = 0;
        }
        _selected = 0;
        _heldType = ItemType.None;
        _heldCount = 0;
        ClearDragState();
        ClearCraftSlots();
    }

    // =====================================================================
    //  Update: открытие окна, хотбар
    // =====================================================================

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (_isOpen)
                SetOpen(false);
            else
                OpenInventory();
        }

        if (_isOpen && Input.GetKeyDown(KeyCode.Escape))
            SetOpen(false);

        for (int i = 0; i < HotbarCount; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                _selected = i;
        }

        // Колесо: при открытом инвентаре не работает
        if (!_isOpen)
        {
            float wheel = Input.mouseScrollDelta.y;
            if (wheel > 0f)
                _selected = (_selected + HotbarCount - 1) % HotbarCount;
            else if (wheel < 0f)
                _selected = (_selected + 1) % HotbarCount;
        }
    }

    /// <summary>Открыть или закрыть окно, управляя курсором.</summary>
    private void SetOpen(bool open)
    {
        _isOpen = open;
        if (!_isOpen)
        {
            StashHeld();
            ReturnCraftItems();
            ClearDragState();
        }
        Cursor.lockState = _isOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = _isOpen;
    }

    /// <summary>Открыть инвентарь с сеткой крафта 2×2.</summary>
    public void OpenInventory()
    {
        _craftSize = 2;
        SetOpen(true);
    }

    /// <summary>Открыть верстак с сеткой крафта 3×3.</summary>
    public void OpenWorkbench()
    {
        _craftSize = 3;
        SetOpen(true);
    }

    /// <summary>При закрытии складывает «руку» обратно в инвентарь.</summary>
    private void StashHeld()
    {
        if (_heldType == ItemType.None)
            return;
        AddMultiple(_heldType, _heldCount);
        _heldType = ItemType.None;
        _heldCount = 0;
    }

    /// <summary>Возвращает предметы из крафт-слотов в инвентарь.</summary>
    private void ReturnCraftItems()
    {
        for (int i = 0; i < CraftSlotCount; i++)
        {
            if (_craftCounts[i] > 0)
            {
                AddMultiple(_craftSlots[i], _craftCounts[i]);
                _craftSlots[i] = ItemType.None;
                _craftCounts[i] = 0;
            }
        }
        UpdateCraftResult();
    }

    /// <summary>Очистить крафт-слоты.</summary>
    private void ClearCraftSlots()
    {
        for (int i = 0; i < CraftSlotCount; i++)
        {
            _craftSlots[i] = ItemType.None;
            _craftCounts[i] = 0;
        }
        UpdateCraftResult();
    }

    private void ClearDragState()
    {
        _mousePending = false;
        _isDragging = false;
        _dragWasPickup = false;
        _pendingOverResult = false;
        _dragCells.Clear();
    }

    // =====================================================================
    //  Крафт
    // =====================================================================

    /// <summary>Создаёт сетку 2×2 из крафт-слотов с учётом количества.</summary>
    private ItemType[,] GetCraftGrid()
    {
        var grid = new ItemType[_craftSize, _craftSize];
        for (int i = 0; i < CraftSlotCount; i++)
        {
            int row = i / _craftSize;
            int col = i % _craftSize;
            grid[row, col] = _craftCounts[i] > 0 ? _craftSlots[i] : ItemType.None;
        }
        return grid;
    }

    /// <summary>Пересчитывает результат крафта на основе сетки.</summary>
    private void UpdateCraftResult()
    {
        var grid = GetCraftGrid();
        var recipe = CraftingSystem.FindRecipe(grid);
        if (recipe != null)
        {
            _craftResult = recipe.Result;
            _craftResultCount = recipe.ResultCount;
        }
        else
        {
            _craftResult = ItemType.None;
            _craftResultCount = 0;
        }
    }

    /// <summary>Клик по результату: обычный — один крафт в руку, с Shift — по максимуму.</summary>
    private void ClickCraftResult(bool rightClick, bool shiftHeld)
    {
        if (_craftResult == ItemType.None)
            return;

        if (shiftHeld)
        {
            CraftMax();
            return;
        }

        if (_heldType != ItemType.None)
        {
            if (_heldType != _craftResult)
                return;
            if (_heldCount + _craftResultCount > MaxStack)
                return;
        }

        for (int i = 0; i < CraftSlotCount; i++)
        {
            if (_craftCounts[i] > 0)
            {
                _craftCounts[i]--;
                if (_craftCounts[i] == 0)
                    _craftSlots[i] = ItemType.None;
            }
        }

        if (_heldType == ItemType.None)
        {
            _heldType = _craftResult;
            _heldCount = _craftResultCount;
        }
        else
        {
            _heldCount += _craftResultCount;
        }

        UpdateCraftResult();
    }

    /// <summary>Крафт по максимуму: результат в хотбар, потом в инвентарь.</summary>
    private void CraftMax()
    {
        var recipe = CraftingSystem.FindRecipe(GetCraftGrid());
        if (recipe == null)
            return;

        int maxCrafts = int.MaxValue;
        for (int i = 0; i < CraftSlotCount; i++)
        {
            if (_craftCounts[i] > 0)
                maxCrafts = Mathf.Min(maxCrafts, _craftCounts[i]);
        }
        if (maxCrafts == int.MaxValue || maxCrafts == 0)
            return;

        int capacity = CapacityFor(recipe.Result);
        int maxByCapacity = capacity / recipe.ResultCount;
        int finalCrafts = Mathf.Min(maxCrafts, maxByCapacity);
        if (finalCrafts <= 0)
            return;

        for (int i = 0; i < CraftSlotCount; i++)
        {
            if (_craftCounts[i] > 0)
            {
                _craftCounts[i] -= finalCrafts;
                if (_craftCounts[i] <= 0)
                {
                    _craftCounts[i] = 0;
                    _craftSlots[i] = ItemType.None;
                }
            }
        }

        AddMultiple(recipe.Result, finalCrafts * recipe.ResultCount);
        UpdateCraftResult();
    }

    // =====================================================================
    //  Телепорт между зонами
    // =====================================================================

    /// <summary>Shift+клик по слоту инвентаря: телепорт хотбар ↔ инвентарь.</summary>
    private void TransferSlot(int idx)
    {
        if (idx < 0 || idx >= SlotCount || _counts[idx] == 0)
            return;

        ItemType type = _slots[idx];
        int count = _counts[idx];

        bool inHotbar = idx >= HotbarStart;
        int targetStart = inHotbar ? 0 : HotbarStart;
        int targetEnd = inHotbar ? MainCount : SlotCount;

        for (int i = targetStart; i < targetEnd && count > 0; i++)
        {
            if (_slots[i] == type && _counts[i] > 0 && _counts[i] < MaxStack)
            {
                int add = Mathf.Min(MaxStack - _counts[i], count);
                _counts[i] += add;
                count -= add;
            }
        }
        for (int i = targetStart; i < targetEnd && count > 0; i++)
        {
            if (_counts[i] == 0)
            {
                int add = Mathf.Min(MaxStack, count);
                _slots[i] = type;
                _counts[i] = add;
                count -= add;
            }
        }

        if (count == 0)
        {
            _slots[idx] = ItemType.None;
            _counts[idx] = 0;
        }
        else
        {
            _counts[idx] = count;
        }
    }

    /// <summary>Shift+клик по крафт-слоту: пересыпать его содержимое в хотбар/инвентарь.</summary>
    private void TransferCraftToInventory(int craftIdx)
    {
        if (craftIdx < 0 || craftIdx >= CraftSlotCount)
            return;
        if (_craftCounts[craftIdx] <= 0)
            return;

        ItemType type = _craftSlots[craftIdx];
        int count = _craftCounts[craftIdx];
        int added = AddMultiple(type, count);
        _craftCounts[craftIdx] -= added;
        if (_craftCounts[craftIdx] <= 0)
        {
            _craftCounts[craftIdx] = 0;
            _craftSlots[craftIdx] = ItemType.None;
        }
        UpdateCraftResult();
    }

    // =====================================================================
    //  Операции над ячейкой (взять / положить) — инвентарь и крафт единообразно
    // =====================================================================

    private int GetCellCount(bool isCraft, int idx) => isCraft ? _craftCounts[idx] : _counts[idx];
    private ItemType GetCellType(bool isCraft, int idx) => isCraft ? _craftSlots[idx] : _slots[idx];

    /// <summary>Записывает количество и тип ячейки (тип чистится при нуле).</summary>
    private void WriteCellCount(bool isCraft, int idx, ItemType type, int count)
    {
        if (count <= 0)
        {
            if (isCraft) { _craftCounts[idx] = 0; _craftSlots[idx] = ItemType.None; }
            else { _counts[idx] = 0; _slots[idx] = ItemType.None; }
        }
        else
        {
            if (isCraft) { _craftCounts[idx] = count; _craftSlots[idx] = type; }
            else { _counts[idx] = count; _slots[idx] = type; }
        }
    }

    /// <summary>Взять предмет из ячейки: ЛКМ — весь стак, ПКМ — половину.</summary>
    private void PickupFromCell(bool isCraft, int idx, bool rightClick)
    {
        int count = GetCellCount(isCraft, idx);
        if (count <= 0)
            return;
        ItemType type = GetCellType(isCraft, idx);
        int take = rightClick ? (count + 1) / 2 : count;

        WriteCellCount(isCraft, idx, type, count - take);
        _heldType = type;
        _heldCount = take;

        // ЛКМ-взятие задаёт опорную точку для двойного клика (сбор стака)
        if (!rightClick)
        {
            _lastClickTime = Time.realtimeSinceStartup;
            _lastClickType = type;
        }
        UpdateCraftResult();
    }

    /// <summary>Положить предмет в ячейку: ЛКМ — всё/слить, ПКМ — один, swap для другого типа.</summary>
    private void PlaceOnCell(bool isCraft, int idx, bool rightClick)
    {
        if (_heldType == ItemType.None || idx < 0)
            return;

        int count = GetCellCount(isCraft, idx);
        ItemType type = GetCellType(isCraft, idx);

        if (count == 0)
        {
            if (rightClick)
            {
                WriteCellCount(isCraft, idx, _heldType, 1);
                _heldCount--;
            }
            else
            {
                WriteCellCount(isCraft, idx, _heldType, _heldCount);
                _heldCount = 0;
            }
        }
        else if (type == _heldType && count < MaxStack)
        {
            if (rightClick)
            {
                WriteCellCount(isCraft, idx, type, count + 1);
                _heldCount--;
            }
            else
            {
                int add = Mathf.Min(MaxStack - count, _heldCount);
                WriteCellCount(isCraft, idx, type, count + add);
                _heldCount -= add;
            }
        }
        else if (!rightClick)
        {
            // другой тип, ЛКМ — обмен
            WriteCellCount(isCraft, idx, _heldType, _heldCount);
            _heldType = type;
            _heldCount = count;
        }

        if (_heldCount <= 0)
        {
            _heldCount = 0;
            _heldType = ItemType.None;
        }
        UpdateCraftResult();
    }

    // =====================================================================
    //  Двойной клик — сбор стака
    // =====================================================================

    /// <summary>Если это второй быстрый клик ЛКМ — собирает такой же тип до 64.</summary>
    private bool MaybeDoubleClickCollect()
    {
        if (_heldType == ItemType.None)
            return false;
        if (_lastClickType != _heldType)
            return false;
        if (Time.realtimeSinceStartup - _lastClickTime >= DoubleClickWindow)
            return false;
        CollectSameType();
        _lastClickTime = -1f;
        return true;
    }

    /// <summary>Собирает предметы того же типа из инвентаря и крафта в руку до 64.</summary>
    private void CollectSameType()
    {
        if (_heldType == ItemType.None)
            return;
        int need = MaxStack - _heldCount;
        if (need <= 0)
            return;

        // Неполные стаки (инвентарь + крафт) по возрастанию количества
        var partials = new List<(bool isCraft, int idx, int count)>();
        for (int i = 0; i < SlotCount; i++)
            if (_slots[i] == _heldType && _counts[i] > 0 && _counts[i] < MaxStack)
                partials.Add((false, i, _counts[i]));
        for (int i = 0; i < CraftSlotCount; i++)
            if (_craftSlots[i] == _heldType && _craftCounts[i] > 0 && _craftCounts[i] < MaxStack)
                partials.Add((true, i, _craftCounts[i]));

        partials.Sort((a, b) => a.count.CompareTo(b.count));

        foreach (var (isCraft, idx, count) in partials)
        {
            if (need <= 0)
                break;
            int take = Mathf.Min(count, need);
            WriteCellCount(isCraft, idx, _heldType, count - take);
            _heldCount += take;
            need -= take;
        }

        // Полные стаки (инвентарь + крафт)
        for (int i = 0; i < SlotCount && need > 0; i++)
        {
            if (_slots[i] == _heldType && _counts[i] == MaxStack)
            {
                int take = Mathf.Min(MaxStack, need);
                WriteCellCount(false, i, _heldType, MaxStack - take);
                _heldCount += take;
                need -= take;
            }
        }
        for (int i = 0; i < CraftSlotCount && need > 0; i++)
        {
            if (_craftSlots[i] == _heldType && _craftCounts[i] == MaxStack)
            {
                int take = Mathf.Min(MaxStack, need);
                WriteCellCount(true, i, _heldType, MaxStack - take);
                _heldCount += take;
                need -= take;
            }
        }

        UpdateCraftResult();
    }

    // =====================================================================
    //  Протяжка — равномерное распределение
    // =====================================================================

    /// <summary>Годна ли ячейка для протяжки: пуста или того же типа.</summary>
    private bool IsValidDragCell(bool isCraft, int idx)
    {
        ItemType t = GetCellType(isCraft, idx);
        return t == ItemType.None || t == _dragType;
    }

    /// <summary>Добавляет ячейку в протяжку (один раз) и применяет распределение.</summary>
    private void TryAddDragCell(bool isCraft, int idx)
    {
        if (!IsValidDragCell(isCraft, idx))
            return;

        for (int i = 0; i < _dragCells.Count; i++)
            if (_dragCells[i].isCraft == isCraft && _dragCells[i].index == idx)
                return; // уже обработана — повторно не трогаем

        int original = GetCellCount(isCraft, idx);
        _dragCells.Add(new DragCell { isCraft = isCraft, index = idx, originalCount = original });

        if (_pendingButton == 0)
            ApplyUniformDistribution();
        else
            ApplySingleDistribution(original, isCraft, idx);

        UpdateCraftResult();
    }

    /// <summary>ЛКМ: раскладывает весь запас поровну, остаток — в первые ячейки.</summary>
    private void ApplyUniformDistribution()
    {
        int n = _dragCells.Count;
        if (n == 0)
        {
            _heldCount = _dragTotal;
            return;
        }

        int baseShare = _dragTotal / n;
        int rem = _dragTotal % n;
        int placed = 0;

        for (int i = 0; i < n; i++)
        {
            var cell = _dragCells[i];
            int share = baseShare + (i < rem ? 1 : 0);
            int room = MaxStack - cell.originalCount;
            int add = Mathf.Clamp(share, 0, room);
            WriteCellCount(cell.isCraft, cell.index, _dragType, cell.originalCount + add);
            placed += add;
        }

        _heldCount = _dragTotal - placed;
        if (_heldCount <= 0)
        {
            _heldCount = 0;
            _heldType = ItemType.None;
        }
    }

    /// <summary>ПКМ: кладёт по одному предмету в каждую новую ячейку.</summary>
    private void ApplySingleDistribution(int original, bool isCraft, int idx)
    {
        if (_heldCount > 0 && original < MaxStack)
        {
            WriteCellCount(isCraft, idx, _dragType, original + 1);
            _heldCount--;
            if (_heldCount <= 0)
            {
                _heldCount = 0;
                _heldType = ItemType.None;
            }
        }
    }

    // =====================================================================
    //  Обработка мыши (клик / протяжка)
    // =====================================================================

    private void HandleWindowMouseEvents(Event e)
    {
        if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 1))
            HandleMouseDown(e);
        else if (e.type == EventType.MouseDrag && _mousePending)
            HandleMouseDrag(e);
        else if (e.type == EventType.MouseUp && _mousePending)
            HandleMouseUp(e);
    }

    private void HandleMouseDown(Event e)
    {
        var mouse = e.mousePosition;
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        _mousePending = true;
        _pendingButton = e.button;
        _pendingShift = shift;
        _isDragging = false;
        _dragWasPickup = false;
        _pendingInvSlot = FindInvSlotAt(mouse);
        _pendingCraftSlot = FindCraftSlotAt(mouse);
        _pendingOverResult = IsOverCraftResult(mouse);
        _dragCells.Clear();

        // Результат крафта обрабатывается сразу, без протяжки
        if (_pendingOverResult)
        {
            ClickCraftResult(e.button == 1, shift);
            _mousePending = false;
            e.Use();
            return;
        }

        bool isCraft = _pendingCraftSlot >= 0;
        int cellIdx = isCraft ? _pendingCraftSlot : _pendingInvSlot;
        if (cellIdx < 0)
        {
            _mousePending = false; // клик по фону окна
            return;
        }

        // Shift+клик — пересылка
        if (shift)
        {
            if (isCraft)
            {
                TransferCraftToInventory(_pendingCraftSlot);
                _mousePending = false;
                e.Use();
                return;
            }
            if (_heldType == ItemType.None)
            {
                TransferSlot(_pendingInvSlot);
                _mousePending = false;
                e.Use();
                return;
            }
            // Shift + предметы на курсоре: падаем в обычный режим ниже
        }

        if (_heldType == ItemType.None)
        {
            // Курсор пуст — берём из ячейки (это отдельное действие, не протяжка)
            if (GetCellCount(isCraft, cellIdx) > 0)
            {
                PickupFromCell(isCraft, cellIdx, e.button == 1);
                _dragWasPickup = true;
            }
            _mousePending = false;
            e.Use();
            return;
        }

        // На курсоре есть предметы: это будущая протяжка или одиночный клик
        _dragType = _heldType;
        _dragTotal = _heldCount;
        e.Use();
    }

    private void HandleMouseDrag(Event e)
    {
        if (_pendingOverResult)
            return;
        // Начаться протяжка может только с предметами на курсоре, но продолжаться
        // должна и с пустым курсором: при ЛКМ-распределении весь запас улёгся
        // в ячейки сразу, и _heldType уже None, а ведение мыши надо учитывать
        if (!_isDragging && _heldType == ItemType.None)
            return;

        var mouse = e.mousePosition;
        int curInv = FindInvSlotAt(mouse);
        int curCraft = FindCraftSlotAt(mouse);

        if (!_isDragging)
        {
            bool movedToDifferent = curInv != _pendingInvSlot || curCraft != _pendingCraftSlot;
            if (!movedToDifferent)
                return; // всё ещё на стартовой ячейке — пока не протяжка
            _isDragging = true;
            _dragTotal = _heldCount;

            // Стартовая ячейка — первый участник распределения
            if (_pendingCraftSlot >= 0)
                TryAddDragCell(true, _pendingCraftSlot);
            else if (_pendingInvSlot >= 0)
                TryAddDragCell(false, _pendingInvSlot);
        }

        if (curCraft >= 0)
            TryAddDragCell(true, curCraft);
        else if (curInv >= 0)
            TryAddDragCell(false, curInv);

        e.Use();
    }

    private void HandleMouseUp(Event e)
    {
        _mousePending = false;

        if (_pendingOverResult)
        {
            e.Use();
            return;
        }

        if (_isDragging)
        {
            // Протяжка завершена — распределение уже применено
            _isDragging = false;
            _dragCells.Clear();
            e.Use();
            return;
        }

        if (_dragWasPickup)
        {
            // Это было «взять» — больше ничего не делаем
            e.Use();
            return;
        }

        bool isCraft = _pendingCraftSlot >= 0;
        int cellIdx = isCraft ? _pendingCraftSlot : _pendingInvSlot;

        if (_heldType != ItemType.None && cellIdx >= 0)
        {
            // Двойной клик ЛКМ — сбор стака (до одиночного «положить»)
            if (_pendingButton == 0 && MaybeDoubleClickCollect())
            {
                e.Use();
                return;
            }
            PlaceOnCell(isCraft, cellIdx, _pendingButton == 1);
            e.Use();
            return;
        }

        e.Use();
    }

    // =====================================================================
    //  Попадание мыши в ячейки
    // =====================================================================

    private int FindInvSlotAt(Vector2 mouse)
    {
        for (int i = 0; i < SlotCount; i++)
            if (GetInvSlotRect(i).Contains(mouse))
                return i;
        return -1;
    }

    private int FindCraftSlotAt(Vector2 mouse)
    {
        for (int i = 0; i < CraftSlotCount; i++)
            if (GetCraftSlotRect(i).Contains(mouse))
                return i;
        return -1;
    }

    private bool IsOverCraftResult(Vector2 mouse) => GetCraftResultRect().Contains(mouse);

    private Rect GetInvSlotRect(int idx)
    {
        if (idx < MainCount)
        {
            int row = idx / GridCols;
            int col = idx % GridCols;
            return new Rect(GridX + col * (SlotSize + SlotGap), GridY + row * (SlotSize + SlotGap),
                SlotSize, SlotSize);
        }
        int hcol = idx - HotbarStart;
        return new Rect(GridX + hcol * (SlotSize + SlotGap), HotbarRowY, SlotSize, SlotSize);
    }

    private Rect GetCraftSlotRect(int craftIdx)
    {
        int row = craftIdx / _craftSize;
        int col = craftIdx % _craftSize;
        return new Rect(CraftX + col * (SlotSize + SlotGap), CraftY + row * (SlotSize + SlotGap),
            SlotSize, SlotSize);
    }

    private Rect GetCraftResultRect()
    {
        return new Rect(ResultX, ResultY, SlotSize, SlotSize);
    }

    // =====================================================================
    //  Отрисовка
    // =====================================================================

    private void OnGUI()
    {
        EnsureGuiResources();
        DrawHotbar();
        if (_isOpen)
        {
            DrawScreenDarken();
            DrawInventoryWindow();
            DrawCraftingGrid();
            HandleWindowMouseEvents(Event.current);
        }
        DrawItemName(GetItemToName());
        DrawHeld();
    }

    /// <summary>Небольшое потемнение экрана под окном инвентаря.</summary>
    private void DrawScreenDarken()
    {
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _screenDarken);
    }

    /// <summary>Нижний хотбар — только отображение.</summary>
    private void DrawHotbar()
    {
        const float slot = 48f;
        const float gap = 4f;
        float totalWidth = HotbarCount * slot + (HotbarCount - 1) * gap;
        float x0 = (Screen.width - totalWidth) / 2f;
        float y0 = Screen.height - slot - 12f;

        for (int i = 0; i < HotbarCount; i++)
        {
            int idx = HotbarStart + i;
            float x = x0 + i * (slot + gap);
            var rect = new Rect(x, y0, slot, slot);
            GUI.DrawTexture(rect, _slotBg);
            if (i == _selected)
            {
                const float b = 2f;
                var outer = new Rect(x - b, y0 - b, slot + b * 2, slot + b * 2);
                GUI.DrawTexture(new Rect(outer.x, outer.y, outer.width, b), _border);
                GUI.DrawTexture(new Rect(outer.x, outer.yMax - b, outer.width, b), _border);
                GUI.DrawTexture(new Rect(outer.x, outer.y, b, outer.height), _border);
                GUI.DrawTexture(new Rect(outer.xMax - b, outer.y, b, outer.height), _border);
            }
            if (_counts[idx] > 0)
            {
                float icon = slot - 16f;
                GUI.DrawTexture(new Rect(x + 8f, y0 + 8f, icon, icon), GetIcon(_slots[idx]));
                if (_counts[idx] > 1)
                    GUI.Label(rect, _counts[idx].ToString(), _countStyle);
            }
        }
    }

    /// <summary>Окно: 3 ряда инвентаря + хотбар снизу.</summary>
    private void DrawInventoryWindow()
    {
        GUI.DrawTexture(new Rect(WinX, WinY, WinWidth, WinHeight), _windowBg);

        for (int i = 0; i < SlotCount; i++)
            DrawInvSlot(i);
    }

    private void DrawInvSlot(int idx)
    {
        var rect = GetInvSlotRect(idx);
        GUI.DrawTexture(rect, _slotBg);
        if (_counts[idx] > 0)
        {
            float icon = SlotSize - IconPad * 2f;
            GUI.DrawTexture(new Rect(rect.x + IconPad, rect.y + IconPad, icon, icon), GetIcon(_slots[idx]));
            if (_counts[idx] > 1)
                GUI.Label(CountRect(rect), _counts[idx].ToString(), _windowCountStyle);
        }
    }

    /// <summary>Крафт-сетка 2×2 и результат справа от инвентаря.</summary>
    private void DrawCraftingGrid()
    {
        for (int i = 0; i < CraftSlotCount; i++)
            DrawCraftSlot(i);

        // Стрелка — простой прямоугольник-тире по центру 8-го столбца
        float dashWidth = SlotSize * 0.7f;
        float dashHeight = SlotSize * 0.2f;
        float dashX = ArrowX + (SlotSize - dashWidth) / 2f;
        float dashY = CraftY + CraftGridSize / 2f - dashHeight / 2f;
        GUI.DrawTexture(new Rect(dashX, dashY, dashWidth, dashHeight), _arrowBg);

        DrawCraftResult();
    }

    private void DrawCraftSlot(int craftIdx)
    {
        var rect = GetCraftSlotRect(craftIdx);
        GUI.DrawTexture(rect, _slotBg);
        if (_craftCounts[craftIdx] > 0)
        {
            float icon = SlotSize - IconPad * 2f;
            GUI.DrawTexture(new Rect(rect.x + IconPad, rect.y + IconPad, icon, icon), GetIcon(_craftSlots[craftIdx]));
            if (_craftCounts[craftIdx] > 1)
                GUI.Label(CountRect(rect), _craftCounts[craftIdx].ToString(), _windowCountStyle);
        }
    }

    private void DrawCraftResult()
    {
        var rect = GetCraftResultRect();
        GUI.DrawTexture(rect, _slotBg);
        if (_craftResult != ItemType.None)
        {
            float icon = SlotSize - IconPad * 2f;
            GUI.DrawTexture(new Rect(rect.x + IconPad, rect.y + IconPad, icon, icon), GetIcon(_craftResult));
            if (_craftResultCount > 1)
                GUI.Label(CountRect(rect), _craftResultCount.ToString(), _windowCountStyle);
        }
    }

    /// <summary>Какой предмет подписать: наведение → предмет в руке → выбранный в хотбаре.</summary>
    private ItemType GetItemToName()
    {
        if (_isOpen)
        {
            ItemType hover = GetHoverItemType();
            if (hover != ItemType.None)
                return hover;
            if (_heldType != ItemType.None)
                return _heldType;
        }
        return SelectedItem;
    }

    /// <summary>Предмет под курсором мыши в окне (инвентарь, крафт-слоты, результат).</summary>
    private ItemType GetHoverItemType()
    {
        Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        int invSlot = FindInvSlotAt(mouse);
        if (invSlot >= 0 && _counts[invSlot] > 0)
            return _slots[invSlot];
        int craftSlot = FindCraftSlotAt(mouse);
        if (craftSlot >= 0 && _craftCounts[craftSlot] > 0)
            return _craftSlots[craftSlot];
        if (IsOverCraftResult(mouse) && _craftResult != ItemType.None)
            return _craftResult;
        return ItemType.None;
    }

    /// <summary>Название предмета над сердечками и голодом, с обводкой для читаемости.</summary>
    private void DrawItemName(ItemType type)
    {
        if (type == ItemType.None)
            return;
        string name = ItemUtils.Name(type);
        if (string.IsNullOrEmpty(name))
            return;
        var rect = new Rect(0f, Screen.height - 110f + _nameStyle.fontSize / 2f, Screen.width, 24f);
        var shadow = new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height);
        GUI.Label(shadow, name, _nameShadowStyle);
        GUI.Label(rect, name, _nameStyle);
    }

    /// <summary>Предмет «в руке» у курсора вместе с числом.</summary>
    private void DrawHeld()
    {
        if (_heldType == ItemType.None || _heldCount <= 0 || !_isOpen)
            return;
        float size = SlotSize;
        var mouse = Event.current.mousePosition;
        var iconRect = new Rect(mouse.x - size / 2f, mouse.y - size / 2f, size, size);
        GUI.DrawTexture(iconRect, GetIcon(_heldType));
        if (_heldCount > 1)
            GUI.Label(CountRect(iconRect), _heldCount.ToString(), _windowCountStyle);
    }

    /// <summary>Создаёт текстуры и стиль при первом рисовании.</summary>
    private void EnsureGuiResources()
    {
        if (_slotBg != null)
            return;
        _slotBg = SolidTexture(new Color(0f, 0f, 0f, 0.45f));
        _border = SolidTexture(Color.white);
        _windowBg = SolidTexture(new Color(0.78f, 0.78f, 0.78f, 0.95f));
        _arrowBg = SolidTexture(new Color(0.5f, 0.5f, 0.5f));
        _screenDarken = SolidTexture(new Color(0f, 0f, 0f, 0.35f));
        // Хотбар — как было, до уменьшения окна
        _countStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.LowerRight,
        };
        _countStyle.normal.textColor = Color.white;
        // Числа внутри окна инвентаря — мельче
        _windowCountStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.LowerRight,
        };
        _windowCountStyle.normal.textColor = Color.white;
        _nameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
        };
        _nameStyle.normal.textColor = Color.white;
        _nameShadowStyle = new GUIStyle(_nameStyle);
        _nameShadowStyle.normal.textColor = Color.black;
    }

    /// <summary>
    /// Прямоугольник числа в слоте. Число поднято на 3/4 высоты шрифта
    /// относительно прежнего положения (было опущено на полную высоту шрифта).
    /// </summary>
    private Rect CountRect(Rect slotRect)
    {
        return new Rect(slotRect.x, slotRect.y + _windowCountStyle.fontSize * 0.25f,
            slotRect.width, slotRect.height);
    }

    /// <summary>Сплошная текстура 2x2 заданного цвета.</summary>
    private static Texture2D SolidTexture(Color color)
    {
        var tex = new Texture2D(2, 2);
        tex.SetPixels(new[] { color, color, color, color });
        tex.Apply();
        return tex;
    }

    /// <summary>Иконка предмета — квадрат его цвета.</summary>
    private Texture2D GetIcon(ItemType type)
    {
        if (!_icons.TryGetValue(type, out Texture2D icon))
        {
            icon = SolidTexture(VoxelColors.ForItem(type));
            _icons[type] = icon;
        }
        return icon;
    }
}
}