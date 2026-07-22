namespace Sandbox;

/// <summary>Procedurally generates a flat quad in its own local XY plane, normal +Z, centered at the origin,
/// spanning [-halfExtent, halfExtent] in local X and Y. Callers position/orient it (floor, ceiling, wall...)
/// entirely via the model matrix — one mesh/BLAS reused for every room surface.</summary>
internal static class Quad
{
    public static (float[] Vertices, ushort[] Indices) Generate(float halfExtent = 4f, float uvTiling = 1f)
    {
        float[] vertices =
        [
            -halfExtent, -halfExtent, 0f,   0f, 0f, 1f,   0f, 0f,
             halfExtent, -halfExtent, 0f,   0f, 0f, 1f,   uvTiling, 0f,
             halfExtent,  halfExtent, 0f,   0f, 0f, 1f,   uvTiling, uvTiling,
            -halfExtent,  halfExtent, 0f,   0f, 0f, 1f,   0f, uvTiling,
        ];
        ushort[] indices = [0, 1, 2, 2, 3, 0];
        return (vertices, indices);
    }
}
