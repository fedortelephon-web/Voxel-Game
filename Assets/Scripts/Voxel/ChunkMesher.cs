using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Voxel
{
    /// <summary>
    /// Собирает меш чанка и учитывает блоки соседних загруженных чанков
    /// на границах. Это нужно для бесшовного chunk streaming.
    /// </summary>
    public static class ChunkMesher
    {
        // Предварительное резервирование уменьшает расширения List и
        // временный мусор при построении мешей.
        private const int InitialVertexCapacity = 4096;
        private const int InitialTriangleIndexCapacity = 6144;

        private static readonly Vector3Int[] Directions =
        {
            Vector3Int.right, Vector3Int.left,
            Vector3Int.up, Vector3Int.down,
            Vector3Int.forward, Vector3Int.back,
        };

        private static readonly Vector3[][] FaceCorners =
        {
            new[] { new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) },
            new[] { new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0), new Vector3(0,0,0) },
            new[] { new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0), new Vector3(0,1,0) },
            new[] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1), new Vector3(0,0,1) },
            new[] { new Vector3(0,0,1), new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1) },
            new[] { new Vector3(1,0,0), new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0) },
        };

        private static readonly float[] FaceShade =
        {
            0.8f, 0.8f, 1f, 0.5f, 0.7f, 0.7f
        };

        public static Mesh BuildMesh(
            ChunkData chunk,
            WorldManager worldManager,
            int chunkX,
            int chunkZ)
        {
            var vertices =
                new List<Vector3>(InitialVertexCapacity);

            var normals =
                new List<Vector3>(InitialVertexCapacity);

            var colors =
                new List<Color>(InitialVertexCapacity);

            var uvs =
                new List<Vector2>(InitialVertexCapacity);

            var opaqueTriangles =
                new List<int>(InitialTriangleIndexCapacity);

            var waterTriangles =
                new List<int>(InitialTriangleIndexCapacity);

            for (int y = 0; y < ChunkData.SizeY; y++)
            for (int z = 0; z < ChunkData.SizeZ; z++)
            for (int x = 0; x < ChunkData.SizeX; x++)
            {
                BlockType block =
                    chunk.GetBlock(x, y, z);

                if (block == BlockType.Air)
                    continue;

                for (int face = 0; face < 6; face++)
                {
                    Vector3Int dir =
                        Directions[face];

                    int nx = x + dir.x;
                    int ny = y + dir.y;
                    int nz = z + dir.z;

                    BlockType neighbor;

                    if (nx >= 0 && nx < ChunkData.SizeX &&
                        ny >= 0 && ny < ChunkData.SizeY &&
                        nz >= 0 && nz < ChunkData.SizeZ)
                    {
                        neighbor =
                            chunk.GetBlock(
                                nx,
                                ny,
                                nz);
                    }
                    else
                    {
                        neighbor =
                            worldManager.GetBlock(
                                new Vector3Int(
                                    chunkX * ChunkData.SizeX + nx,
                                    ny,
                                    chunkZ * ChunkData.SizeZ + nz));
                    }

                    if (neighbor != BlockType.Air)
                        continue;

                    Rect tileUV =
                        VoxelTextures.UVRect(
                            VoxelTextures.TileForBlock(
                                block,
                                face));

                    Color tint = Color.white;

                    if (block == BlockType.Grass ||
                        block == BlockType.Leaves)
                    {
                        tint =
                            worldManager.GetVegetationTint(
                                chunkX * ChunkData.SizeX + x,
                                chunkZ * ChunkData.SizeZ + z);
                    }

                    List<int> targetTriangles =
                        block == BlockType.Water
                            ? waterTriangles
                            : opaqueTriangles;

                    AddFace(
                        vertices,
                        normals,
                        colors,
                        uvs,
                        targetTriangles,
                        new Vector3(x, y, z),
                        face,
                        tileUV,
                        tint,
                        block == BlockType.Water);
                }
            }

            var mesh =
                new Mesh
                {
                    name = "ChunkMesh"
                };

            // Индексный буфер 16-bit почти всегда достаточен для нашего
            // чанка; при переполнении автоматически остаёмся на 32-bit.
            mesh.indexFormat =
                vertices.Count <= 65535
                    ? IndexFormat.UInt16
                    : IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(
                opaqueTriangles,
                0,
                true);

            mesh.SetTriangles(
                waterTriangles,
                1,
                true);

            return mesh;
        }

        private static void AddFace(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Color> colors,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 blockPos,
            int face,
            Rect tileUV,
            Color tint,
            bool transparent)
        {
            int baseIndex =
                vertices.Count;

            float shade =
                FaceShade[face];

            var faceColor =
                new Color(
                    shade * tint.r,
                    shade * tint.g,
                    shade * tint.b,
                    transparent ? 0.72f : 1f);

            foreach (Vector3 corner
                     in FaceCorners[face])
            {
                vertices.Add(
                    blockPos + corner);

                normals.Add(
                    Directions[face]);

                colors.Add(
                    faceColor);

                FaceUV(
                    face,
                    corner,
                    out float u,
                    out float v);

                uvs.Add(
                    new Vector2(
                        tileUV.x +
                        u * tileUV.width,
                        tileUV.y +
                        v * tileUV.height));
            }

            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 3);
        }

        private static void FaceUV(
            int face,
            Vector3 corner,
            out float u,
            out float v)
        {
            switch (face)
            {
                case 0:
                case 1:
                    u = corner.z;
                    v = corner.y;
                    break;

                case 2:
                case 3:
                    u = corner.x;
                    v = corner.z;
                    break;

                default:
                    u = corner.x;
                    v = corner.y;
                    break;
            }
        }
    }
}