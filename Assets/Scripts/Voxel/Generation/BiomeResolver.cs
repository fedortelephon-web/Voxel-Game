namespace Voxel
{
    /// <summary>
    /// Определяет биом по ближайшей точке в Multi-Noise пространстве.
    /// </summary>
    public static class BiomeResolver
    {
        public static BiomeDefinition Resolve(
            ClimatePoint climate,
            int height)
        {
            // Сначала учитываем высоту рельефа.
            // Низины — океаны, высокогорья — горы.
            if (height < 50)
                return Get(BiomeType.Ocean);

            if (height >= 88)
                return Get(BiomeType.Mountains);

            // На равнинах и холмах биом зависит от климата.
            if (climate.Temperature > 0.68f &&
                climate.Humidity < 0.40f)
            {
                return Get(BiomeType.Desert);
            }

            if (climate.Temperature < 0.38f &&
                climate.Humidity > 0.45f)
            {
                return Get(BiomeType.Taiga);
            }

            if (climate.Humidity > 0.75f &&
                climate.Temperature > 0.35f &&
                climate.Temperature < 0.72f)
            {
                return Get(BiomeType.Swamp);
            }

            if (climate.Humidity > 0.58f &&
                climate.Temperature > 0.35f &&
                climate.Temperature < 0.72f)
            {
                return Get(BiomeType.Forest);
            }

            return Get(BiomeType.Plains);
        }

        private static BiomeDefinition Get(BiomeType type)
        {
            foreach (BiomeDefinition biome in BiomeRegistry.All)
            {
                if (biome.Type == type)
                    return biome;
            }

            return BiomeRegistry.All[0];
        }
    }
}