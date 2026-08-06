using UnityEngine;

[RequireComponent(typeof(Terrain))]
public class LowPolyTerrain : MonoBehaviour
{
    [Header("Налаштування генерації")]
    [Tooltip("Наскільки великими будуть трикутники (більше значення = більш грубий Low-Poly)")]
    [Range(1, 10)]
    public int detailSkip = 4; // 4 - ідеально для мапи 1000х1000 з RAW 512

    [Tooltip("Матеріал для нового Low-Poly острова")]
    public Material terrainMaterial;

    [ContextMenu("Generate Low Poly Mesh")]
    public void GenerateLowPoly()
    {
        Terrain terrain = GetComponent<Terrain>();
        TerrainData tData = terrain.terrainData;

        int w = tData.heightmapResolution;
        int h = tData.heightmapResolution;
        Vector3 size = tData.size;

        int vertsWidth = (w - 1) / detailSkip + 1;
        int vertsHeight = (h - 1) / detailSkip + 1;

        // Кожен трикутник повинен мати свої унікальні вершини для Flat Shading
        int numTriangles = (vertsWidth - 1) * (vertsHeight - 1) * 2;
        int numVertices = numTriangles * 3;

        Vector3[] vertices = new Vector3[numVertices];
        int[] triangles = new int[numVertices];
        Vector2[] uvs = new Vector2[numVertices];

        int vIndex = 0;

        for (int y = 0; y < vertsHeight - 1; y++)
        {
            for (int x = 0; x < vertsWidth - 1; x++)
            {
                // Отримуємо координати 4 точок квадрата
                int y0 = y * detailSkip;
                int x0 = x * detailSkip;
                int y1 = (y + 1) * detailSkip;
                int x1 = (x + 1) * detailSkip;

                // Точки у світових координатах
                Vector3 p00 = new Vector3((float)x0 / (w - 1) * size.x, tData.GetHeight(x0, y0), (float)y0 / (h - 1) * size.z);
                Vector3 p10 = new Vector3((float)x1 / (w - 1) * size.x, tData.GetHeight(x1, y0), (float)y0 / (h - 1) * size.z);
                Vector3 p01 = new Vector3((float)x0 / (w - 1) * size.x, tData.GetHeight(x0, y1), (float)y1 / (h - 1) * size.z);
                Vector3 p11 = new Vector3((float)x1 / (w - 1) * size.x, tData.GetHeight(x1, y1), (float)y1 / (h - 1) * size.z);

                // Трикутник 1
                vertices[vIndex] = p00; triangles[vIndex] = vIndex; uvs[vIndex] = new Vector2(0, 0); vIndex++;
                vertices[vIndex] = p01; triangles[vIndex] = vIndex; uvs[vIndex] = new Vector2(0, 1); vIndex++;
                vertices[vIndex] = p10; triangles[vIndex] = vIndex; uvs[vIndex] = new Vector2(1, 0); vIndex++;

                // Трикутник 2
                vertices[vIndex] = p10; triangles[vIndex] = vIndex; uvs[vIndex] = new Vector2(1, 0); vIndex++;
                vertices[vIndex] = p01; triangles[vIndex] = vIndex; uvs[vIndex] = new Vector2(0, 1); vIndex++;
                vertices[vIndex] = p11; triangles[vIndex] = vIndex; uvs[vIndex] = new Vector2(1, 1); vIndex++;
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Дозволяє великі мапи
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;

        // Магія Flat Shading!
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Створюємо новий об'єкт для нашого Low-Poly острова
        GameObject lowPolyObject = new GameObject("LowPoly_Island");
        lowPolyObject.transform.position = terrain.transform.position;

        MeshFilter mf = lowPolyObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = lowPolyObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = terrainMaterial != null ? terrainMaterial : new Material(Shader.Find("Universal Render Pipeline/Lit"));

        lowPolyObject.AddComponent<MeshCollider>();

        // Вимикаємо старий гладкий Terrain
        terrain.enabled = false;
        GameLog.Info("Епічний Low-Poly острів успішно згенеровано!");
    }
}