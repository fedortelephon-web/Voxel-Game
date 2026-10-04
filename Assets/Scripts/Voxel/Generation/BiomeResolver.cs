namespace Voxel
{
    /// <summary>
    /// Определяет биом по ближайшей точке в Multi-Noise пространстве.
    /// </summary>
    public static class BiomeResolver
    {
        public static BiomeDefinition Resolve(ClimatePoint climate)
        {
            BiomeDefinition bestBiome = BiomeRegistry.All[0];
            float bestDistance = float.PositiveInfinity;

            foreach (BiomeDefinition biome in BiomeRegistry.All)
            {
                float distance = biome.DistanceTo(climate);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestBiome = biome;
                }
            }

            return bestBiome;
        }
    }
}