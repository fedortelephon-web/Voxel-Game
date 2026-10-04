namespace Voxel
{
    /// <summary>
    /// Все характеристики одного биома.
    /// </summary>
    public sealed class BiomeDefinition
    {
        public readonly BiomeType Type;

        public readonly float Temperature;
        public readonly float Humidity;
        public readonly float Continentalness;
        public readonly float Erosion;

        public readonly float TemperatureWeight;
        public readonly float HumidityWeight;
        public readonly float ContinentalnessWeight;
        public readonly float ErosionWeight;

        public readonly BlockType SurfaceBlock;
        public readonly BlockType FillerBlock;
        public readonly int DirtDepth;
        public readonly float TreeChance;

        public BiomeDefinition(
            BiomeType type,
            float temperature,
            float humidity,
            float continentalness,
            float erosion,
            BlockType surfaceBlock,
            BlockType fillerBlock,
            int dirtDepth,
            float treeChance,
            float temperatureWeight = 1f,
            float humidityWeight = 1f,
            float continentalnessWeight = 1f,
            float erosionWeight = 1f)
        {
            Type = type;

            Temperature = temperature;
            Humidity = humidity;
            Continentalness = continentalness;
            Erosion = erosion;

            TemperatureWeight = temperatureWeight;
            HumidityWeight = humidityWeight;
            ContinentalnessWeight = continentalnessWeight;
            ErosionWeight = erosionWeight;

            SurfaceBlock = surfaceBlock;
            FillerBlock = fillerBlock;
            DirtDepth = dirtDepth;
            TreeChance = treeChance;
        }

        public float DistanceTo(ClimatePoint climate)
        {
            float dt = climate.Temperature - Temperature;
            float dh = climate.Humidity - Humidity;
            float dc = climate.Continentalness - Continentalness;
            float de = climate.Erosion - Erosion;

            return
                dt * dt * TemperatureWeight +
                dh * dh * HumidityWeight +
                dc * dc * ContinentalnessWeight +
                de * de * ErosionWeight;
        }
    }
}