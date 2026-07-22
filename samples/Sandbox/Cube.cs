using System.Numerics;

namespace Sandbox;

/// <summary>Procedurally generates a unit cube (24 verts / 36 indices) — no asset pipeline needed yet.</summary>
internal static class Cube
{
    // Each face is defined by (normal, up, right) with right x up == normal, so every face
    // comes out wound consistently (CCW as viewed from outside along its normal).
    private static readonly (Vector3 Normal, Vector3 Up, Vector3 Right)[] Faces =
    [
        (Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX),       // +Z front
        (-Vector3.UnitZ, Vector3.UnitY, -Vector3.UnitX),     // -Z back
        (Vector3.UnitX, Vector3.UnitY, -Vector3.UnitZ),      // +X right
        (-Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),      // -X left
        (Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitX),      // +Y top
        (-Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX),      // -Y bottom
    ];

    public static (float[] Vertices, ushort[] Indices) Generate(float halfExtent = 0.5f)
    {
        // Stride per vertex: position(3) + normal(3) + uv(2) = 8 floats.
        float[] vertices = new float[Faces.Length * 4 * 8];
        ushort[] indices = new ushort[Faces.Length * 6];

        for (int f = 0; f < Faces.Length; f++)
        {
            var (normal, up, right) = Faces[f];
            Span<Vector3> corners =
            [
                normal * halfExtent - right * halfExtent - up * halfExtent,
                normal * halfExtent + right * halfExtent - up * halfExtent,
                normal * halfExtent + right * halfExtent + up * halfExtent,
                normal * halfExtent - right * halfExtent + up * halfExtent,
            ];
            Span<Vector2> uvs = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)];

            int baseVertex = f * 4;
            for (int i = 0; i < 4; i++)
            {
                int o = (baseVertex + i) * 8;
                vertices[o + 0] = corners[i].X;
                vertices[o + 1] = corners[i].Y;
                vertices[o + 2] = corners[i].Z;
                vertices[o + 3] = normal.X;
                vertices[o + 4] = normal.Y;
                vertices[o + 5] = normal.Z;
                vertices[o + 6] = uvs[i].X;
                vertices[o + 7] = uvs[i].Y;
            }

            int baseIndex = f * 6;
            indices[baseIndex + 0] = (ushort)(baseVertex + 0);
            indices[baseIndex + 1] = (ushort)(baseVertex + 1);
            indices[baseIndex + 2] = (ushort)(baseVertex + 2);
            indices[baseIndex + 3] = (ushort)(baseVertex + 2);
            indices[baseIndex + 4] = (ushort)(baseVertex + 3);
            indices[baseIndex + 5] = (ushort)(baseVertex + 0);
        }

        return (vertices, indices);
    }
}
