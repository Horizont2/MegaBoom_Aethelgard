using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class CampEconomyManager : MonoBehaviour
{
    [Header("Economy Settings")]
    public float resourceTickInterval = 60f;

    // ОПТИМІЗАЦІЯ: Кешування ключів
    private Dictionary<int, string> regionLevelKeys = new Dictionary<int, string>();

    private void Start()
    {
        // Ініціалізуємо ключі
        if (MapProgressionManager.Instance != null && MapProgressionManager.Instance.allRegionsInGame != null)
        {
            foreach (var region in MapProgressionManager.Instance.allRegionsInGame)
            {
                if (region != null)
                {
                    regionLevelKeys[region.regionID] = "RegionLevel_" + region.regionID;
                }
            }
        }

        StartCoroutine(EconomyTickRoutine());
    }

    private IEnumerator EconomyTickRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(resourceTickInterval);
            CollectPassiveIncome();
        }
    }

    private void CollectPassiveIncome()
    {
        if (MapProgressionManager.Instance == null) return;

        int totalWood = 0, totalStone = 0, totalFood = 0, totalDiamonds = 0;

        foreach (RegionData region in MapProgressionManager.Instance.allRegionsInGame)
        {
            if (region.currentState == RegionState.Conquered)
            {
                // Використовуємо кешований рядок без аллокації
                string key = regionLevelKeys.ContainsKey(region.regionID) ? regionLevelKeys[region.regionID] : "RegionLevel_" + region.regionID;

                int currentLevel = PlayerPrefs.GetInt(key, 1);

                if (region.upgradeLevels != null && region.upgradeLevels.Length >= currentLevel)
                {
                    RegionLevelData levelData = region.upgradeLevels[currentLevel - 1];

                    totalWood += levelData.passiveWood;
                    totalStone += levelData.passiveStone;
                    totalFood += levelData.passiveFood;
                    totalDiamonds += levelData.passiveDiamonds;
                }
            }
        }

        // ТУТ БУДЕ ВАШ КОД ДОДАВАННЯ РЕСУРСІВ (наприклад, звернення до ResourceManager)
        // ResourceManager.Instance.AddWood(totalWood);

        Debug.Log($"[Economy] Зібрано пасивний дохід: Дерево +{totalWood}, Камінь +{totalStone}, Їжа +{totalFood}, Діаманти +{totalDiamonds}");
    }
}