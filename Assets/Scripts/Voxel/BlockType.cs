namespace Voxel
{
    /// <summary>Идентификаторы блоков. 0 — всегда воздух.</summary>
    public enum BlockType : byte
    {
        Air = 0,
        Grass = 1,
        Dirt = 2,
        Stone = 3,
        Wood = 4,
        Leaves = 5,
        Planks = 6,
        CraftingTable = 7,
        Cobblestone = 8,
        Sand = 9,
    }

    /// <summary>Тип инструмента.</summary>
    public enum ToolType : byte
    {
        None = 0,
        Pickaxe,
        Axe,
        Shovel,
        Sword,
    }

    /// <summary>Уровень инструмента. Рука = 0.</summary>
    public enum ToolTier : byte
    {
        Hand = 0,
        Wood = 1,
        Stone = 2,
    }

    /// <summary>
    /// Единый источник правды о параметрах блоков:
    /// базовая твёрдость, требуемый тип инструмента и минимальный уровень
    /// для получения дропа. Механика ломания читает только отсюда.
    /// </summary>
    public static class BlockDefs
    {
        /// <summary>
        /// Штраф скорости, если блок требует инструмент,
        /// а в руке неподходящий предмет или голые руки.
        /// Камень рукой будет ломаться мучительно долго.
        /// </summary>
        public const float WrongToolPenalty = 0.15f;

        /// <summary>
        /// Базовое время ломания в секундах при подходящем инструменте
        /// подходящего уровня (или рукой, если инструмент не обязателен).
        /// </summary>
        public static float Hardness(BlockType type)
        {
            switch (type)
            {
                case BlockType.Grass:
                case BlockType.Dirt:
                    return 0.6f;

                case BlockType.Stone:
                    return 1.5f;

                case BlockType.Cobblestone:
                    return 2.0f;

                case BlockType.Wood:
                case BlockType.Planks:
                case BlockType.CraftingTable:
                    return 2.0f;

                case BlockType.Leaves:
                    return 0.2f;

                default:
                    return 1.0f;
            }
        }

        /// <summary>
        /// Какой тип инструмента эффективен для блока.
        /// None — ломается рукой без штрафа, инструмент не влияет.
        /// </summary>
        public static ToolType RequiredTool(BlockType type)
        {
            switch (type)
            {
                case BlockType.Grass:
                case BlockType.Dirt:
                    return ToolType.Shovel;

                case BlockType.Stone:
                case BlockType.Cobblestone:
                    return ToolType.Pickaxe;

                case BlockType.Wood:
                case BlockType.Planks:
                case BlockType.CraftingTable:
                    return ToolType.Axe;

                default:
                    return ToolType.None;
            }
        }

        /// <summary>
        /// Минимальный уровень инструмента, при котором блок даёт дроп.
        /// Если уровень ниже — блок ломается, но ничего не выпадает.
        /// </summary>
        public static ToolTier MinTier(BlockType type)
        {
            switch (type)
            {
                case BlockType.Stone:
                case BlockType.Cobblestone:
                    return ToolTier.Wood;

                default:
                    return ToolTier.Hand;
            }
        }
    }
}