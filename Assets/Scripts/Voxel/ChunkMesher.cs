using System.Collections.Generic;
using UnityEngine;

namespace Voxel
{
    /// <summary>
    /// Собирает меш чанка: рисует только грани, граничащие с воздухом.
    /// Грани получают UV из пиксельного атласа; цвет вершин — затенение грани.
    /// </summary>
    public static class ChunkMesher
    {
        // Шесть направлений и для каждого — 4 угла квада.
        // Порядок углов подобран так, чтобы треугольники смотрели наружу.
        private static readonly Vector3Int[] Directions =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up,
            Vector3Int.down, Vector3Int.forward, Vector3Int.back,
        };

        private static readonly Vector3[][] FaceCorners =
        {
            new[] { new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) }, // +X
            new[] { new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0), new Vector3(0,0,0) }, // -X
            new[] { new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0), new Vector3(0,1,0) }, // +Y
            new[] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1), new Vector3(0,0,1) }, // -Y
            new[] { new Vector3(0,0,1), new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1) }, // +Z
            new[] { new Vector3(1,0,0), new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0) }, // -Z
        };

        // Яркость грани по направлению: верх светлее, бока и низ темнее.
        // Порядок совпадает с массивом Directions.
        private static readonly float[] FaceShade = { 0.8f, 0.8f, 1f, 0.5f, 0.7f, 0.7f };

        /// <summary>Строит меш из данных чанка.</summary>
        public static Mesh BuildMesh(ChunkData chunk)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int y = 0; y < ChunkData.SizeY; y++)
            for (int z = 0; z < ChunkData.SizeZ; z++)
            for (int x = 0; x < ChunkData.SizeX; x++)
            {
                BlockType block = chunk.GetBlock(x, y, z);
                if (block == BlockType.Air)
                    continue;

                for (int face = 0; face < 6; face++)
                {
                    Vector3Int dir = Directions[face];
                    // Границу чанка считаем воздухом, чтобы края были видны
                    if (chunk.GetBlock(x + dir.x, y + dir.y, z + dir.z) != BlockType.Air)
                        continue;

                    Rect tileUV = VoxelTextures.UVRect(VoxelTextures.TileForBlock(block, face));
                    AddFace(vertices, normals, colors, uvs, triangles,
                        new Vector3(x, y, z), face, tileUV);
                }
            }

            var mesh = new Mesh { name = "ChunkMesh" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(uvs);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        /// <summary>Добавляет один квад грани в списки меша с затенением и UV тайла.</summary>
        private static void AddFace(List<Vector3> vertices, List<Vector3> normals,
            List<Color> colors, List<Vector2> uvs, List<int> triangles,
            Vector3 blockPos, int face, Rect tileUV)
        {
            int baseIndex = vertices.Count;

            // Затенение запекаем в цвет вершин: шейдер умножит его на текстуру
            float shade = FaceShade[face];
            var faceColor = new Color(shade, shade, shade);

            foreach (Vector3 corner in FaceCorners[face])
            {
                vertices.Add(blockPos + corner);
                normals.Add(Directions[face]);
                colors.Add(faceColor);

                FaceUV(face, corner, out float u, out float v);
                uvs.Add(new Vector2(tileUV.x + u * tileUV.width, tileUV.y + v * tileUV.height));
            }

            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 3);
        }

        /// <summary>
        /// Вдоль каких мировых осей идут U и V текстуры для каждой грани.
        /// Для боковых граней V = высота: трава на боку всегда сверху.
        /// </summary>
        private static void FaceUV(int face, Vector3 corner, out float u, out float v)
        {
            switch (face)
            {
                case 0:
                case 1:
                    u = corner.z; v = corner.y; break; // ±X
                case 2:
                case 3:
                    u = corner.x; v = corner.z; break; // ±Y (верх/низ)
                default:
                    u = corner.x; v = corner.y; break; // ±Z
            }
        }
    }
}