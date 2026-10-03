using UnityEngine;

namespace Voxel
{
    /// <summary>Цвета блоков и предметов, пока нет текстур.</summary>
    public static class VoxelColors
    {
        /// <summary>Цвет блока в мире.</summary>
        public static Color ForBlock(BlockType type)
        {
            switch (type)
            {
                case BlockType.Grass: return new Color(0.25f, 0.7f, 0.25f);
                case BlockType.Dirt: return new Color(0.45f, 0.32f, 0.2f);
                case BlockType.Stone: return new Color(0.55f, 0.55f, 0.55f);
                case BlockType.Wood: return new Color(0.42f, 0.3f, 0.15f);
                case BlockType.Leaves: return new Color(0.1f, 0.45f, 0.12f);
                case BlockType.Planks: return new Color(0.7f, 0.5f, 0.3f);
                case BlockType.CraftingTable: return new Color(0.55f, 0.38f, 0.22f);
                case BlockType.Cobblestone: return new Color(0.48f, 0.48f, 0.5f);
                default: return Color.magenta;
            }
        }

        /// <summary>Цвет иконки предмета в хотбаре.</summary>
        public static Color ForItem(ItemType type)
        {
            switch (type)
            {
                case ItemType.Apple: return new Color(0.85f, 0.1f, 0.1f);
                case ItemType.Stick: return new Color(0.5f, 0.35f, 0.2f);
                case ItemType.Cobblestone: return new Color(0.48f, 0.48f, 0.5f);
                case ItemType.WoodenPickaxe: return new Color(0.7f, 0.5f, 0.3f);
                case ItemType.StonePickaxe: return new Color(0.55f, 0.55f, 0.55f);
                case ItemType.WoodenAxe: return new Color(0.65f, 0.45f, 0.25f);
                case ItemType.StoneAxe: return new Color(0.5f, 0.5f, 0.52f);
                case ItemType.WoodenShovel: return new Color(0.75f, 0.55f, 0.35f);
                case ItemType.StoneShovel: return new Color(0.6f, 0.6f, 0.6f);
                case ItemType.WoodenSword: return new Color(0.8f, 0.6f, 0.3f);
                case ItemType.StoneSword: return new Color(0.65f, 0.65f, 0.65f);
                default: return ForBlock(ItemUtils.ToBlock(type));
            }
        }
    }
}