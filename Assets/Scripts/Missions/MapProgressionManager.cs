using UnityEngine;
using System;
using System.Collections.Generic;

public class MapProgressionManager : MonoBehaviour
{
    public static MapProgressionManager Instance;

    [Header("All Regions Database")]
    public List<RegionData> allRegionsInGame;

    public static event Action OnMapStateChanged;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        SyncMapStatesWithSaves(); // Завантажуємо збереження під час запуску сцени!
    }

    public void SyncMapStatesWithSaves()
    {
        bool needsSave = false;

        // 1. Завантажуємо стани всіх регіонів з PlayerPrefs
        foreach (var region in allRegionsInGame)
        {
            // Беремо дефолтне значення, щоб не зламати стартовий стан першого регіону
            int defaultState = (int)region.currentState;
            int savedState = PlayerPrefs.GetInt("RegionState_" + region.regionID, defaultState);
            region.currentState = (RegionState)savedState;
        }

        // 2. АВТОМАТИЧНО відкриваємо сусідів для ВСІХ захоплених регіонів.
        // Це необхідно, бо захоплення відбувається в GameScene (де немає цього скрипта),
        // і коли гравець повертається в табір, ми маємо перевірити, чи не треба відкрити нові землі.
        foreach (var region in allRegionsInGame)
        {
            if (region.currentState == RegionState.Conquered)
            {
                foreach (RegionData neighbor in region.neighboringRegions)
                {
                    if (neighbor.currentState == RegionState.Locked)
                    {
                        neighbor.currentState = RegionState.Available;
                        neighbor.isNewlyUnlocked = true; // Тригерить анімацію розсіювання бурі
                        PlayerPrefs.SetInt("RegionState_" + neighbor.regionID, (int)RegionState.Available);
                        needsSave = true;
                    }
                }
            }
        }

        if (needsSave) PlayerPrefs.Save();
    }

    public void ConquerRegionAndUnlockNeighbors(RegionData conqueredRegion)
    {
        if (conqueredRegion.currentState == RegionState.Conquered) return;

        conqueredRegion.currentState = RegionState.Conquered;
        PlayerPrefs.SetInt("RegionState_" + conqueredRegion.regionID, (int)RegionState.Conquered);

        int currentConquered = PlayerPrefs.GetInt("TotalConqueredRegions", 0);
        PlayerPrefs.SetInt("TotalConqueredRegions", currentConquered + 1);
        PlayerPrefs.Save();

        // Перевіряємо та відкриваємо сусідів
        SyncMapStatesWithSaves();
        OnMapStateChanged?.Invoke();
    }

    public void RefreshMapState()
    {
        OnMapStateChanged?.Invoke();
    }
}