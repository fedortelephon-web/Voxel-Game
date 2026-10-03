using UnityEngine;

namespace Voxel
{
    /// <summary>Тайлы атласа.</summary>
    public enum TileId
    {
        GrassTop, GrassSide, Dirt, Stone, Cobblestone,
        LogSide, LogTop, Leaves, Planks,
        CraftingTop, CraftingSide,
        Count,
    }

    /// <summary>
    /// Процедурный пиксельный атлас: тайлы 16×16, собирается кодом,
    /// детерминированно. Единый источник правды для текстур мира.
    /// Если позже появятся рисованные PNG — заменяется только Build(),
    /// мешер и инвентарь не меняются.
    /// </summary>
    public static class VoxelTextures
    {
        public const int TileSize = 16;
        private const int AtlasCols = 8;

        private static Texture2D _atlas;
        private static Rect[] _uv;

        /// <summary>Текстура атласа (ленивая сборка при первом обращении).</summary>
        public static Texture2D Atlas
        {
            get
            {
                if (_atlas == null)
                    Build();
                return _atlas;
            }
        }

        /// <summary>
        /// UV-прямоугольник тайла с отступом в пол-текселя:
        /// защита от «вытекания» соседних тайлов на границах.
        /// </summary>
        public static Rect UVRect(TileId tile)
        {
            if (_atlas == null)
                Build();
            return _uv[(int)tile];
        }

        /// <summary>
        /// Тайл для грани блока. Порядок граней как в ChunkMesher:
        /// 0:+X 1:-X 2:верх 3:низ 4:+Z 5:-Z.
        /// </summary>
        public static TileId TileForBlock(BlockType block, int face)
        {
            switch (block)
            {
                case BlockType.Grass:
                    if (face == 2) return TileId.GrassTop;
                    if (face == 3) return TileId.Dirt;
                    return TileId.GrassSide;
                case BlockType.Dirt: return TileId.Dirt;
                case BlockType.Stone: return TileId.Stone;
                case BlockType.Cobblestone: return TileId.Cobblestone;
                case BlockType.Wood:
                    return face == 2 || face == 3 ? TileId.LogTop : TileId.LogSide;
                case BlockType.Leaves: return TileId.Leaves;
                case BlockType.Planks: return TileId.Planks;
                case BlockType.CraftingTable:
                    return face == 2 ? TileId.CraftingTop : TileId.CraftingSide;
                default: return TileId.Stone;
            }
        }

        // =================================================================
        //  Сборка атласа
        // =================================================================

        private static void Build()
        {
            int count = (int)TileId.Count;
            int rows = (count + AtlasCols - 1) / AtlasCols;
            int w = AtlasCols * TileSize;
            int h = rows * TileSize;
            var pixels = new Color[w * h];

            for (int i = 0; i < count; i++)
            {
                int col = i % AtlasCols;
                int row = i / AtlasCols;
                // Свой детерминированный сид на тайл — картинка стабильна
                var rng = new System.Random(9000 + i * 137);
                DrawTile(pixels, w, col * TileSize, row * TileSize, (TileId)i, rng);
            }

            _atlas = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _atlas.filterMode = FilterMode.Point;   // чёткие пиксели
            _atlas.wrapMode = TextureWrapMode.Clamp;
            _atlas.SetPixels(pixels);
            _atlas.Apply();

            // UV-прямоугольники с отступом в пол-текселя от краёв тайла
            float texelX = 1f / w;
            float texelY = 1f / h;
            _uv = new Rect[count];
            for (int i = 0; i < count; i++)
            {
                int col = i % AtlasCols;
                int row = i / AtlasCols;
                _uv[i] = new Rect(
                    (col * TileSize + 0.5f) * texelX,
                    (row * TileSize + 0.5f) * texelY,
                    (TileSize - 1) * texelX,
                    (TileSize - 1) * texelY);
            }
        }

        private static void DrawTile(Color[] px, int stride, int ox, int oy, TileId tile, System.Random rng)
        {
            switch (tile)
            {
                case TileId.GrassTop: Fill(px, stride, ox, oy, rng, Greens); break;
                case TileId.GrassSide: GrassSide(px, stride, ox, oy, rng); break;
                case TileId.Dirt: Fill(px, stride, ox, oy, rng, Browns); break;
                case TileId.Stone: Fill(px, stride, ox, oy, rng, Grays); break;
                case TileId.Cobblestone: Cobble(px, stride, ox, oy, rng); break;
                case TileId.LogSide: LogSide(px, stride, ox, oy, rng); break;
                case TileId.LogTop: LogTop(px, stride, ox, oy, rng); break;
                case TileId.Leaves: Fill(px, stride, ox, oy, rng, LeafGreens); break;
                case TileId.Planks: Planks(px, stride, ox, oy, rng); break;
                case TileId.CraftingTop: CraftingTop(px, stride, ox, oy, rng); break;
                case TileId.CraftingSide: CraftingSide(px, stride, ox, oy, rng); break;
            }
        }

        // =================================================================
        //  Генераторы тайлов
        // =================================================================

        /// <summary>Случайный выбор цвета из палитры на каждый пиксель.</summary>
        private static void Fill(Color[] px, int stride, int ox, int oy, System.Random rng, Color[] palette)
        {
            for (int ly = 0; ly < TileSize; ly++)
            for (int lx = 0; lx < TileSize; lx++)
                px[(oy + ly) * stride + ox + lx] = palette[rng.Next(palette.Length)];
        }

        /// <summary>Земля с рваной полосой травы сверху.</summary>
        private static void GrassSide(Color[] px, int stride, int ox, int oy, System.Random rng)
        {
            Fill(px, stride, ox, oy, rng, Browns);

            // Верхние 3 ряда — трава
            for (int ly = TileSize - 3; ly < TileSize; ly++)
            for (int lx = 0; lx < TileSize; lx++)
                px[(oy + ly) * stride + ox + lx] = Greens[rng.Next(Greens.Length)];

            // Рваный нижний край травы
            int edgeY = TileSize - 4;
            for (int lx = 0; lx < TileSize; lx++)
                if (rng.NextDouble() < 0.5)
                    px[(oy + edgeY) * stride + ox + lx] = Greens[rng.Next(Greens.Length)];
        }

        /// <summary>Булыжник: ячейки Вороного с тёмными швами.</summary>
        private static void Cobble(Color[] px, int stride, int ox, int oy, System.Random rng)
        {
            const int seedCount = 7;
            var pts = new Vector2[seedCount];
            var shade = new float[seedCount];
            for (int i = 0; i < seedCount; i++)
            {
                pts[i] = new Vector2((float)rng.NextDouble() * TileSize, (float)rng.NextDouble() * TileSize);
                shade[i] = 0.42f + (float)rng.NextDouble() * 0.2f;
            }

            for (int ly = 0; ly < TileSize; ly++)
            for (int lx = 0; lx < TileSize; lx++)
            {
                var p = new Vector2(lx + 0.5f, ly + 0.5f);
                int best = 0;
                float d1 = float.MaxValue, d2 = float.MaxValue;
                for (int i = 0; i < seedCount; i++)
                {
                    float d = (p - pts[i]).sqrMagnitude;
                    if (d < d1) { d2 = d1; d1 = d; best = i; }
                    else if (d < d2) d2 = d;
                }

                // Тёмный шов там, где расстояния до двух центров близки
                float edge = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);
                float s = edge < 1.1f
                    ? 0.28f
                    : shade[best] + ((float)rng.NextDouble() - 0.5f) * 0.04f;
                px[(oy + ly) * stride + ox + lx] = new Color(s, s, s * 1.02f);
            }
        }

        /// <summary>Кора: вертикальные полосы разной темноты.</summary>
        private static void LogSide(Color[] px, int stride, int ox, int oy, System.Random rng)
        {
            var colShade = new float[TileSize];
            for (int lx = 0; lx < TileSize; lx++)
                colShade[lx] = rng.NextDouble() < 0.3
                    ? 0.6f
                    : 0.9f + (float)rng.NextDouble() * 0.15f;

            for (int ly = 0; ly < TileSize; ly++)
            for (int lx = 0; lx < TileSize; lx++)
            {
                float s = colShade[lx] + ((float)rng.NextDouble() - 0.5f) * 0.08f;
                px[(oy + ly) * stride + ox + lx] = new Color(0.45f * s, 0.33f * s, 0.17f * s);
            }
        }

        /// <summary>Срез пня: концентрические кольца.</summary>
        private static void LogTop(Color[] px, int stride, int ox, int oy, System.Random rng)
        {
            var center = new Vector2(8f, 8f);
            for (int ly = 0; ly < TileSize; ly++)
            for (int lx = 0; lx < TileSize; lx++)
            {
                float d = Vector2.Distance(new Vector2(lx + 0.5f, ly + 0.5f), center);
                Color c;
                if (d > 7.2f)
                {
                    c = new Color(0.30f, 0.21f, 0.11f); // кора по краю
                }
                else
                {
                    float s = ((int)d % 2 == 0) ? 1f : 0.8f;
                    s += ((float)rng.NextDouble() - 0.5f) * 0.06f;
                    c = new Color(0.60f * s, 0.45f * s, 0.25f * s);
                }
                px[(oy + ly) * stride + ox + lx] = c;
            }
        }

        /// <summary>Доски: горизонтальные плашки с тёмными швами.</summary>
        private static void Planks(Color[] px, int stride, int ox, int oy, System.Random rng)
        {
            // Вертикальный шов у каждой из четырёх плашек
            int[] seams = { 3, 11, 7, 14 };
            var seamColor = new Color(0.45f, 0.33f, 0.19f);

            for (int ly = 0; ly < TileSize; ly++)
            {
                int plank = ly / 4;
                bool seamRow = ly % 4 == 0;
                for (int lx = 0; lx < TileSize; lx++)
                {
                    Color c;
                    if (seamRow || lx == seams[plank])
                    {
                        c = seamColor;
                    }
                    else
                    {
                        float jitter = 0.95f + (float)rng.NextDouble() * 0.1f;
                        c = new Color(0.72f * jitter, 0.55f * jitter, 0.33f * jitter);
                    }
                    px[(oy + ly) * stride + ox + lx] = c;
                }
            }
        }

        /// <summary>Верх верстака: доски с рамкой и крестом сетки 2×2.</summary>
        private static void CraftingTop(Color[] px, int stride, int ox, int oy, System.Random rng)
        {
            Planks(px, stride, ox, oy, rng);
            var dark = new Color(0.40f, 0.28f, 0.15f);
            for (int i = 0; i < TileSize; i++)
            {
                // Рамка по краю
                px[(oy + 0) * stride + ox + i] = dark;
                px[(oy + TileSize - 1) * stride + ox + i] = dark;
                px[(oy + i) * stride + ox + 0] = dark;
                px[(oy + i) * stride + ox + TileSize - 1] = dark;
                // Крест сетки 2×2
                px[(oy + i) * stride + ox + 7] = dark;
                px[(oy + i) * stride + ox + 8] = dark;
                px[(oy + 7) * stride + ox + i] = dark;
                px[(oy + 8) * stride + ox + i] = dark;
            }
        }

        /// <summary>Бок верстака: доски с тёмной верхней полосой и «инструментами».</summary>
        private static void CraftingSide(Color[] px, int stride, int ox, int oy, System.Random rng)
        {
            Planks(px, stride, ox, oy, rng);

            var band = new Color(0.50f, 0.35f, 0.18f);
            for (int ly = TileSize - 4; ly < TileSize; ly++)
            for (int lx = 0; lx < TileSize; lx++)
                px[(oy + ly) * stride + ox + lx] = band;

            var tool = new Color(0.35f, 0.24f, 0.13f);
            for (int ly = 4; ly <= 9; ly++)
            for (int lx = 3; lx <= 5; lx++)
                px[(oy + ly) * stride + ox + lx] = tool;
            for (int ly = 4; ly <= 9; ly++)
            for (int lx = 10; lx <= 12; lx++)
                px[(oy + ly) * stride + ox + lx] = tool;
        }

        // =================================================================
        //  Палитры
        // =================================================================

        private static readonly Color[] Greens =
        {
            new Color(0.36f, 0.62f, 0.24f), new Color(0.32f, 0.57f, 0.22f),
            new Color(0.40f, 0.66f, 0.27f), new Color(0.30f, 0.52f, 0.20f),
        };

        private static readonly Color[] Browns =
        {
            new Color(0.52f, 0.38f, 0.24f), new Color(0.47f, 0.34f, 0.21f),
            new Color(0.43f, 0.31f, 0.19f), new Color(0.56f, 0.41f, 0.26f),
        };

        private static readonly Color[] Grays =
        {
            new Color(0.56f, 0.56f, 0.58f), new Color(0.51f, 0.51f, 0.53f),
            new Color(0.61f, 0.61f, 0.63f), new Color(0.47f, 0.47f, 0.49f),
        };

        private static readonly Color[] LeafGreens =
        {
            new Color(0.16f, 0.42f, 0.14f), new Color(0.22f, 0.50f, 0.17f),
            new Color(0.12f, 0.34f, 0.11f), new Color(0.26f, 0.55f, 0.20f),
        };
    }
}