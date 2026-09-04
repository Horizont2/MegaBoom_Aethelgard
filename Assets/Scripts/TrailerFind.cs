using System.Collections.Generic;
using UnityEngine;

// GameObject.Find only ever returns ACTIVE objects. Every trailer rig spends
// most of its life disabled (the sequence director parks the rigs it isn't
// using), so Find returned null for them — which is why each run of a setup tool
// created ANOTHER "LoreTrailer_Part2_Rig" instead of reusing the one already in
// the scene, and why the runtime director found no rigs to switch between and
// the camera ended up off the map.
//
// Everything that looks a trailer rig up goes through here instead.
public static class TrailerFind
{
    // The scene object named `name`, active or not. When several exist the first
    // in hierarchy order wins (Duplicates() returns the rest).
    public static GameObject ByName(string name)
    {
        GameObject best = null;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || t.gameObject.name != name) continue;
            if (!t.gameObject.scene.IsValid()) continue;          // never a prefab asset
            if (best == null || t.GetSiblingIndex() < best.transform.GetSiblingIndex()) best = t.gameObject;
        }
        return best;
    }

    // Every scene object with this name, active or not.
    public static List<GameObject> AllByName(string name)
    {
        var list = new List<GameObject>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || t.gameObject.name != name) continue;
            if (!t.gameObject.scene.IsValid()) continue;
            list.Add(t.gameObject);
        }
        return list;
    }
}
