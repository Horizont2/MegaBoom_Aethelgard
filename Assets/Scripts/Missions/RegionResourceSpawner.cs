using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Used to pop collectable resource icons onto conquered regions on the map.
// It does not any more, and the component stays only so the scene references
// that point at it keep resolving.
//
// ==== THE REGIONS WERE PAYING TWICE ====
//
// CampEconomyManager already accrues every conquered region's per-hour yield
// from exactly the same numbers - RegionData.upgradeLevels[level-1].passiveWood
// and friends - and flushes it to the stash on a timer. These icons read the
// same fields and paid out again on click, so a region's income depended on
// whether the player happened to be looking at the map and how often they
// clicked. The same resources, granted twice, through two systems that did not
// know about each other.
//
// One of them had to go, and it is this one: clicking icons on a strategy map
// is busywork that the player cannot lose by ignoring, only gain by grinding.
// The income arrives on its own now, with a line saying where it came from.
public class RegionResourceSpawner : MonoBehaviour
{
    [Header("References")]
    public RegionData myRegionData;
    public RectTransform spawnArea;

    [Header("Resource Prefabs")]
    public GameObject woodPrefab;
    public GameObject stonePrefab;
    public GameObject foodPrefab;
    public GameObject diamondPrefab;

    [Header("Spawning Settings")]
    public float spawnIntervalMin = 20f;
    public float spawnIntervalMax = 45f;
    [Tooltip("Радіус розбросу іконок, щоб вони не злипалися і не вилазили за регіон")]
    public float scatterRadius = 40f;

    void Start()
    {
        // Sweep up anything a previous version left standing, then stand down.
        foreach (var node in GetComponentsInChildren<MapResourceNode>(true))
            if (node != null) Destroy(node.gameObject);
        if (spawnArea != null)
            foreach (var node in spawnArea.GetComponentsInChildren<MapResourceNode>(true))
                if (node != null) Destroy(node.gameObject);

        enabled = false;
    }

}
