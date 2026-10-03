using UnityEngine;

namespace Voxel
{
/// <summary>
/// Выживание: здоровье, голод, урон от падения, еда, смерть и респаун.
/// 20 HP = 10 сердечек, 20 голода = 10 «куриных ножек».
/// Землю спрашивает у PlayerController — единственного источника правды.
/// </summary>
public class SurvivalStats : MonoBehaviour
{
    public const float MaxHealth = 20f;
    public const float MaxHunger = 20f;

    [Header("Голод")]
    [SerializeField] private float hungerDrainPerSec = 0.1f;  // голода в секунду на спринте
    [SerializeField] private float walkDrainDivisor = 3f;     // во сколько раз медленнее трата без спринта
    [SerializeField] private float starveDamageInterval = 2f; // урон при нулевом голоде
    [SerializeField] private float regenInterval = 1.5f;      // реген при голоде >= 17

    [Header("Падение")]
    [SerializeField] private float safeFallDistance = 3f; // безопасная высота

    private InventorySystem _inventory;
    private PlayerController _controller;

    private float _health = MaxHealth;
    private float _hunger = MaxHunger;
    private float? _fallStartY;
    private float _starveTimer;
    private float _regenTimer;

    private Texture2D _heartFull, _heartEmpty, _foodFull, _foodEmpty;

    /// <summary>Статы для сохранения.</summary>
    public (float health, float hunger) GetSaveStats()
    {
        return (_health, _hunger);
    }

    /// <summary>Восстановить статы из сохранения.</summary>
    public void ApplySaveStats(float health, float hunger)
    {
        _health = Mathf.Clamp(health, 1f, MaxHealth);
        _hunger = Mathf.Clamp(hunger, 0f, MaxHunger);
    }

    /// <summary>Полный сброс статов для новой игры.</summary>
    public void ResetStats()
    {
        _health = MaxHealth;
        _hunger = MaxHunger;
        _fallStartY = null;
        _starveTimer = 0f;
        _regenTimer = 0f;
    }

    private void Start()
    {
        _inventory = FindObjectOfType<InventorySystem>();
        _controller = FindObjectOfType<PlayerController>();

        if (_controller == null)
        {
            Debug.LogError("SurvivalStats: PlayerController не найден");
            enabled = false;
        }
    }

    private void Update()
    {
        UpdateHunger();
        UpdateFall();
        UpdateEating();
    }

    /// <summary>Голод падает; при нуле — урон, при высоком — реген здоровья.</summary>
    private void UpdateHunger()
    {
        float drain = _controller.IsSprinting
            ? hungerDrainPerSec
            : hungerDrainPerSec / walkDrainDivisor;
        _hunger = Mathf.Max(0f, _hunger - drain * Time.deltaTime);

        if (_hunger <= 0f)
        {
            _starveTimer += Time.deltaTime;
            if (_starveTimer >= starveDamageInterval)
            {
                _starveTimer = 0f;
                ApplyDamage(1f);
            }
        }
        else if (_hunger >= 17f && _health < MaxHealth)
        {
            _regenTimer += Time.deltaTime;
            if (_regenTimer >= regenInterval)
            {
                _regenTimer = 0f;
                _health = Mathf.Min(MaxHealth, _health + 1f);
            }
        }
    }

    /// <summary>Следит за падением и даёт урон при приземлении.</summary>
    private void UpdateFall()
    {
        bool grounded = _controller.IsGrounded;

        if (!grounded)
        {
            // Начало и верхняя точка падения
            if (_fallStartY == null)
                _fallStartY = transform.position.y;
            _fallStartY = Mathf.Max(_fallStartY.Value, transform.position.y);
            return;
        }

        if (_fallStartY != null)
        {
            float drop = _fallStartY.Value - transform.position.y;
            _fallStartY = null;
            if (drop > safeFallDistance)
                ApplyDamage(Mathf.RoundToInt(drop - safeFallDistance));
        }
    }

    /// <summary>ПКМ с выбранным яблоком — съесть его (+5 голода).</summary>
    private void UpdateEating()
    {
        // При открытом инвентаре не едим
        if (_inventory != null && _inventory.IsOpen)
            return;
        if (!Input.GetMouseButtonDown(1))
            return;
        if (_inventory.SelectedItem != ItemType.Apple)
            return;
        if (_hunger >= MaxHunger)
            return;
        if (!_inventory.TryConsumeSelected(out _))
            return;

        _hunger = Mathf.Min(MaxHunger, _hunger + 5f);
    }

    /// <summary>Наносит урон и обрабатывает смерть.</summary>
    private void ApplyDamage(float damage)
    {
        _health -= damage;
        if (_health <= 0f)
            Die();
    }

    /// <summary>Смерть: сброс статов и возврат на спавн. Инвентарь сохраняем.</summary>
    private void Die()
    {
        _health = MaxHealth;
        _hunger = MaxHunger;
        _fallStartY = null;
        transform.position = new Vector3(8f, 12f, 8f);
    }

    /// <summary>Рисует сердечки и голод над хотбаром.</summary>
    private void OnGUI()
    {
        EnsureIcons();

        const float icon = 16f;
        const float gap = 2f;
        const float hotbarWidth = 9 * 48f + 8 * 4f;
        float rowWidth = 10 * icon + 9 * gap;
        float x0 = (Screen.width - hotbarWidth) / 2f;
        float y = Screen.height - 48f - 12f - icon - 6f;

        for (int i = 0; i < 10; i++)
        {
            // Сердечки слева: пустеют справа налево
            GUI.DrawTexture(new Rect(x0 + i * (icon + gap), y, icon, icon),
                _health >= (i + 1) * 2f ? _heartFull : _heartEmpty);

            // Голод справа: пустеет слева направо (зеркально)
            float xR = x0 + hotbarWidth - rowWidth + i * (icon + gap);
            GUI.DrawTexture(new Rect(xR, y, icon, icon),
                _hunger > (9 - i) * 2f ? _foodFull : _foodEmpty);
        }
    }

    /// <summary>Создаёт иконки при первом рисовании.</summary>
    private void EnsureIcons()
    {
        if (_heartFull != null)
            return;

        _heartFull = Solid(new Color(0.9f, 0.1f, 0.1f));
        _heartEmpty = Solid(new Color(0.25f, 0.05f, 0.05f));
        _foodFull = Solid(new Color(0.8f, 0.5f, 0.15f));
        _foodEmpty = Solid(new Color(0.25f, 0.15f, 0.05f));
    }

    /// <summary>Сплошная текстура 2x2 заданного цвета.</summary>
    private static Texture2D Solid(Color color)
    {
        var tex = new Texture2D(2, 2);
        tex.SetPixels(new[] { color, color, color, color });
        tex.Apply();
        return tex;
    }
}
}