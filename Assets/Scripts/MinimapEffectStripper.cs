using UnityEngine;

// Catch-all so NO particle/trail effect shows on the minimap.
//
// The minimap camera renders the Default layer (terrain/buildings), and most
// VFX spawn on Default too, so they cluttered the map as blips. Rather than
// tagging every one of the dozens of spawn sites, this singleton periodically
// moves every ParticleSystem / TrailRenderer that's still on the Default layer
// onto TransparentFX — which the main camera renders but the minimap camera
// does NOT. Mesh markers on the MinimapOnly layer (enemy dots, the caged-ally
// icon) are left alone, so real minimap icons still show.
public class MinimapEffectStripper : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null) return;
        var go = new GameObject("MinimapEffectStripper");
        s_instance = go.AddComponent<MinimapEffectStripper>();
        DontDestroyOnLoad(go);
    }

    private static MinimapEffectStripper s_instance;

    private int vfxLayer = -1;
    private const int DefaultLayer = 0;
    private float timer;

    private void Awake()
    {
        vfxLayer = LayerMask.NameToLayer("TransparentFX");
    }

    private void Update()
    {
        if (vfxLayer < 0 || vfxLayer == DefaultLayer) return;
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        // 20 Hz — short-lived bursts (hit sparks, muzzle flashes) used to spawn
        // and die inside a single slow 0.35s window and flash on the map the
        // whole time. At 0.05s they're pulled off Default within ~one frame or
        // two, before they read as a blip.
        timer = 0.05f;
        Strip();
    }

    private void Strip()
    {
        // Once moved off Default a system is skipped forever (layer no longer 0),
        // so there's no need to track processed instances.
        var ps = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
        for (int i = 0; i < ps.Length; i++)
        {
            var g = ps[i] != null ? ps[i].gameObject : null;
            if (g != null && g.layer == DefaultLayer) g.layer = vfxLayer;
        }
        var tr = FindObjectsByType<TrailRenderer>(FindObjectsSortMode.None);
        for (int i = 0; i < tr.Length; i++)
        {
            var g = tr[i] != null ? tr[i].gameObject : null;
            if (g != null && g.layer == DefaultLayer) g.layer = vfxLayer;
        }
    }
}
