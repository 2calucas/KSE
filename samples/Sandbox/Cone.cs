using System.Numerics;

namespace Sandbox;

/// <summary>Procedurally generates a right circular cone: base ring at local y=0, apex at local y=height, plus
/// a flat downward-facing base cap. Shares the cube/plane/sphere interleaved position(3)+normal(3)+uv(2) layout.</summary>
internal static class Cone
{
    public static (float[] Vertices, ushort[] Indices) Generate(float radius = 0.7f, float height = 1.6f, int segments = 24)
    {
        float halfAngle = MathF.Atan2(radius, height);
        float cosHalf = MathF.Cos(halfAngle);
        float sinHalf = MathF.Sin(halfAngle);

        List<float> vertices = [];
        List<ushort> indices = [];
        int vi = 0;

        void AddVertex(Vector3 pos, Vector3 normal, Vector2 uv)
        {
            vertices.Add(pos.X); vertices.Add(pos.Y); vertices.Add(pos.Z);
            vertices.Add(normal.X); vertices.Add(normal.Y); vertices.Add(normal.Z);
            vertices.Add(uv.X); vertices.Add(uv.Y);
            vi++;
        }

        // Side surface: a smoothly-varying slant normal per angle (apex vertex duplicated per segment so its
        // normal can vary along with the base ring, rather than averaging to a meaningless single apex normal).
        int sideBase = vi;
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float theta = t * MathF.PI * 2f;
            float cosT = MathF.Cos(theta), sinT = MathF.Sin(theta);
            Vector3 normal = new(cosT * cosHalf, sinHalf, sinT * cosHalf);
            AddVertex(new Vector3(0f, height, 0f), normal, new Vector2(t, 1f));
            AddVertex(new Vector3(cosT * radius, 0f, sinT * radius), normal, new Vector2(t, 0f));
        }
        for (int i = 0; i < segments; i++)
        {
            int apex0 = sideBase + i * 2;
            int base0 = apex0 + 1;
            int apex1 = sideBase + (i + 1) * 2;
            int base1 = apex1 + 1;
            indices.Add((ushort)apex0); indices.Add((ushort)base0); indices.Add((ushort)base1);
            indices.Add((ushort)apex0); indices.Add((ushort)base1); indices.Add((ushort)apex1);
        }

        // Base cap, flat, facing down.
        int capCenter = vi;
        AddVertex(Vector3.Zero, new Vector3(0f, -1f, 0f), new Vector2(0.5f, 0.5f));
        int capRingStart = vi;
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float theta = t * MathF.PI * 2f;
            Vector3 pos = new(MathF.Cos(theta) * radius, 0f, MathF.Sin(theta) * radius);
            Vector2 uv = new(0.5f + MathF.Cos(theta) * 0.5f, 0.5f + MathF.Sin(theta) * 0.5f);
            AddVertex(pos, new Vector3(0f, -1f, 0f), uv);
        }
        for (int i = 0; i < segments; i++)
        {
            indices.Add((ushort)capCenter);
            indices.Add((ushort)(capRingStart + i + 1));
            indices.Add((ushort)(capRingStart + i));
        }

        return (vertices.ToArray(), indices.ToArray());
    }
}
