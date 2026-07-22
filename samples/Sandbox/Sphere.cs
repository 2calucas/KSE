using System.Numerics;

namespace Sandbox;

/// <summary>Procedurally generates a UV sphere (lat/lon bands). Shares the cube/plane's interleaved
/// position(3)+normal(3)+uv(2) vertex layout so it can reuse the same pipelines and BLAS geometry setup.</summary>
internal static class Sphere
{
    public static (float[] Vertices, ushort[] Indices) Generate(float radius = 0.6f, int latSegments = 16, int lonSegments = 24)
    {
        int rowStride = lonSegments + 1;
        int vertexCount = (latSegments + 1) * rowStride;
        float[] vertices = new float[vertexCount * 8];

        int vi = 0;
        for (int lat = 0; lat <= latSegments; lat++)
        {
            float v = (float)lat / latSegments;
            float theta = v * MathF.PI;
            float sinTheta = MathF.Sin(theta), cosTheta = MathF.Cos(theta);

            for (int lon = 0; lon <= lonSegments; lon++)
            {
                float u = (float)lon / lonSegments;
                float phi = u * MathF.PI * 2f;
                float sinPhi = MathF.Sin(phi), cosPhi = MathF.Cos(phi);

                Vector3 normal = new(sinTheta * cosPhi, cosTheta, sinTheta * sinPhi);
                Vector3 position = normal * radius;

                int o = vi * 8;
                vertices[o + 0] = position.X;
                vertices[o + 1] = position.Y;
                vertices[o + 2] = position.Z;
                vertices[o + 3] = normal.X;
                vertices[o + 4] = normal.Y;
                vertices[o + 5] = normal.Z;
                vertices[o + 6] = u;
                vertices[o + 7] = v;
                vi++;
            }
        }

        // CullMode.None is used for every pipeline in this sample, so winding order doesn't affect correctness.
        var indices = new List<ushort>(latSegments * lonSegments * 6);
        for (int lat = 0; lat < latSegments; lat++)
        {
            for (int lon = 0; lon < lonSegments; lon++)
            {
                int i0 = lat * rowStride + lon;
                int i1 = i0 + 1;
                int i2 = i0 + rowStride;
                int i3 = i2 + 1;
                indices.Add((ushort)i0); indices.Add((ushort)i2); indices.Add((ushort)i1);
                indices.Add((ushort)i1); indices.Add((ushort)i2); indices.Add((ushort)i3);
            }
        }

        return (vertices, indices.ToArray());
    }
}
