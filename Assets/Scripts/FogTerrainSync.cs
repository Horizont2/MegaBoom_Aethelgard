using System.Reflection;
using UnityEngine;

// Makes the volumetric fog re-measure the terrain once the world exists.
//
// ==== THE FOG IS FITTED TO A TERRAIN THAT IS NO LONGER THERE ====
//
// Pure Volumetric Fog decides where the ground is by baking the terrain's
// heightmap into a texture and caching the terrain's size and maximum height.
// It does that when it wakes up — which, in the game scene, is before
// WorldGenerator has done anything.
//
// The generator then rewrites the entire heightmap AND changes the terrain's
// vertical range outright:
//
//     terrainData.SetHeights(0, 0, heights);
//     terrainData.size = new Vector3(size.x, depth, size.z);
//
// So the fog spends the whole session fitted to the flat placeholder the scene
// shipped with. Its ground reference, its height limits and its rejection
// ceiling all describe land that was replaced seconds after it looked. On the
// low middle of a generated map that reads exactly as the fog failing to settle
// onto the ground while it still hangs correctly out at the raised rim.
//
// rebuildWhenTerrainChanges is on and Unity does fire a heightmap callback, but
// it does not fire for a change to terrainData.size, and the generator changes
// both. One explicit Refresh when generation reports finished settles it for
// certain rather than depending on which of the two the asset happened to
// notice.
//
// Reached by reflection, like every other touch of this asset in the project:
// the fog lives in its own assembly and a hard reference would stop this file
// compiling for anyone who removes the package.
public static class FogTerrainSync
{
    private static bool s_hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        if (s_hooked) return;
        s_hooked = true;
        WorldGenerator.OnWorldGenerationComplete += Resync;
    }

    private static void Resync()
    {
        System.Type t = System.Type.GetType("BKPureNature.PureVolumetricFog, BKPureNature.PureVolumetricFog");
        if (t == null) return;

        var fog = Object.FindFirstObjectByType(t) as Component;
        if (fog == null) return;

        try
        {
            // FindActiveTerrains first: the generator can replace the terrain
            // object as well as its data, and a Refresh that re-measures the
            // wrong terrain is no better than not refreshing at all.
            MethodInfo find = t.GetMethod("FindActiveTerrains", BindingFlags.Instance | BindingFlags.Public);
            if (find != null) find.Invoke(fog, null);

            MethodInfo refresh = t.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.Public);
            if (refresh == null)
            {
                Debug.LogWarning("[FogTerrainSync] PureVolumetricFog has no Refresh() any more — the fog will stay " +
                                 "fitted to the terrain as it was before the world was generated.");
                return;
            }
            refresh.Invoke(fog, null);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[FogTerrainSync] Could not re-fit the fog to the generated terrain: " + e.Message);
        }
    }
}
