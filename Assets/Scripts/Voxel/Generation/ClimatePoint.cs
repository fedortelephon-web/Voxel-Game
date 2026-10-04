namespace Voxel
{
    /// <summary>
    /// Значения всех климатических Noise Map в одной мировой координате.
    /// </summary>
    public readonly struct ClimatePoint
    {
        public readonly float Temperature;
        public readonly float Humidity;
        public readonly float Continentalness;
        public readonly float Erosion;

        public ClimatePoint(
            float temperature,
            float humidity,
            float continentalness,
            float erosion)
        {
            Temperature = temperature;
            Humidity = humidity;
            Continentalness = continentalness;
            Erosion = erosion;
        }
    }
}