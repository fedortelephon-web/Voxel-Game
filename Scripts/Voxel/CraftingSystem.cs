using System.Collections.Generic;
using UnityEngine;

namespace Voxel
{
    /// <summary>
    /// Система крафта: хранит рецепты, проверяет сетку, выдаёт результат.
    /// Работает с сетками любого размера (2×2 в инвентаре, 3×3 у верстака).
    /// Рецепт может быть меньше сетки и размещаться в любом её месте.
    /// Рецепты с флагом AllowRotation допускают повороты/отражения.
    /// Флаг AllowMirror допускает только отражение без поворотов.
    /// </summary>
    public static class CraftingSystem
    {
        /// <summary>Рецепт: сетка произвольного размера, результат и флаги поворотов.</summary>
        public class Recipe
        {
            public readonly ItemType[,] Grid;
            public readonly ItemType Result;
            public readonly int ResultCount;
            public readonly bool AllowRotation;
            public readonly bool AllowMirror;

            public Recipe(ItemType[,] grid, ItemType result, int count = 1, bool allowRotation = true, bool allowMirror = false)
            {
                Grid = grid;
                Result = result;
                ResultCount = count;
                AllowRotation = allowRotation;
                AllowMirror = allowMirror;
            }
        }

        private static readonly List<Recipe> Recipes = new List<Recipe>
        {
            // Wood → 4 Planks (один блок в любом месте сетки)
            new Recipe(
                new ItemType[,] { { ItemType.Wood } },
                ItemType.Planks, 4, allowRotation: true),

            // 2 Planks вертикально → 4 Sticks (только вертикаль, в любом столбце)
            new Recipe(
                new ItemType[,] { { ItemType.Planks }, { ItemType.Planks } },
                ItemType.Stick, 4, allowRotation: false),

            // 4 Planks в квадрате 2×2 → верстак
            new Recipe(
                new ItemType[,]
                {
                    { ItemType.Planks, ItemType.Planks },
                    { ItemType.Planks, ItemType.Planks },
                },
                ItemType.CraftingTable, 1, allowRotation: true),

            // ==== Инструменты (только верстак 3×3) ====

            // Деревянная кирка
            new Recipe(
                new ItemType[,]
                {
                    { ItemType.Planks, ItemType.Planks, ItemType.Planks },
                    { ItemType.None,   ItemType.Stick,  ItemType.None },
                    { ItemType.None,   ItemType.Stick,  ItemType.None },
                },
                ItemType.WoodenPickaxe, 1, allowRotation: false),

            // Каменная кирка
            new Recipe(
                new ItemType[,]
                {
                    { ItemType.Cobblestone, ItemType.Cobblestone, ItemType.Cobblestone },
                    { ItemType.None,        ItemType.Stick,       ItemType.None },
                    { ItemType.None,        ItemType.Stick,       ItemType.None },
                },
                ItemType.StonePickaxe, 1, allowRotation: false),

            // Деревянный топор (зеркально влево/вправо)
            new Recipe(
                new ItemType[,]
                {
                    { ItemType.Planks, ItemType.Planks, ItemType.None },
                    { ItemType.Planks, ItemType.Stick,  ItemType.None },
                    { ItemType.None,   ItemType.Stick,  ItemType.None },
                },
                ItemType.WoodenAxe, 1, allowRotation: false, allowMirror: true),

            // Каменный топор (зеркально влево/вправо)
            new Recipe(
                new ItemType[,]
                {
                    { ItemType.Cobblestone, ItemType.Cobblestone, ItemType.None },
                    { ItemType.Cobblestone, ItemType.Stick,       ItemType.None },
                    { ItemType.None,        ItemType.Stick,       ItemType.None },
                },
                ItemType.StoneAxe, 1, allowRotation: false, allowMirror: true),

            // Деревянная лопата (вертикально, в любом столбце)
            new Recipe(
                new ItemType[,]
                {
                    { ItemType.Planks },
                    { ItemType.Stick },
                    { ItemType.Stick },
                },
                ItemType.WoodenShovel, 1, allowRotation: false),

            // Каменная лопата (вертикально, в любом столбце)
            new Recipe(
                new ItemType[,]
                {
                    { ItemType.Cobblestone },
                    { ItemType.Stick },
                    { ItemType.Stick },
                },
                ItemType.StoneShovel, 1, allowRotation: false),

            // Деревянный меч (вертикально, остриё вверх)
            new Recipe(
                new ItemType[,]
                {
                    { ItemType.Planks },
                    { ItemType.Planks },
                    { ItemType.Stick },
                },
                ItemType.WoodenSword, 1, allowRotation: false),

            // Каменный меч (вертикально, остриё вверх)
            new Recipe(
                new ItemType[,]
                {
                    { ItemType.Cobblestone },
                    { ItemType.Cobblestone },
                    { ItemType.Stick },
                },
                ItemType.StoneSword, 1, allowRotation: false),
        };

        /// <summary>Проверяет сетку и возвращает найденный рецепт или null.</summary>
        public static Recipe FindRecipe(ItemType[,] grid)
        {
            if (grid == null)
                return null;
            int gridSize = grid.GetLength(0);
            foreach (var recipe in Recipes)
            {
                if (MatchesRecipe(grid, gridSize, recipe))
                    return recipe;
            }
            return null;
        }

        /// <summary>Проверяет один рецепт во всех вариантах и позициях.</summary>
        private static bool MatchesRecipe(ItemType[,] grid, int gridSize, Recipe recipe)
        {
            foreach (var variant in GetVariants(recipe))
            {
                var trimmed = TrimEmptyBorders(variant);
                int patH = trimmed.GetLength(0);
                int patW = trimmed.GetLength(1);
                if (patH == 0 || patW == 0 || patH > gridSize || patW > gridSize)
                    continue;

                for (int dy = 0; dy <= gridSize - patH; dy++)
                for (int dx = 0; dx <= gridSize - patW; dx++)
                {
                    if (MatchesAt(grid, trimmed, dx, dy, gridSize))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Варианты паттерна: повороты/отражения, только отражение или только оригинал.</summary>
        private static List<ItemType[,]> GetVariants(Recipe recipe)
        {
            var result = new List<ItemType[,]>();

            if (!recipe.AllowRotation)
            {
                result.Add(recipe.Grid);
                if (recipe.AllowMirror)
                    result.Add(FlipH(recipe.Grid));
                return result;
            }

            var p = recipe.Grid;
            for (int rot = 0; rot < 4; rot++)
            {
                result.Add(p);
                result.Add(FlipH(p));
                p = Rotate90(p);
            }
            return result;
        }

        /// <summary>Совпадает ли паттерн в позиции (dx, dy); вне паттерна должно быть пусто.</summary>
        private static bool MatchesAt(ItemType[,] grid, ItemType[,] pattern, int dx, int dy, int gridSize)
        {
            int patH = pattern.GetLength(0);
            int patW = pattern.GetLength(1);

            for (int y = 0; y < patH; y++)
            for (int x = 0; x < patW; x++)
            {
                if (grid[dy + y, dx + x] != pattern[y, x])
                    return false;
            }

            // Вне паттерна должно быть пусто
            for (int y = 0; y < gridSize; y++)
            for (int x = 0; x < gridSize; x++)
            {
                bool inside = x >= dx && x < dx + patW && y >= dy && y < dy + patH;
                if (!inside && grid[y, x] != ItemType.None)
                    return false;
            }

            return true;
        }

        /// <summary>Обрезает пустые края паттерна до минимального прямоугольника.</summary>
        private static ItemType[,] TrimEmptyBorders(ItemType[,] grid)
        {
            int h = grid.GetLength(0);
            int w = grid.GetLength(1);
            int minR = h, maxR = -1, minC = w, maxC = -1;

            for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                if (grid[r, c] == ItemType.None)
                    continue;
                if (r < minR) minR = r;
                if (r > maxR) maxR = r;
                if (c < minC) minC = c;
                if (c > maxC) maxC = c;
            }

            if (maxR < 0)
                return new ItemType[0, 0];

            int newH = maxR - minR + 1;
            int newW = maxC - minC + 1;
            var result = new ItemType[newH, newW];
            for (int r = 0; r < newH; r++)
            for (int c = 0; c < newW; c++)
                result[r, c] = grid[minR + r, minC + c];

            return result;
        }

        private static ItemType[,] Rotate90(ItemType[,] src)
        {
            int h = src.GetLength(0);
            int w = src.GetLength(1);
            var result = new ItemType[w, h];
            for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
                result[c, h - 1 - r] = src[r, c];
            return result;
        }

        private static ItemType[,] FlipH(ItemType[,] src)
        {
            int h = src.GetLength(0);
            int w = src.GetLength(1);
            var result = new ItemType[h, w];
            for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
                result[r, w - 1 - c] = src[r, c];
            return result;
        }
    }
}