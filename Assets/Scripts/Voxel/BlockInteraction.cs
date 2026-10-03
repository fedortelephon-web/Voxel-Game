using UnityEngine;

namespace Voxel
{
    /// <summary>
    /// Ломание и установка блоков по лучу из камеры,
    /// подсветка выбранного блока, оверлей трещин и временный прицел.
    /// </summary>
    public class BlockInteraction : MonoBehaviour
    {
        [Header("Настройки")]
        [SerializeField] private Transform playerCamera;         // камера для пуска луча
        [SerializeField] private float maxDistance = 5f;         // дальность взаимодействия

        private InventorySystem _inventory;
        private WorldManager _worldManager;
        private PlayerController _controller;
        private GameObject _highlight;
        private GameObject _crackOverlay;
        private Material _crackMaterial;
        private Texture2D[] _crackTextures;

        // Состояние долгого ломания
        private Vector3Int? _breakTarget;
        private float _breakProgress;

        /// <summary>Ищем WorldManager, проверяем камеру, создаём подсветку.</summary>
        private void Start()
        {
            _worldManager = FindObjectOfType<WorldManager>();
            if (_worldManager == null)
            {
                Debug.LogError("WorldManager не найден в сцене");
                enabled = false;
                return;
            }
            if (playerCamera == null)
            {
                Debug.LogError("BlockInteraction: не назначена Player Camera");
                enabled = false;
            }
            _inventory = FindObjectOfType<InventorySystem>();
            if (_inventory == null)
            {
                Debug.LogError("InventorySystem не найден в сцене");
                enabled = false;
            }
            _controller = FindObjectOfType<PlayerController>();
            if (_controller == null)
            {
                Debug.LogError("PlayerController не найден в сцене");
                enabled = false;
            }

            _crackTextures = CreateCrackTextures();

            Shader crackShader = Shader.Find("Custom/VoxelCracks");
            if (crackShader == null)
            {
                Debug.LogError("BlockInteraction: шейдер Custom/VoxelCracks не найден");
            }
            else
            {
                _crackMaterial = new Material(crackShader);
            }

            CreateHighlight();
        }

        /// <summary>ЛКМ — ломать (удержание), ПКМ — поставить/взаимодействовать.</summary>
        private void Update()
        {
            // При открытом инвентаре не ломаем и не ставим
            if (_inventory != null && _inventory.IsOpen)
            {
                _breakTarget = null;
                _breakProgress = 0f;
                return;
            }

            UpdateBreaking();

            if (Input.GetMouseButtonDown(1))
                TryInteractOrPlace();
        }

        /// <summary>Двигаем подсветку и оверлей трещин после всех обновлений кадра.</summary>
        private void LateUpdate()
        {
            UpdateHighlight();
            UpdateCrackOverlay();
        }

        /// <summary>Временный прицел: две белые полоски в центре экрана.</summary>
        private void OnGUI()
        {
            // При открытом инвентаре прицел не нужен
            if (_inventory != null && _inventory.IsOpen)
                return;

            const float halfLength = 8f;
            const float thickness = 2f;
            float cx = Screen.width / 2f;
            float cy = Screen.height / 2f;

            GUI.DrawTexture(new Rect(cx - halfLength, cy - thickness / 2f, halfLength * 2f, thickness),
                Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - thickness / 2f, cy - halfLength, thickness, halfLength * 2f),
                Texture2D.whiteTexture);
        }

        /// <summary>Накапливает прогресс ломания, пока ЛКМ удерживается на одном блоке.</summary>
        private void UpdateBreaking()
        {
            if (!Input.GetMouseButton(0))
            {
                _breakTarget = null;
                _breakProgress = 0f;
                return;
            }

            VoxelHit? hit = RaycastVoxel();
            if (!hit.HasValue)
            {
                _breakTarget = null;
                _breakProgress = 0f;
                return;
            }

            Vector3Int target = hit.Value.blockPos;
            BlockType block = _worldManager.GetBlock(target);
            if (block == BlockType.Air)
            {
                _breakTarget = null;
                _breakProgress = 0f;
                return;
            }

            // Если прицел перешёл на другой блок — прогресс сбрасывается
            if (_breakTarget != target)
            {
                _breakTarget = target;
                _breakProgress = 0f;
            }

            float breakTime = CalculateBreakTime(block, _inventory.SelectedItem);
            if (breakTime <= 0f)
            {
                _breakTarget = null;
                _breakProgress = 0f;
                return;
            }

            _breakProgress += Time.deltaTime / breakTime;

            if (_breakProgress >= 1f)
            {
                BreakBlock(target, block);
                _breakTarget = null;
                _breakProgress = 0f;
            }
        }

        /// <summary>
        /// Время ломания блока с учётом инструмента в руке.
        /// Неправильный инструмент для твёрдых блоков даёт штраф.
        /// </summary>
        private float CalculateBreakTime(BlockType block, ItemType heldItem)
        {
            float hardness = BlockDefs.Hardness(block);
            ToolType required = BlockDefs.RequiredTool(block);
            ToolTier minTier = BlockDefs.MinTier(block);
            ToolType handTool = ItemUtils.Tool(heldItem);

            float speedMult = 1f;

            if (required != ToolType.None)
            {
                if (handTool == required)
                {
                    speedMult = ItemUtils.MiningSpeed(heldItem);
                }
                else if (minTier > ToolTier.Hand)
                {
                    // Блок требует инструмент (камень): без него мучительно медленно
                    speedMult = BlockDefs.WrongToolPenalty;
                }
                // Иначе (земля рукой): скорость 1, инструмент не обязателен
            }

            return hardness / speedMult;
        }

        /// <summary>Можно ли получить дроп с блока текущим инструментом.</summary>
        private bool CanHarvest(BlockType block, ItemType heldItem)
        {
            ToolTier minTier = BlockDefs.MinTier(block);
            if (minTier == ToolTier.Hand)
                return true;

            ToolType required = BlockDefs.RequiredTool(block);
            ToolType handTool = ItemUtils.Tool(heldItem);
            ToolTier handTier = ItemUtils.Tier(heldItem);
            return handTool == required && handTier >= minTier;
        }

        /// <summary>Ломает блок и кладёт дроп в инвентарь, если инструмент подходит.</summary>
        private void BreakBlock(Vector3Int pos, BlockType block)
        {
            bool canHarvest = CanHarvest(block, _inventory.SelectedItem);
            _worldManager.SetBlock(pos, BlockType.Air);

            if (canHarvest)
            {
                ItemType drop = ItemUtils.DropFrom(block, Random.value);
                if (drop != ItemType.None)
                    _inventory.Add(drop);
            }
        }

        /// <summary>ПКМ: если под прицелом верстак — открыть его, иначе поставить блок.</summary>
        private void TryInteractOrPlace()
        {
            VoxelHit? hit = RaycastVoxel();
            if (!hit.HasValue)
                return;

            if (_worldManager.GetBlock(hit.Value.blockPos) == BlockType.CraftingTable)
            {
                _inventory.OpenWorkbench();
                return;
            }

            TryPlaceBlock(hit.Value);
        }

        /// <summary>Ставит выбранный в хотбаре блок, тратя его из инвентаря.</summary>
        private void TryPlaceBlock(VoxelHit hit)
        {
            Vector3Int placePos = hit.blockPos + hit.normal;

            // Нельзя ставить блок в занятую клетку и внутрь игрока
            if (_worldManager.GetBlock(placePos) != BlockType.Air)
                return;

            // За пределы мира ставить нельзя — и предмет не тратим
            if (!_worldManager.InBounds(placePos))
                return;

            // Блок не должен пересекать AABB игрока.
            // AABB сжат на 2 см с каждой стороны: Intersects считает касание граней
            // пересечением, а ноги стоят ровно на верхней грани блока — без зазора
            // нельзя было поставить опору под вторую ногу.
            Bounds body = _controller.BodyBounds;
            Bounds shrunken = new Bounds(body.center, body.size - Vector3.one * 0.04f);
            Bounds cellBounds = new Bounds(placePos + new Vector3(0.5f, 0.5f, 0.5f), Vector3.one);
            if (shrunken.Intersects(cellBounds))
                return;

            // Неблоки (яблоко, инструменты) не ставятся
            if (ItemUtils.ToBlock(_inventory.SelectedItem) == BlockType.Air)
                return;

            if (!_inventory.TryConsumeSelected(out ItemType item))
                return;

            _worldManager.SetBlock(placePos, ItemUtils.ToBlock(item));
        }

        /// <summary>Создаёт куб-подсветку и оверлей трещин.</summary>
        private void CreateHighlight()
        {
            _highlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _highlight.name = "BlockHighlight";
            Destroy(_highlight.GetComponent<Collider>());
            MeshRenderer renderer = _highlight.GetComponent<MeshRenderer>();
            renderer.material = new Material(Shader.Find("Custom/VoxelDarken"));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _crackOverlay = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _crackOverlay.name = "BlockCrackOverlay";
            Destroy(_crackOverlay.GetComponent<Collider>());
            MeshRenderer crackRenderer = _crackOverlay.GetComponent<MeshRenderer>();
            if (_crackMaterial != null)
            {
                crackRenderer.material = _crackMaterial;
                crackRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            else
            {
                _crackOverlay.SetActive(false);
            }
        }

        /// <summary>Ставит куб-окантовку на блок под прицелом или прячет его.</summary>
        private void UpdateHighlight()
        {
            VoxelHit? hit = RaycastVoxel();
            if (!hit.HasValue)
            {
                _highlight.SetActive(false);
                return;
            }

            _highlight.SetActive(true);
            _highlight.transform.position = hit.Value.blockPos + new Vector3(0.5f, 0.5f, 0.5f);
            _highlight.transform.rotation = Quaternion.identity;
            _highlight.transform.localScale = Vector3.one * 1.005f;
        }

        /// <summary>Отображает трещины на блоке, который сейчас ломается.</summary>
        private void UpdateCrackOverlay()
        {
            if (_crackOverlay == null)
                return;

            if (_breakTarget.HasValue && _breakProgress > 0f)
            {
                _crackOverlay.SetActive(true);
                _crackOverlay.transform.position = _breakTarget.Value + new Vector3(0.5f, 0.5f, 0.5f);
                _crackOverlay.transform.rotation = Quaternion.identity;
                _crackOverlay.transform.localScale = Vector3.one * 1.006f;

                int stage = Mathf.FloorToInt(_breakProgress * _crackTextures.Length);
                stage = Mathf.Clamp(stage, 0, _crackTextures.Length - 1);
                if (_crackMaterial != null)
                    _crackMaterial.mainTexture = _crackTextures[stage];
            }
            else
            {
                _crackOverlay.SetActive(false);
            }
        }

        /// <summary>Генерирует текстуры трещин для стадий ломания.</summary>
        private static Texture2D[] CreateCrackTextures(int count = 5)
        {
            var textures = new Texture2D[count];
            var rng = new System.Random(42); // фиксированный сид для стабильности

            for (int i = 0; i < count; i++)
            {
                var tex = new Texture2D(16, 16, TextureFormat.ARGB32, false);
                tex.filterMode = FilterMode.Point;

                var pixels = new Color[16 * 16];
                for (int p = 0; p < pixels.Length; p++)
                    pixels[p] = Color.clear;

                // Каждая стадия добавляет одну новую трещину
                int crackCount = i + 1;
                for (int c = 0; c < crackCount; c++)
                {
                    int x = rng.Next(16);
                    int y = rng.Next(16);
                    int dx = rng.Next(2) * 2 - 1; // -1 или 1
                    int dy = rng.Next(2) * 2 - 1;
                    int len = 3 + rng.Next(4);    // 3..6 пикселей

                    for (int s = 0; s < len; s++)
                    {
                        if (x >= 0 && x < 16 && y >= 0 && y < 16)
                            pixels[y * 16 + x] = Color.black;

                        x += dx;
                        y += dy;

                        // Иногда меняем направление для извилистости
                        if (rng.Next(3) == 0)
                        {
                            dx = rng.Next(2) * 2 - 1;
                            dy = rng.Next(2) * 2 - 1;
                        }
                    }
                }

                tex.SetPixels(pixels);
                tex.Apply();
                textures[i] = tex;
            }

            return textures;
        }

        /// <summary>
        /// Воксельный raycast (DDA): шагаем по лучу от камеры до первого непустого блока.
        /// Возвращает позицию блока и нормаль грани входа.
        /// </summary>
        private VoxelHit? RaycastVoxel()
        {
            Ray ray = new Ray(playerCamera.position, playerCamera.forward);
            Vector3 pos = ray.origin;
            Vector3 dir = ray.direction;

            Vector3Int current = new Vector3Int(
                Mathf.FloorToInt(pos.x),
                Mathf.FloorToInt(pos.y),
                Mathf.FloorToInt(pos.z));

            Vector3Int step = new Vector3Int(
                dir.x > 0 ? 1 : -1,
                dir.y > 0 ? 1 : -1,
                dir.z > 0 ? 1 : -1);

            float tMaxX = dir.x != 0 ? ((current.x + (dir.x > 0 ? 1 : 0)) - pos.x) / dir.x : float.PositiveInfinity;
            float tMaxY = dir.y != 0 ? ((current.y + (dir.y > 0 ? 1 : 0)) - pos.y) / dir.y : float.PositiveInfinity;
            float tMaxZ = dir.z != 0 ? ((current.z + (dir.z > 0 ? 1 : 0)) - pos.z) / dir.z : float.PositiveInfinity;

            float tDeltaX = dir.x != 0 ? Mathf.Abs(1f / dir.x) : float.PositiveInfinity;
            float tDeltaY = dir.y != 0 ? Mathf.Abs(1f / dir.y) : float.PositiveInfinity;
            float tDeltaZ = dir.z != 0 ? Mathf.Abs(1f / dir.z) : float.PositiveInfinity;

            Vector3Int normal = Vector3Int.zero;
            float t = 0f;

            while (t < maxDistance)
            {
                if (_worldManager.GetBlock(current) != BlockType.Air)
                    return new VoxelHit { blockPos = current, normal = normal };

                if (tMaxX < tMaxY && tMaxX < tMaxZ)
                {
                    current.x += step.x;
                    t = tMaxX;
                    tMaxX += tDeltaX;
                    normal = new Vector3Int(-step.x, 0, 0);
                }
                else if (tMaxY < tMaxZ)
                {
                    current.y += step.y;
                    t = tMaxY;
                    tMaxY += tDeltaY;
                    normal = new Vector3Int(0, -step.y, 0);
                }
                else
                {
                    current.z += step.z;
                    t = tMaxZ;
                    tMaxZ += tDeltaZ;
                    normal = new Vector3Int(0, 0, -step.z);
                }
            }

            return null;
        }

        /// <summary>Результат попадания луча в воксель.</summary>
        private struct VoxelHit
        {
            public Vector3Int blockPos;
            public Vector3Int normal;
        }
    }
}