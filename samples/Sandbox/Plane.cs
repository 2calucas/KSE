namespace Sandbox;

/// <summary>Procedurally generates a flat ground plane (4 verts / 6 indices) to receive the cube's shadow.</summary>
internal static class Plane
{
    public static (float[] Vertices, ushort[] Indices) Generate(float halfExtent = 3f, float uvTiling = 4f)
    {
        // position(3) + normal(3) + uv(2) per vertex, normal is straight up.
        float[] vertices =
        [
            -halfExtent, 0f, -halfExtent, 0f, 1f, 0f, 0f, 0f,
             halfExtent, 0f, -halfExtent, 0f, 1f, 0f, uvTiling, 0f,
             halfExtent, 0f,  halfExtent, 0f, 1f, 0f, uvTiling, uvTiling,
            -halfExtent, 0f,  halfExtent, 0f, 1f, 0f, 0f, uvTiling,
        ];
        ushort[] indices = [0, 1, 2, 2, 3, 0];
        return (vertices, indices);
    }
}
