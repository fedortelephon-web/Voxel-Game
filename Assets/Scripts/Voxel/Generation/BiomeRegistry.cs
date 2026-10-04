namespace Voxel
{
    /// <summary>
    /// Все доступные биомы мира.
    /// </summary>
    public static class BiomeRegistry
    {
        public static readonly BiomeDefinition[] All =
        {
            new BiomeDefinition(
                BiomeType.Plains,
                temperature: 0.50f,
                humidity: 0.45f,
                continentalness: 0.55f,
                erosion: 0.80f,
                surfaceBlock: BlockType.Grass,
                fillerBlock: BlockType.Grass,
                dirtDepth: 32,
                treeChance: 0f),

            new BiomeDefinition(
                BiomeType.Forest,
                temperature: 0.50f,
                humidity: 0.78f,
                continentalness: 0.55f,
                erosion: 0.65f,
                surfaceBlock: BlockType.Sand,
                fillerBlock: BlockType.Sand,
                dirtDepth: 32,
                treeChance: 0f),

            new BiomeDefinition(
                BiomeType.Desert,
                temperature: 0.85f,
                humidity: 0.15f,
                continentalness: 0.65f,
                erosion: 0.85f,
                surfaceBlock: BlockType.Stone,
                fillerBlock: BlockType.Stone,
                dirtDepth: 32,
                treeChance: 0f),

            new BiomeDefinition(
                BiomeType.Taiga,
                temperature: 0.20f,
                humidity: 0.65f,
                continentalness: 0.55f,
                erosion: 0.65f,
                surfaceBlock: BlockType.Planks,
                fillerBlock: BlockType.Planks,
                dirtDepth: 32,
                treeChance: 0f),

            new BiomeDefinition(
                BiomeType.Mountains,
                temperature: 0.35f,
                humidity: 0.45f,
                continentalness: 0.85f,
                erosion: 0.05f,
                surfaceBlock: BlockType.Wood,
                fillerBlock: BlockType.Wood,
                dirtDepth: 32,
                treeChance: 0f,
                temperatureWeight: 1.2f,
                humidityWeight: 1.2f,
                continentalnessWeight: 2.0f,
                erosionWeight: 3.0f),

            new BiomeDefinition(
                BiomeType.Swamp,
                temperature: 0.65f,
                humidity: 0.90f,
                continentalness: 0.40f,
                erosion: 0.90f,
                surfaceBlock: BlockType.CraftingTable,
                fillerBlock: BlockType.CraftingTable,
                dirtDepth: 32,
                treeChance: 0f),

            new BiomeDefinition(
                BiomeType.Ocean,
                temperature: 0.50f,
                humidity: 1.00f,
                continentalness: 0.20f,
                erosion: 0.50f,
                surfaceBlock: BlockType.Sand,
                fillerBlock: BlockType.Sand,
                dirtDepth: 32,
                treeChance: 0f),
        };
    }
}