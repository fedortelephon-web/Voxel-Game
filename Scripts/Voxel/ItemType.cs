namespace Voxel
{
    /// <summary>Предметы инвентаря: блоки и неблоки.</summary>
    public enum ItemType : byte
    {
        None = 0,
        Grass,
        Dirt,
        Stone,
        Wood,
        Leaves,
        Apple,
        Planks,
        Stick,
        CraftingTable,
        Cobblestone,
        WoodenPickaxe,
        StonePickaxe,
        WoodenAxe,
        StoneAxe,
        WoodenShovel,
        StoneShovel,
        WoodenSword,
        StoneSword,
    }

    /// <summary>Преобразования между блоками и предметами.</summary>
    public static class ItemUtils
    {
        /// <summary>Что выпадает из блока. roll — случайное число 0..1.</summary>
        public static ItemType DropFrom(BlockType block, float roll)
        {
            switch (block)
            {
                case BlockType.Grass: return ItemType.Grass;
                case BlockType.Dirt: return ItemType.Dirt;
                case BlockType.Stone: return ItemType.Cobblestone;
                case BlockType.Cobblestone: return ItemType.Cobblestone;
                case BlockType.Wood: return ItemType.Wood;
                case BlockType.Leaves: return roll < 0.25f ? ItemType.Apple : ItemType.None;
                case BlockType.Planks: return ItemType.Planks;
                case BlockType.CraftingTable: return ItemType.CraftingTable;
                default: return ItemType.None;
            }
        }

        /// <summary>Какой блок ставит предмет. Air — предмет не ставится.</summary>
        public static BlockType ToBlock(ItemType item)
        {
            switch (item)
            {
                case ItemType.Grass: return BlockType.Grass;
                case ItemType.Dirt: return BlockType.Dirt;
                case ItemType.Stone: return BlockType.Stone;
                case ItemType.Wood: return BlockType.Wood;
                case ItemType.Leaves: return BlockType.Leaves;
                case ItemType.Planks: return BlockType.Planks;
                case ItemType.CraftingTable: return BlockType.CraftingTable;
                case ItemType.Cobblestone: return BlockType.Cobblestone;
                default: return BlockType.Air;
            }
        }

        /// <summary>Отображаемое имя предмета для подписи над хотбаром.</summary>
        public static string Name(ItemType item)
        {
            switch (item)
            {
                case ItemType.Grass: return "Дёрн";
                case ItemType.Dirt: return "Земля";
                case ItemType.Stone: return "Камень";
                case ItemType.Wood: return "Дерево";
                case ItemType.Leaves: return "Листва";
                case ItemType.Apple: return "Яблоко";
                case ItemType.Planks: return "Доски";
                case ItemType.Stick: return "Палка";
                case ItemType.CraftingTable: return "Верстак";
                case ItemType.Cobblestone: return "Булыжник";
                case ItemType.WoodenPickaxe: return "Деревянная кирка";
                case ItemType.StonePickaxe: return "Каменная кирка";
                case ItemType.WoodenAxe: return "Деревянный топор";
                case ItemType.StoneAxe: return "Каменный топор";
                case ItemType.WoodenShovel: return "Деревянная лопата";
                case ItemType.StoneShovel: return "Каменная лопата";
                case ItemType.WoodenSword: return "Деревянный меч";
                case ItemType.StoneSword: return "Каменный меч";
                default: return string.Empty;
            }
        }

        /// <summary>Тип инструмента предмета. Для обычных предметов — None.</summary>
        public static ToolType Tool(ItemType item)
        {
            switch (item)
            {
                case ItemType.WoodenPickaxe:
                case ItemType.StonePickaxe:
                    return ToolType.Pickaxe;
                case ItemType.WoodenAxe:
                case ItemType.StoneAxe:
                    return ToolType.Axe;
                case ItemType.WoodenShovel:
                case ItemType.StoneShovel:
                    return ToolType.Shovel;
                case ItemType.WoodenSword:
                case ItemType.StoneSword:
                    return ToolType.Sword;
                default:
                    return ToolType.None;
            }
        }

        /// <summary>Уровень инструмента. Для руки и обычных предметов — Hand.</summary>
        public static ToolTier Tier(ItemType item)
        {
            switch (item)
            {
                case ItemType.WoodenPickaxe:
                case ItemType.WoodenAxe:
                case ItemType.WoodenShovel:
                case ItemType.WoodenSword:
                    return ToolTier.Wood;
                case ItemType.StonePickaxe:
                case ItemType.StoneAxe:
                case ItemType.StoneShovel:
                case ItemType.StoneSword:
                    return ToolTier.Stone;
                default:
                    return ToolTier.Hand;
            }
        }

        /// <summary>Множитель скорости ломания для инструмента. Для руки — 1.</summary>
        public static float MiningSpeed(ItemType item)
        {
            switch (item)
            {
                case ItemType.WoodenPickaxe:
                case ItemType.WoodenAxe:
                case ItemType.WoodenShovel:
                    return 2f;
                case ItemType.StonePickaxe:
                case ItemType.StoneAxe:
                case ItemType.StoneShovel:
                    return 4f;
                case ItemType.WoodenSword:
                case ItemType.StoneSword:
                    return 1.5f;
                default:
                    return 1f;
            }
        }
    }
}