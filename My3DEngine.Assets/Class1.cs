using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Assimp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace My3DEngine.Assets;

// Motorvänliga asset-format (minimal slice, utbyggbara).
public sealed record AssetScene(IReadOnlyList<AssetNode> Nodes, IReadOnlyList<AssetMesh> Meshes, IReadOnlyList<AssetMaterial> Materials);
public sealed record AssetNode(string Name, System.Numerics.Matrix4x4 LocalTransform, int[] MeshIndices, int[] Children);

public sealed record AssetMesh(string Name, Vector3[] Positions, Vector3[]? Normals, Vector2[]? TexCoords0, int[] Indices, int MaterialIndex);
public sealed record AssetMaterial(string Name, string? BaseColorTexturePath);
public sealed record AssetTexture(string Name, int Width, int Height, Rgba32[] Pixels);

public static class AssetImporter
{
    public static AssetScene ImportScene(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) { throw new ArgumentException("Path is empty.", nameof(path)); }
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) { throw new FileNotFoundException($"Model file not found: {full}", full); }

        using var ctx = new AssimpContext();
        var flags =
            PostProcessSteps.Triangulate |
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.ImproveCacheLocality |
            PostProcessSteps.GenerateNormals |
            PostProcessSteps.LimitBoneWeights |
            PostProcessSteps.FlipUVs;

        var scene = ctx.ImportFile(full, flags);
        if (scene is null) { throw new InvalidOperationException($"Assimp returned null for: {full}"); }
        if (scene.RootNode is null) { throw new InvalidOperationException($"Assimp scene has no RootNode: {full}"); }

        var meshes = new List<AssetMesh>(scene.MeshCount);
        for (int mi = 0; mi < scene.MeshCount; mi++)
        {
            var m = scene.Meshes[mi];
            if (!m.HasVertices) { throw new InvalidOperationException($"Mesh '{m.Name}' has no vertices in: {full}"); }

            var pos = new Vector3[m.VertexCount];
            Vector3[]? nrm = m.HasNormals ? new Vector3[m.VertexCount] : null;
            Vector2[]? uv0 = (m.TextureCoordinateChannelCount > 0 && m.HasTextureCoords(0)) ? new Vector2[m.VertexCount] : null;

            for (int vi = 0; vi < m.VertexCount; vi++)
            {
                var p = m.Vertices[vi];
                pos[vi] = new Vector3(p.X, p.Y, p.Z);

                if (nrm is not null)
                {
                    var n = m.Normals[vi];
                    nrm[vi] = new Vector3(n.X, n.Y, n.Z);
                }

                if (uv0 is not null)
                {
                    var t = m.TextureCoordinateChannels[0][vi];
                    uv0[vi] = new Vector2(t.X, t.Y);
                }
            }

            var indices = new List<int>(m.FaceCount * 3);
            foreach (var face in m.Faces)
            {
                if (face.IndexCount != 3)
                {
                    throw new InvalidOperationException($"Non-triangulated face in mesh '{m.Name}' ({face.IndexCount} indices) in: {full}");
                }
                indices.Add(face.Indices[0]);
                indices.Add(face.Indices[1]);
                indices.Add(face.Indices[2]);
            }

            meshes.Add(new AssetMesh(m.Name, pos, nrm, uv0, indices.ToArray(), m.MaterialIndex));
        }

        var materials = new List<AssetMaterial>(scene.MaterialCount);
        string modelDir = Path.GetDirectoryName(full) ?? Environment.CurrentDirectory;
        for (int i = 0; i < scene.MaterialCount; i++)
        {
            var mat = scene.Materials[i];
            string name = string.IsNullOrWhiteSpace(mat.Name) ? $"Material_{i}" : mat.Name;

            string? texPath = null;
            if (mat.GetMaterialTextureCount(TextureType.Diffuse) > 0 &&
                mat.GetMaterialTexture(TextureType.Diffuse, 0, out var slot))
            {
                // Robust path handling: Assimp often stores relative paths.
                texPath = ResolveAssetPath(modelDir, slot.FilePath);
            }

            materials.Add(new AssetMaterial(name, texPath));
        }

        var nodes = new List<AssetNode>();
        BuildNodes(scene.RootNode, nodes);

        return new AssetScene(nodes, meshes, materials);
    }

    public static AssetTexture LoadTexture(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) { throw new ArgumentException("Path is empty.", nameof(path)); }
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) { throw new FileNotFoundException($"Texture file not found: {full}", full); }

        using Image<Rgba32> img = Image.Load<Rgba32>(full);
        var pixels = new Rgba32[img.Width * img.Height];
        img.CopyPixelDataTo(pixels);
        return new AssetTexture(Path.GetFileName(full), img.Width, img.Height, pixels);
    }

    private static void BuildNodes(Node root, List<AssetNode> outNodes)
    {
        int thisIndex = outNodes.Count;
        outNodes.Add(null!); // filled after recursion (keeps indices stable)

        var children = new int[root.ChildCount];
        for (int i = 0; i < root.ChildCount; i++)
        {
            children[i] = outNodes.Count;
            BuildNodes(root.Children[i], outNodes);
        }

        // AssimpNet's Node.MeshIndices is a managed collection; clone into an array.
        var meshIndices = root.MeshIndices is null ? Array.Empty<int>() : root.MeshIndices.ToArray();
        var local = ToNumerics(root.Transform);

        outNodes[thisIndex] = new AssetNode(root.Name ?? $"Node_{thisIndex}", local, meshIndices, children);
    }

    private static string? ResolveAssetPath(string baseDir, string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) { return null; }

        // Normalize separators and trim quotes.
        string p = rawPath.Trim().Trim('"').Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        // If already absolute, use as-is.
        if (Path.IsPathRooted(p)) { return Path.GetFullPath(p); }

        // Otherwise, resolve relative to model directory.
        return Path.GetFullPath(Path.Combine(baseDir, p));
    }

    private static System.Numerics.Matrix4x4 ToNumerics(Assimp.Matrix4x4 assimp)
    {
        // Assimp.Matrix4x4 fields are row-major in memory order, map explicitly.
        return new System.Numerics.Matrix4x4(
            assimp.A1, assimp.B1, assimp.C1, assimp.D1,
            assimp.A2, assimp.B2, assimp.C2, assimp.D2,
            assimp.A3, assimp.B3, assimp.C3, assimp.D3,
            assimp.A4, assimp.B4, assimp.C4, assimp.D4);
    }
}
