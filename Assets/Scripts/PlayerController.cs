using UnityEngine;
using Voxel;

/// <summary>
/// Кинематический воксельный контроллер: AABB-коллизии напрямую по сетке блоков.
/// Без Rigidbody/PhysX: нет сноса на углах и нет зацепления капсулы за рёбра блоков.
/// Движение по осям даёт скольжение вдоль стен, прыжок работает детерминированно.
/// </summary>
[DefaultExecutionOrder(200)]
public class PlayerController : MonoBehaviour
{
    [Header("Движение")]
    [SerializeField] private float moveSpeed = 4.3f;      // целевая скорость, м/с
    [SerializeField] private float groundAccel = 60f;     // разгон/выруливание при зажатых клавишах
    [SerializeField] private float groundDecel = 12f;     // плавное торможение без ввода на земле
    [SerializeField] private float airAccel = 2.5f;       // выруливание в воздухе
    [SerializeField] private float airDecel = 2.5f;       // гашение скорости в воздухе без ввода
    [SerializeField] private float gravity = 32f;         // гравитация, м/с²
    [SerializeField] private float jumpSpeed = 8.4f;      // стартовая скорость прыжка, м/с (MC: 0.42 блока/тик)
    [SerializeField] private float verticalDrag = 0.404f; // сопротивление воздуха по вертикали (MC: ×0.98 за тик 20 Гц)
    [SerializeField] private float maxFallSpeed = 40f;    // ограничение скорости падения
    [SerializeField] private float sprintMultiplier = 1.3f;   // множитель скорости на спринте
    [SerializeField] private float fovKickPercent = 15f;      // расширение FOV на спринте, %
    [SerializeField] private float sneakHeight = 1.5f;        // высота хитбокса при приседании, м
    [SerializeField] private float sneakSpeedDrop = 1.29f;    // насколько медленнее при приседании, м/с
    [SerializeField] private float sneakCameraDrop = 0.2f;    // дополнительное опускание камеры в приседе, м
    [Header("Полёт")]
    [SerializeField] private float flySpeed = 18f;            // скорость полёта
    [SerializeField] private float flyFastMultiplier = 3f;    // ускорение при CapsLock


    [Header("Мышь")]
    [SerializeField] private Transform playerCamera;      // дочерняя камера игрока
    [SerializeField] private float mouseSensitivity = 2f; // чувствительность мыши
    [SerializeField] private float maxPitch = 89f;        // предел взгляда вверх/вниз

    [Header("Размеры игрока")]
    [SerializeField] private float playerWidth = 0.6f;    // ширина/глубина AABB
    [SerializeField] private float playerHeight = 1.8f;   // высота AABB

    // Небольшой зазор, чтобы не считать касание граней коллизией
    private const float ContactEpsilon = 0.001f;

    // Максимальный шаг симуляции, чтобы не было туннелирования при низком FPS
    private const float MaxTickDelta = 1f / 60f;
    private const float MaxFrameDelta = 0.1f;
    private const float MoveEpsilon = 1e-7f;

    private WorldManager _world;

    private Vector3 _halfExtents;
    private Vector3 _position;
    private Vector3 _lastPosition;
    private Vector3 _velocity;

    private float _yaw;
    private float _pitch;

    private bool _grounded;
    private bool _jumpArmed = true;
    private bool _sprinting;
    private bool _sneaking;
    private bool _flying;

    private Camera _cam;
    private float _baseFov;
    private float _baseCamY;
    private InventorySystem _inventory;

    /// <summary>Стоит ли игрок на земле — единственный источник правды для других систем.</summary>
    public bool IsGrounded => _grounded;

    /// <summary>Бежит ли игрок — единственный источник правды для других систем.</summary>
    public bool IsSprinting => _sprinting;

    /// <summary>AABB игрока в мировых координатах — для проверки «не ставить блок в себя».</summary>
    public Bounds BodyBounds => new Bounds(_position, new Vector3(playerWidth, _halfExtents.y * 2f, playerWidth));

    /// <summary>Инициализация: мир, размеры, курсор, отключение старой физики.</summary>
    private void Start()
    {
        _world = FindObjectOfType<WorldManager>();
        if (_world == null)
        {
            Debug.LogError("PlayerController: WorldManager не найден");
            enabled = false;
            return;
        }

        if (playerCamera == null)
        {
            Debug.LogError("PlayerController: не назначена Player Camera");
            enabled = false;
            return;
        }

        _halfExtents = new Vector3(playerWidth * 0.5f, playerHeight * 0.5f, playerWidth * 0.5f);

        _position = transform.position;
        DepenetrateIfNeeded();
        transform.position = _position;
        _lastPosition = _position;

        // В первом лице своё тело не видно
        foreach (Transform child in transform)
        {
            if (child.TryGetComponent<MeshRenderer>(out MeshRenderer r))
                r.enabled = false;
        }

        _cam = playerCamera.GetComponent<Camera>();
        if (_cam != null)
            _baseFov = _cam.fieldOfView;
        _baseCamY = playerCamera.localPosition.y;

        _inventory = FindObjectOfType<InventorySystem>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>Кадр: обзор, синхронизация позиции, симуляция малыми шагами.</summary>
    private void Update()
    {
        bool uiOpen = _inventory != null && _inventory.IsOpen;
        if (!uiOpen)
            UpdateLook();
        SyncPosition();
        UpdateFlightToggle();

        if (_flying)
        {
            TickFlying(Mathf.Min(Time.deltaTime, MaxFrameDelta));
            transform.position = _position;
            _lastPosition = _position;
            UpdateFov();
            UpdateSneakCamera();
            return;
        }

        float remaining = Mathf.Min(Time.deltaTime, MaxFrameDelta);
        while (remaining > MoveEpsilon)
        {
            float dt = Mathf.Min(remaining, MaxTickDelta);
            Tick(dt);
            remaining -= dt;
        }

        transform.position = _position;
        _lastPosition = _position;

        UpdateFov();
        UpdateSneakCamera();
    }

    /// <summary>Включает или выключает свободный полёт для быстрого осмотра генерации мира.</summary>
    private void UpdateFlightToggle()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            _flying = !_flying;
            _velocity = Vector3.zero;
            _grounded = false;
            _jumpArmed = true;
            _sneaking = false;

            Debug.Log(
                $"PlayerController: свободный полёт {(_flying ? "включён" : "выключен")}");
        }
    }

    /// <summary>Свободный полёт без гравитации и коллизий.</summary>
    private void TickFlying(float dt)
    {
        float horizontal = Input.GetKey(KeyCode.D) ? 1f :
            Input.GetKey(KeyCode.A) ? -1f : 0f;

        float forward = Input.GetKey(KeyCode.W) ? 1f :
            Input.GetKey(KeyCode.S) ? -1f : 0f;

        float vertical = Input.GetKey(KeyCode.Space) ? 1f :
            (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? -1f : 0f;

        Vector3 direction =
            transform.forward * forward +
            transform.right * horizontal +
            Vector3.up * vertical;

        if (direction.sqrMagnitude > 1f)
            direction.Normalize();

        float speed = flySpeed;

        if (Input.GetKey(KeyCode.CapsLock))
            speed *= flyFastMultiplier;

        _position += direction * speed * dt;
        _velocity = Vector3.zero;
        _grounded = false;
    }

    private void UpdateLook()
    {
        _yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        _pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        _pitch = Mathf.Clamp(_pitch, -maxPitch, maxPitch);

        transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        playerCamera.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    /// <summary>
    /// Если позицию игрока изменили извне (сейв, респаун, Backspace),
    /// принимаем её и сбрасываем скорость.
    /// </summary>
    private void SyncPosition()
    {
        Vector3 current = transform.position;

        if ((current - _lastPosition).sqrMagnitude > 1e-8f)
        {
            _position = current;
            _velocity = Vector3.zero;
            _grounded = false;
            _jumpArmed = true;
            DepenetrateIfNeeded();
        }
        else
        {
            _position = current;
        }
    }

    /// <summary>Один шаг симуляции: скорость, прыжок, перемещение по осям.</summary>
    private void Tick(float dt)
    {
        UpdateStance();
        UpdateVelocity(dt);
        TryJump();

        // Вертикаль интегрируем точно (независимо от частоты кадров), горизонталь — Эйлером
        float e = Mathf.Exp(-verticalDrag * dt);
        float gOverK = gravity / verticalDrag;
        float dy = (_velocity.y + gOverK) * (1f - e) / verticalDrag - gOverK * dt;
        Vector3 delta = new Vector3(_velocity.x * dt, dy, _velocity.z * dt);

        // Присед: не даём сойти с опоры. Оси проверяем раздельно, чтобы
        // зажатое «в пустоту» не съедало скольжение вдоль грани:
        // отменяется только та ось, которая теряет опору
        if (_sneaking && _grounded && (delta.x != 0f || delta.z != 0f))
        {
            Vector3 proposed = new Vector3(_position.x + delta.x, _position.y, _position.z + delta.z);
            if (!HasSupportBelow(proposed))
            {
                if (delta.x != 0f && !HasSupportBelow(new Vector3(_position.x + delta.x, _position.y, _position.z)))
                {
                    delta.x = 0f;
                    _velocity.x = 0f;
                }

                if (delta.z != 0f && !HasSupportBelow(new Vector3(_position.x, _position.y, _position.z + delta.z)))
                {
                    delta.z = 0f;
                    _velocity.z = 0f;
                }

                // Страховка: если остаток после фильтра всё ещё ведёт за опору
                // (Г-образные углы) — останавливаем обе оси
                if ((delta.x != 0f || delta.z != 0f)
                    && !HasSupportBelow(new Vector3(_position.x + delta.x, _position.y, _position.z + delta.z)))
                {
                    delta.x = 0f;
                    delta.z = 0f;
                    _velocity.x = 0f;
                    _velocity.z = 0f;
                }
            }
        }

        // Горизонтальные оси двигаем отдельно: это даёт скольжение вдоль стен
        if (MoveAxis(0, delta.x))
            _velocity.x = 0f;

        if (MoveAxis(2, delta.z))
            _velocity.z = 0f;

        // Вертикаль последняя: так проще понимать, приземлились мы или нет
        bool blockedY = MoveAxis(1, delta.y);
        float vyBeforeBlock = _velocity.y;
        if (blockedY)
            _velocity.y = 0f;

        // Земля: либо упёрлись при ПАДЕНИИ, либо стоим в снап-допуске от грани.
        // Знак скорости берём ДО обнуления: иначе удар о потолок (vy > 0)
        // читается как приземление, и зажатый пробел «приклеивает» к потолку
        // автопрыжками. Второе условие держит _grounded стабильным на любом FPS.
        if (vyBeforeBlock <= 0f && (blockedY || HasGroundContact()))
        {
            _grounded = true;
            _velocity.y = 0f;
            SnapFeetToBlockTop();
        }
        else
        {
            _grounded = false;
        }

        if (_grounded && Mathf.Abs(_velocity.y) < 0.01f)
            _jumpArmed = true;

        if (!_grounded)
            _jumpArmed = false;

        UpdateVerticalVelocity(dt);
    }

    /// <summary>
    /// Вертикаль как в Minecraft: гравитация плюс сопротивление воздуха.
    /// Применяется ПОСЛЕ перемещения (порядок тика оригинала): сначала позиция
    /// сдвигается текущей скоростью, потом скорость гасится. Тогда дуга «плавучая»:
    /// высота ~1.25 блока и ~0.6 с в воздухе.
    /// </summary>
    private void UpdateVerticalVelocity(float dt)
    {
        // Точное решение dv/dt = -g - k*v на шаге dt: поведение одинаково на любом FPS
        float k = Mathf.Max(0.001f, verticalDrag);
        float e = Mathf.Exp(-k * dt);
        float gOverK = gravity / k;
        _velocity.y = (_velocity.y + gOverK) * e - gOverK;
        if (_velocity.y < -maxFallSpeed)
            _velocity.y = -maxFallSpeed;
    }

    /// <summary>
    /// При приземлении ставит ноги ровно на целую координату верхней грани блока.
    /// Без снапа бинарный поиск оставляет ноги в микрометре от грани, и пересчёт
    /// позиции при смене стойки из-за округления float может опустить их ниже:
    /// тогда земля попадает в каждый запрос коллизий и движение блокируется.
    /// </summary>
    private void SnapFeetToBlockTop()
    {
        float feet = _position.y - _halfExtents.y;
        float nearest = Mathf.Round(feet);
        if (Mathf.Abs(feet - nearest) < 0.05f)
            _position.y += nearest - feet;
    }

    /// <summary>
    /// Есть ли верхняя грань блока в пределах 5 см под ногами.
    /// Держит «на земле» стабильно, даже когда шаг за тик меньше контактного зазора.
    /// </summary>
    private bool HasGroundContact()
    {
        float feet = _position.y - _halfExtents.y;
        int belowY = Mathf.FloorToInt(feet - 0.05f);
        if (Mathf.Abs(feet - (belowY + 1)) > 0.05f)
            return false;

        int minX = Mathf.FloorToInt(_position.x - _halfExtents.x + ContactEpsilon);
        int maxX = Mathf.FloorToInt(_position.x + _halfExtents.x - ContactEpsilon);
        int minZ = Mathf.FloorToInt(_position.z - _halfExtents.z + ContactEpsilon);
        int maxZ = Mathf.FloorToInt(_position.z + _halfExtents.z - ContactEpsilon);

        for (int z = minZ; z <= maxZ; z++)
        for (int x = minX; x <= maxX; x++)
        {
            if (IsSolid(new Vector3Int(x, belowY, z)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Приседание: Shift опускает хитбокс до sneakHeight (ноги остаются на месте,
    /// камера тоже ниже — она дочерняя). Встать обратно даём, только если более
    /// высокий хитбокс помещается — иначе сидим, пока сверху тесно.
    /// </summary>
    private void UpdateStance()
    {
        bool uiOpen = _inventory != null && _inventory.IsOpen;
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        // При открытом инвентаре: присесть нельзя; если уже присели — встаём
        bool wantSneak = uiOpen ? false : shiftHeld;
        if (wantSneak == _sneaking)
            return;

        float newHalf = (wantSneak ? sneakHeight : playerHeight) * 0.5f;
        Vector3 newPos = _position;
        newPos.y = _position.y - _halfExtents.y + newHalf;

        // Встаём только если высокий хитбокс помещается
        if (!wantSneak && CollidesAt(newPos))
            return;

        _position = newPos;
        _halfExtents.y = newHalf;
        _sneaking = wantSneak;
    }

    /// <summary>Есть ли под ногами хотя бы одна клетка опоры (футпринт с отступом 0.05).</summary>
    private bool HasSupportBelow(Vector3 pos)
    {
        const float margin = 0.05f;
        int belowY = Mathf.FloorToInt(pos.y - _halfExtents.y - 0.05f);
        int minX = Mathf.FloorToInt(pos.x - _halfExtents.x + margin);
        int maxX = Mathf.FloorToInt(pos.x + _halfExtents.x - margin);
        int minZ = Mathf.FloorToInt(pos.z - _halfExtents.z + margin);
        int maxZ = Mathf.FloorToInt(pos.z + _halfExtents.z - margin);

        for (int z = minZ; z <= maxZ; z++)
        for (int x = minX; x <= maxX; x++)
        {
            if (IsSolid(new Vector3Int(x, belowY, z)))
                return true;
        }
        return false;
    }

    /// <summary>Целевая горизонтальная скорость и гравитация.</summary>
    private void UpdateVelocity(float dt)
    {
        bool uiOpen = _inventory != null && _inventory.IsOpen;
        float horizontal = uiOpen ? 0f : (Input.GetKey(KeyCode.D) ? 1f : Input.GetKey(KeyCode.A) ? -1f : 0f);
        float vertical = uiOpen ? 0f : (Input.GetKey(KeyCode.W) ? 1f : Input.GetKey(KeyCode.S) ? -1f : 0f);
        // Спринт: только CapsLock + вперёд; приседание (Shift) отменяет спринт
        _sprinting = !uiOpen && Input.GetKey(KeyCode.CapsLock) && vertical > 0f && !_sneaking;

        Vector3 target = transform.forward * vertical + transform.right * horizontal;
        if (target.sqrMagnitude > 1f)
            target.Normalize();

        float speed = moveSpeed * (_sprinting ? sprintMultiplier : 1f);
        if (_sneaking)
            speed = Mathf.Max(0.5f, speed - sneakSpeedDrop);
        target *= speed;

        Vector3 current = new Vector3(_velocity.x, 0f, _velocity.z);
        bool hasInput = horizontal != 0f || vertical != 0f;
        float accel;
        if (_grounded)
            accel = hasInput ? groundAccel : groundDecel;
        else
            accel = hasInput ? airAccel : airDecel;
        Vector3 stepped = Vector3.MoveTowards(current, target, accel * dt);

        _velocity.x = stepped.x;
        _velocity.z = stepped.z;
    }

    /// <summary>Прыжок: один раз за касание земли, зажатый пробел даёт автопрыжок.</summary>
    private void TryJump()
    {
        if (_inventory != null && _inventory.IsOpen)
            return;
        if (!_grounded || !_jumpArmed || !Input.GetKey(KeyCode.Space))
            return;

        _velocity.y = jumpSpeed;
        _jumpArmed = false;
        _grounded = false;
    }

    /// <summary>Плавно расширяет FOV на спринте и возвращает при остановке.</summary>
    private void UpdateFov()
    {
        if (_cam == null)
            return;

        float target = _baseFov * (1f + (_sprinting ? fovKickPercent : 0f) / 100f);
        // Экспоненциальное приближение, независимое от частоты кадров
        float t = 1f - Mathf.Exp(-10f * Time.deltaTime);
        _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, target, t);
    }

    /// <summary>Плавно опускает камеру при приседании и поднимает обратно.</summary>
    private void UpdateSneakCamera()
    {
        float target = _baseCamY - (_sneaking ? sneakCameraDrop : 0f);
        float t = 1f - Mathf.Exp(-10f * Time.deltaTime);
        Vector3 lp = playerCamera.localPosition;
        lp.y = Mathf.Lerp(lp.y, target, t);
        playerCamera.localPosition = lp;
    }

    /// <summary>
    /// Перемещение по одной оси с бинарным поиском максимально безопасной позиции.
    /// Возвращает true, если движение было упёрто в блок.
    /// </summary>
    private bool MoveAxis(int axis, float amount)
    {
        if (Mathf.Abs(amount) < MoveEpsilon)
            return false;

        Vector3 target = _position;
        target[axis] += amount;

        if (!CollidesAt(target))
        {
            _position = target;
            return false;
        }

        float safe = 0f;
        float far = amount;

        for (int i = 0; i < 10; i++)
        {
            float mid = (safe + far) * 0.5f;

            Vector3 test = _position;
            test[axis] += mid;

            if (CollidesAt(test))
                far = mid;
            else
                safe = mid;
        }

        Vector3 resolved = _position;
        resolved[axis] += safe;
        _position = resolved;

        return true;
    }

    /// <summary>Проверяет, пересекается ли AABB игрока с твёрдыми блоками.</summary>
    private bool CollidesAt(Vector3 pos)
    {
        int minX = Mathf.FloorToInt(pos.x - _halfExtents.x + ContactEpsilon);
        int maxX = Mathf.FloorToInt(pos.x + _halfExtents.x - ContactEpsilon);

        int minY = Mathf.FloorToInt(pos.y - _halfExtents.y + ContactEpsilon);
        int maxY = Mathf.FloorToInt(pos.y + _halfExtents.y - ContactEpsilon);

        int minZ = Mathf.FloorToInt(pos.z - _halfExtents.z + ContactEpsilon);
        int maxZ = Mathf.FloorToInt(pos.z + _halfExtents.z - ContactEpsilon);

        for (int y = minY; y <= maxY; y++)
        for (int z = minZ; z <= maxZ; z++)
        for (int x = minX; x <= maxX; x++)
        {
            if (IsSolid(new Vector3Int(x, y, z)))
                return true;
        }

        return false;
    }

    /// <summary>Твёрдость блока: воздух не твёрдый, всё остальное твёрдое.</summary>
    private bool IsSolid(Vector3Int p)
    {
        return _world.GetBlock(p) != BlockType.Air;
    }


    /// <summary>
    /// Если игрок оказался внутри блоков (например, после телепорта),
    /// выталкиваем его вверх до первой свободной позиции.
    /// </summary>
    private void DepenetrateIfNeeded()
    {
        if (!CollidesAt(_position))
            return;

        for (int i = 1; i <= ChunkData.SizeY + 2; i++)
        {
            Vector3 test = _position + Vector3.up * i;
            if (!CollidesAt(test))
            {
                _position = test;
                _velocity = Vector3.zero;
                return;
            }
        }
    }
}