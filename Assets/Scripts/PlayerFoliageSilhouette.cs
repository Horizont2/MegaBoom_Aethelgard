using System.Collections.Generic;
using UnityEngine;

// Keeps the player readable inside bushes.
//
// The bushes are terrain-painted details: GPU instances with no GameObject and
// no collider, so CameraOcclusion — which fades occluding objects carrying a
// FadingObject component — cannot see them and never fades them. Walk into a
// patch and the player simply disappears.
//
// Instead of detecting the occluder, this mirrors the player's renderers into a
// second set that draws with ZTest Greater: it is visible ONLY where something
// is already in front of the player, and renders nothing at all when they are in
// the open. Foliage, rocks, walls — anything that writes depth — all work, with
// no per-frame line-of-sight test.
//
// Needs PlayerSilhouetteFeature on the active URP renderer. The mirror meshes
// carry a shader whose only pass is tagged LightMode = "PlayerSilhouette", which
// no built-in URP pass knows — so without the feature they draw nothing at all,
// and the old failure where the whole character turned into a solid blue figure
// cannot come back.
[DisallowMultipleComponent]
public class PlayerFoliageSilhouette : MonoBehaviour
{
    [Tooltip("OFF until the depth direction below is confirmed. Two attempts drew the silhouette over the WHOLE character instead of only through occluders, which is worse than the problem it solves, so it stays off by default rather than shipping another guess.")]
    public bool enableSilhouette = false;

    [Tooltip("Which comparison counts as 'something is already in front of the player'. Greater is the textbook answer; if the character comes out solid everywhere, it is Less in this setup. Flip it in Play mode to find out in one go.")]
    public UnityEngine.Rendering.CompareFunction depthTest = UnityEngine.Rendering.CompareFunction.Greater;

    [Tooltip("Silhouette material. Left empty, one is created from Hollow/PlayerSilhouette.")]
    public Material silhouetteMaterial;

    public Color silhouetteColor = new Color(0.55f, 0.85f, 1f, 0.8f);

    [Tooltip("Renderers whose name contains any of these are skipped — VFX trails, capes and the like read as noise in silhouette.")]
    public string[] skipNameContains = { "trail", "vfx", "particle", "fx_", "aura" };

    [Tooltip("Off by default: shadows from an occlusion silhouette are never wanted.")]
    public bool castShadows = false;

    private readonly List<GameObject> _mirrors = new List<GameObject>();

    private void Start()
    {
        if (!enableSilhouette) return;
        Build();
        WarnIfFeatureMissing();
    }

    // Live-editable so the depth direction can be settled without a rebuild.
    private void OnValidate()
    {
        if (silhouetteMaterial != null && silhouetteMaterial.HasProperty("_ZTest"))
            silhouetteMaterial.SetFloat("_ZTest", (float)depthTest);
    }

    // The mirrors are invisible without the render feature, which would look like
    // the component silently doing nothing — say so instead.
    private void WarnIfFeatureMissing()
    {
        if (_mirrors.Count == 0) return;
        var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        if (urp == null) return;
        if (Object.FindObjectsByType<PlayerSilhouetteFeature>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0) return;
        Debug.Log("[PlayerFoliageSilhouette] Silhouette meshes built. They render only via PlayerSilhouetteFeature — add it to the renderer in use (Assets/Settings/PC_Renderer.asset ▸ Add Renderer Feature ▸ Player Silhouette) if the player still disappears in bushes.");
    }

    private void OnDestroy()
    {
        foreach (var g in _mirrors) if (g != null) Destroy(g);
        _mirrors.Clear();
    }

    public void SetVisible(bool on)
    {
        foreach (var g in _mirrors) if (g != null) g.SetActive(on);
    }

    private void Build()
    {
        if (silhouetteMaterial == null)
        {
            // Loaded from Resources, not Shader.Find alone: a shader that no
            // material in any scene references is stripped from a player build,
            // and this material is created at runtime. Anything under Resources
            // is always included, so no Graphics-settings entry is needed.
            var sh = Resources.Load<Shader>("Shaders/PlayerSilhouette") ?? Shader.Find("Hollow/PlayerSilhouette");
            if (sh == null)
            {
                Debug.LogWarning("[PlayerFoliageSilhouette] Shader 'Hollow/PlayerSilhouette' not found at Assets/Resources/Shaders — no silhouette.");
                return;
            }
            silhouetteMaterial = new Material(sh) { name = "M_PlayerSilhouette (runtime)" };
        }
        if (silhouetteMaterial.HasProperty("_SilhouetteColor"))
            silhouetteMaterial.SetColor("_SilhouetteColor", silhouetteColor);
        if (silhouetteMaterial.HasProperty("_ZTest"))
            silhouetteMaterial.SetFloat("_ZTest", (float)depthTest);

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !ShouldMirror(r)) continue;

            var go = new GameObject(r.name + " (Silhouette)");
            go.transform.SetParent(r.transform.parent, false);
            go.transform.localPosition = r.transform.localPosition;
            go.transform.localRotation = r.transform.localRotation;
            go.transform.localScale = r.transform.localScale;
            go.layer = r.gameObject.layer;

            if (r is SkinnedMeshRenderer src)
            {
                // Share the mesh and the ORIGINAL bones, so the silhouette is
                // skinned by the same animation with no extra work.
                var dst = go.AddComponent<SkinnedMeshRenderer>();
                dst.sharedMesh = src.sharedMesh;
                dst.bones = src.bones;
                dst.rootBone = src.rootBone;
                dst.localBounds = src.localBounds;
                dst.updateWhenOffscreen = false;
                dst.quality = SkinQuality.Bone2;      // silhouette needs no fine skinning
                Apply(dst);
            }
            else if (r is MeshRenderer)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) { Destroy(go); continue; }
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                Apply(go.AddComponent<MeshRenderer>());
            }
            else { Destroy(go); continue; }

            _mirrors.Add(go);
        }

        if (_mirrors.Count == 0)
            Debug.LogWarning($"[PlayerFoliageSilhouette] No renderers mirrored on '{name}'.");
    }

    private void Apply(Renderer r)
    {
        r.sharedMaterial = silhouetteMaterial;
        r.shadowCastingMode = castShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        r.allowOcclusionWhenDynamic = false;
    }

    private bool ShouldMirror(Renderer r)
    {
        if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) return false;
        if (r.GetComponentInParent<PlayerFoliageSilhouette>() != this) return false;
        string n = r.name.ToLowerInvariant();
        if (skipNameContains != null)
            foreach (var s in skipNameContains)
                if (!string.IsNullOrEmpty(s) && n.Contains(s)) return false;
        return true;
    }
}
