namespace Voxel
{
    /// <summary>Хранит блоки одного чанка в плоском массиве.</summary>
    public class ChunkData
    {
        public const int SizeX = 16;
        public const int SizeY = 32;
        public const int SizeZ = 16;

        private readonly BlockType[] _blocks = new BlockType[SizeX * SizeY * SizeZ];

        /// <summary>Блок по координатам внутри чанка; снаружи — воздух.</summary>
        public BlockType GetBlock(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= SizeX || y >= SizeY || z >= SizeZ)
                return BlockType.Air;

            return _blocks[Index(x, y, z)];
        }

        /// <summary>Установить блок по координатам внутри чанка.</summary>
        public void SetBlock(int x, int y, int z, BlockType type)
        {
            if (x < 0 || y < 0 || z < 0 || x >= SizeX || y >= SizeY || z >= SizeZ)
                return;

            _blocks[Index(x, y, z)] = type;
        }

        /// <summary>Байты чанка для сохранения.</summary>
        public byte[] ToBytes()
        {
            var result = new byte[_blocks.Length];
            for (int i = 0; i < _blocks.Length; i++)
                result[i] = (byte)_blocks[i];
            return result;
        }

        /// <summary>Восстановить чанк из байтов.</summary>
        public void FromBytes(byte[] data)
        {
            int n = System.Math.Min(_blocks.Length, data.Length);
            for (int i = 0; i < n; i++)
                _blocks[i] = (BlockType)data[i];
        }

        /// <summary>Индекс в плоском массиве из трёхмерных координат.</summary>
        private static int Index(int x, int y, int z)
        {
            return x + SizeX * (z + SizeZ * y);
        }
    }
}