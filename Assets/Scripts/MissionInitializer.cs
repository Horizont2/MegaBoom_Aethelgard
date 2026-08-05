using UnityEngine;

public class MissionInitializer : MonoBehaviour
{
    public static MissionInitializer Instance;

    // --- Բ��: �� ����� ���� �� ������� � ������ ���� � ���� ---
    public static RegionData PendingMissionRegion;

    [Header("Debug / Testing")]
    [Tooltip("��������� ���� ����-���� RegionData, ��� ��������� ����� ��� ������� ����")]
    public RegionData testFallbackRegion;

    [Header("Debug Info (Read Only)")]
    public string currentRegionName;
    public int currentBiomeIndex;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        // ����������, �� �� ������ � ����, ��� �� � �������� ����
        if (PlayerPrefs.GetInt("IsRegionMission", 0) == 1 || testFallbackRegion != null)
        {
            SetupMission();
        }
        else
        {
            Debug.Log("<color=yellow>[Mission]</color> ��������� ������ ����� (�� � ���� � ��� ��������� ������).");
        }
    }

    private void SetupMission()
    {
        RegionData activeRegion = null;

        // 1. ����²��� �������: �� �������� ��� ���� ������ �����?
        if (PendingMissionRegion != null)
        {
            activeRegion = PendingMissionRegion;

            // �������� ���� � ����� GameManager, ��� EnemyAI �� ���� ���������
            if (GameManager.Instance != null) GameManager.Instance.currentRegion = activeRegion;
        }
        // 2. ���� ������� �����, ��� GameManager ������ �� ���� (������)
        else if (GameManager.Instance != null && GameManager.Instance.currentRegion != null)
        {
            activeRegion = GameManager.Instance.currentRegion;
        }
        // 3. �������� ����� ��� ������ �������� � Unity
        else if (testFallbackRegion != null)
        {
            Debug.LogWarning("<color=orange>[Mission]</color> ������ ��� ����. ������������� �������� �����: " + testFallbackRegion.regionName);
            activeRegion = testFallbackRegion;

            if (GameManager.Instance != null) GameManager.Instance.currentRegion = activeRegion;
            PlayerPrefs.SetInt("RegionBiomeType", (int)activeRegion.regionBiome);
            PlayerPrefs.SetInt("IsRegionMission", 1);
        }
        else
        {
            Debug.LogError("[Mission] RegionData �� ��������! �������� �� ������ �������� � ����.");
            return;
        }

        currentRegionName = activeRegion.regionName;
        currentBiomeIndex = PlayerPrefs.GetInt("RegionBiomeType", 0);

        Debug.Log($"<color=#00FF00>[Mission]</color> ��������� ��: {currentRegionName}. ���� ID: {currentBiomeIndex}");

        // Start the per-run scoreboard (drives the death recap panel).
        // Reset counters + timer here so the recap shows THIS run's
        // stats, not the accumulated career values.
        RunSession.Begin();

        // �������ֲ� � SmartSeasonManager (����������� ����, ����� � �����)
        // SmartSeasonManager seasonManager = FindFirstObjectByType<SmartSeasonManager>();
        // if (seasonManager != null) seasonManager.LockSeasonForMission(currentBiomeIndex);
    }
}