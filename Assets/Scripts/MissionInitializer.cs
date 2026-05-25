using UnityEngine;

public class MissionInitializer : MonoBehaviour
{
    public static MissionInitializer Instance;

    // --- ФІКС: Ця змінна живе між сценами і приймає дані з Мапи ---
    public static RegionData PendingMissionRegion;

    [Header("Debug / Testing")]
    [Tooltip("Перетягни сюди будь-який RegionData, щоб тестувати сцену без запуску Мапи")]
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
        // Перевіряємо, чи ми зайшли з мапи, АБО чи є тестовий файл
        if (PlayerPrefs.GetInt("IsRegionMission", 0) == 1 || testFallbackRegion != null)
        {
            SetupMission();
        }
        else
        {
            Debug.Log("<color=yellow>[Mission]</color> Звичайний запуск сцени (не з Мапи і без тестового регіону).");
        }
    }

    private void SetupMission()
    {
        RegionData activeRegion = null;

        // 1. ПЕРЕВІРКА СТАТИКИ: Чи передала нам Мапа якийсь регіон?
        if (PendingMissionRegion != null)
        {
            activeRegion = PendingMissionRegion;

            // Записуємо його в новий GameManager, щоб EnemyAI міг його прочитати
            if (GameManager.Instance != null) GameManager.Instance.currentRegion = activeRegion;
        }
        // 2. Якщо статика пуста, але GameManager чомусь має дані (резерв)
        else if (GameManager.Instance != null && GameManager.Instance.currentRegion != null)
        {
            activeRegion = GameManager.Instance.currentRegion;
        }
        // 3. Тестовий режим для зручної розробки в Unity
        else if (testFallbackRegion != null)
        {
            Debug.LogWarning("<color=orange>[Mission]</color> Запуск без Мапи. Використовуємо ТЕСТОВИЙ регіон: " + testFallbackRegion.regionName);
            activeRegion = testFallbackRegion;

            if (GameManager.Instance != null) GameManager.Instance.currentRegion = activeRegion;
            PlayerPrefs.SetInt("RegionBiomeType", (int)activeRegion.regionBiome);
            PlayerPrefs.SetInt("IsRegionMission", 1);
        }
        else
        {
            Debug.LogError("[Mission] RegionData не знайдено! Перевірте чи працює передача з мапи.");
            return;
        }

        currentRegionName = activeRegion.regionName;
        currentBiomeIndex = PlayerPrefs.GetInt("RegionBiomeType", 0);

        Debug.Log($"<color=#00FF00>[Mission]</color> Генерація місії: {currentRegionName}. Біом ID: {currentBiomeIndex}");

        // ІНТЕГРАЦІЯ З SmartSeasonManager (Налаштовуємо небо, світло і туман)
        // SmartSeasonManager seasonManager = FindFirstObjectByType<SmartSeasonManager>();
        // if (seasonManager != null) seasonManager.LockSeasonForMission(currentBiomeIndex);
    }
}