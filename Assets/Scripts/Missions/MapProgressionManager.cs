using UnityEngine;
using System;
using System.Collections.Generic;

public class MapProgressionManager : MonoBehaviour
{
    public static MapProgressionManager Instance;

    [Header("All Regions Database")]
    public List<RegionData> allRegionsInGame;

    public static event Action OnMapStateChanged;

    // ����̲��ֲ�: ��������� ������ ��� PlayerPrefs, ��� �� ���������� ����� � ���'�� (Garbage)
    private Dictionary<int, string> regionStateKeys = new Dictionary<int, string>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // �������� �� ����� ���� ���
        if (allRegionsInGame != null)
        {
            foreach (var region in allRegionsInGame)
            {
                if (region != null)
                    regionStateKeys[region.regionID] = "RegionState_" + region.regionID;
            }
        }

        SyncMapStatesWithSaves();
    }

    public void SyncMapStatesWithSaves()
    {
        bool needsSave = false;

        foreach (var region in allRegionsInGame)
        {
            int defaultState = (int)region.currentState;
            int savedState = PlayerPrefs.GetInt(regionStateKeys[region.regionID], defaultState);
            region.currentState = (RegionState)savedState;
        }

        foreach (var region in allRegionsInGame)
        {
            if (region.currentState == RegionState.Conquered)
            {
                foreach (RegionData neighbor in region.neighboringRegions)
                {
                    if (neighbor.currentState == RegionState.Locked)
                    {
                        neighbor.currentState = RegionState.Available;
                        neighbor.isNewlyUnlocked = true;

                        // ������������� ����������� ���� ������ "RegionState_" + ID
                        if (regionStateKeys.ContainsKey(neighbor.regionID))
                        {
                            PlayerPrefs.SetInt(regionStateKeys[neighbor.regionID], (int)RegionState.Available);
                        }
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
        PlayerPrefs.SetInt(regionStateKeys[conqueredRegion.regionID], (int)RegionState.Conquered);

        int currentConquered = PlayerPrefs.GetInt("TotalConqueredRegions", 0);
        PlayerPrefs.SetInt("TotalConqueredRegions", currentConquered + 1);
        PlayerPrefs.Save();

        SyncMapStatesWithSaves();
        OnMapStateChanged?.Invoke();

        // First-time hint the very first region falls — teach the
        // neighbour-unlock system so the player realises the map opens
        // outward as they conquer.
        if (currentConquered == 0 && TutorialHints.Instance != null)
        {
            TutorialHints.Instance.ShowIfNew("FirstRegionConquered",
                "Region cleared! Its neighbours are now Available. Chain conquests outward — the map opens as you go.", 6f);
        }
    }

    public void RefreshMapState()
    {
        OnMapStateChanged?.Invoke();
    }
}