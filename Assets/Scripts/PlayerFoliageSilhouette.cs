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
// Put it on the player root. It builds itself at Start.
[DisallowMultipleComponent]
public class PlayerFoliageSilhouette : MonoBehaviour
{
    [Tooltip("Silhouette material. Left empty, one is created from Hollow/PlayerSilhouette.")]
    public Material silhouetteMaterial;

    public Color silhouetteColor = new Color(0.55f, 0.85f, 1f, 0.8f);

    [Tooltip("Renderers whose name contains any of these are skipped — VFX trails, capes and the like read as noise in silhouette.")]
    public string[] skipNameContains = { "trail", "vfx", "particle", "fx_", "aura" };

    [Tooltip("Off by default: shadows from an occlusion silhouette are never wanted.")]
    public bool castShadows = false;

    private readonly List<GameObject> _mirrors = new List<GameObject>();

    private void Start() { Build(); }

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
            var sh = Shader.Find("Hollow/PlayerSilhouette");
            if (sh == null)
            {
                Debug.LogWarning("[PlayerFoliageSilhouette] Shader 'Hollow/PlayerSilhouette' not found — no silhouette. Make sure the shader is in the build (it is referenced by name, so it may need to be in Always Included Shaders for a player build).");
                return;
            }
            silhouetteMaterial = new Material(sh) { name = "M_PlayerSilhouette (runtime)" };
        }
        if (silhouetteMaterial.HasProperty("_SilhouetteColor"))
            silhouetteMaterial.SetColor("_SilhouetteColor", silhouetteColor);

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
