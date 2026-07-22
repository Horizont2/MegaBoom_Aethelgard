using UnityEngine;

public class BiomeWeather : MonoBehaviour
{
    [Header("References")]
    public Terrain terrain;

    [Header("Weather Particle Systems")]
    public ParticleSystem leavesEffect;   // ��� ���� 0 (�����)
    public ParticleSystem sandEffect;     // ��� ���� 1 (ϳ���)
    public ParticleSystem snowEffect;     // ��� ���� 2 (����)
    // ���� (��� 3) �������� ��� ������

    private int currentDominantTexture = -1;

    private void Start()
    {
        if (terrain == null) terrain = Terrain.activeTerrain;
    }

    // Throttle the biome check — the dominant texture under the player
    // only changes when they cross a biome boundary, and each check
    // allocates a float[,,] via terrainData.GetAlphamaps. 0.5s cadence
    // is imperceptible and drops the GC pressure to nothing.
    private float sampleTimer = 0f;
    private const float SAMPLE_INTERVAL = 0.5f;

    private void Update()
    {
        if (terrain == null) return;
        sampleTimer -= Time.deltaTime;
        if (sampleTimer > 0f) return;
        sampleTimer = SAMPLE_INTERVAL;

        int dominantTexture = GetDominantTerrainTexture(transform.position);
        if (dominantTexture != currentDominantTexture)
        {
            currentDominantTexture = dominantTexture;
            UpdateWeatherEffects(currentDominantTexture);
        }
    }

    private void UpdateWeatherEffects(int textureIndex)
    {
        // �������� �������� �� ������
        if (leavesEffect != null) leavesEffect.Stop();
        if (sandEffect != null) sandEffect.Stop();
        if (snowEffect != null) snowEffect.Stop();

        // ������� ���, ���� ������� ����
        if (textureIndex == 0 && leavesEffect != null) leavesEffect.Play();      // �����
        else if (textureIndex == 1 && sandEffect != null) sandEffect.Play(); // ϳ���
        else if (textureIndex == 2 && snowEffect != null) snowEffect.Play(); // ����
    }

    // --- ��ò� ������� ���˲ ---
    private int GetDominantTerrainTexture(Vector3 worldPos)
    {
        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;

        // ���������� ���������� ���� � ���������� ����� �������
        int mapX = Mathf.RoundToInt(((worldPos.x - terrainPos.x) / terrainData.size.x) * terrainData.alphamapWidth);
        int mapZ = Mathf.RoundToInt(((worldPos.z - terrainPos.z) / terrainData.size.z) * terrainData.alphamapHeight);

        // ������ �� ������ �� ���
        if (mapX < 0 || mapZ < 0 || mapX >= terrainData.alphamapWidth || mapZ >= terrainData.alphamapHeight)
            return 0;

        // �������� "����" ����� ����� � ��� �����
        float[,,] splatmapData = terrainData.GetAlphamaps(mapX, mapZ, 1, 1);
        float[] cellMix = new float[splatmapData.GetUpperBound(2) + 1];

        for (int i = 0; i < cellMix.Length; i++)
        {
            cellMix[i] = splatmapData[0, 0, i];
        }

        // ��������� ��������, ��� ��� ��������
        float maxMix = 0;
        int maxIndex = 0;
        for (int i = 0; i < cellMix.Length; i++)
        {
            if (cellMix[i] > maxMix)
            {
                maxIndex = i;
                maxMix = cellMix[i];
            }
        }

        return maxIndex; // ������� 0, 1, 2 ��� 3
    }
}