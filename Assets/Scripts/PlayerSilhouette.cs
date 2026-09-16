using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Shows the player through foliage they are standing in.
//
// Drop it on the player. Nothing to wire.
//
// ==== TWO HALVES, AND THE LAST ATTEMPT ONLY HAD ONE ====
//
// WHERE the silhouette appears is decided by the depth buffer: the shader's
// ZTest Greater draws only on pixels where something nearer has already been
// written, so the shape is cut out exactly by the leaves in front and stays
// correct while the bush sways. See OccludedSilhouette.shader for why the
// render queue is the thing that makes or breaks that.
//
// WHETHER it appears at all is decided here, and it has to be, because depth
// alone cannot tell a bush from a mountain. Pure ZTest Greater would show the
// player through cliffs and buildings too. So the ghosts are only switched on
// while a probe from the camera to the player actually hits something on the
// foliage layers — the same test CameraOcclusion already uses to fade trees.
//
// ==== WHY IT CLONES THE RENDERERS INSTEAD OF ADDING A MATERIAL ====
//
// Appending the silhouette to Renderer.materials is one line, and it only
// re-draws ONE sub-mesh. The player is a body, armour pieces and a weapon
// across several sub-meshes and several renderers, so most of the character
// would simply be missing from its own silhouette.
//
// A parallel set of renderers sharing the same mesh, bones and transforms draws
// the whole character, every sub-mesh, and is rebuilt when the armour changes.
[DisallowMultipleComponent]
public class PlayerSilhouette : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Left empty, a material is built from the OccludedSilhouette shader at runtime.")]
    public Material silhouetteMaterial;
    public Color silhouetteColour = new Color(0.35f, 0.72f, 1.0f, 0.75f);
    [Tooltip("Seconds-ish to fade in and out. Instant popping reads as a glitch every time it happens.")]
    public float fadeSpeed = 9f;

    [Header("When to show it")]
    [Tooltip("Layers that count as 'something the player can hide in'. Nature and Damageable by default — the same set CameraOcclusion fades.")]
    public LayerMask foliageLayers = (1 << 15) | (1 << 9);
    [Tooltip("Width of the probe from camera to player. Wide enough to catch a bush the player is inside, narrow enough to ignore one they are merely walking past.")]
    public float probeRadius = 0.55f;
    [Tooltip("Ignore hits this close to the camera.")]
    public float cameraNearIgnore = 0.4f;
    [Tooltip("Stop the probe this far short of the player, so their own colliders cannot answer the question.")]
    public float playerNearIgnore = 0.5f;
    [Tooltip("Checks per second. The answer changes about as fast as the player walks, so this does not need the frame rate.")]
    [Range(10f, 60f)] public float checksPerSecond = 25f;

    private readonly List<Renderer> _ghosts = new List<Renderer>(8);
    private Material _runtimeMat;
    private Camera _cam;
    private float _nextCheck;
    private float _alpha;
    private bool _wantVisible;
    private int _sourceCount = -1;
    private float _nextValidate;

    private static readonly RaycastHit[] s_probe = new RaycastHit[12];

    // ==== PAINTED FOLIAGE IS NOT ON THE FOLIAGE LAYER ====
    //
    // This probed foliageLayers alone, which is the Nature layer the generator
    // puts spawned decoration on. That is right for a bush that exists as a
    // GameObject — and most bushes do not.
    //
    // WorldGenerator paints its bushes onto the terrain as tree instances when
    // useTerrainVegetationPainting is on, which it is in GameScene. A painted
    // instance has no GameObject at all: whatever collider it gets belongs to
    // the TERRAIN, and therefore sits on the terrain's layer. So the probe could
    // never see the commonest bush in the game, and the effect only appeared
    // where painting was unavailable and the generator fell back to real objects
    // — which is exactly the difference between ordinary generation and a region
    // capture that was reported.
    //
    // Adding the terrain's layer makes the painted ones visible to the probe.
    // The TerrainCollider itself is rejected in the loop, so the ground still
    // cannot trigger the ghost.
    private int _probeMask = 0;
    private int ProbeMask
    {
        get
        {
            if (_probeMask != 0) return _probeMask;
            _probeMask = foliageLayers;
            Terrain t = Terrain.activeTerrain;
            if (t != null) _probeMask |= 1 << t.gameObject.layer;
            return _probeMask;
        }
    }
    private static readonly int s_colorId = Shader.PropertyToID("_Color");

    private void OnEnable()
    {
        // Subscribe FIRST. Build can switch this component off when the shader
        // is missing, and OnDisable would then unsubscribe a handler that had
        // not been added yet — leaving it subscribed forever once Build was
        // retried.
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        Build();
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        SetGhostsEnabled(false);
    }

    private void OnDestroy()
    {
        Teardown();
        if (_runtimeMat != null) Destroy(_runtimeMat);
    }

    // ==== ONLY THE CAMERA THE PLAYER IS LOOKING THROUGH ====
    //
    // The minimap camera renders the world from directly overhead, where tree
    // canopies are in front of the player almost permanently — so the silhouette
    // would pass its depth test and paint a glowing blob on the map. It is the
    // main camera's effect and nobody else's.
    private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        bool mine = cam == _cam && _alpha > 0.004f;
        SetGhostsEnabled(mine);
    }

    // ==== ONE LOUD REPORT, A FEW SECONDS IN ====
    //
    // Every way this can fail looks identical from the player's seat: no
    // silhouette. The map markers cost three rounds of guessing before a report
    // like this named the cause in one line; this one says it up front.
    private float _reportAt = -1f;
    private bool _reported;

    private void SelfReport()
    {
        if (_reported) return;
        if (_reportAt < 0f) { _reportAt = Time.unscaledTime + 4f; return; }
        if (Time.unscaledTime < _reportAt) return;
        _reported = true;

        var sb = new System.Text.StringBuilder("[Silhouette] SELF-REPORT\n");
        sb.AppendLine($"  ghost renderers built: {_ghosts.Count} (from {_sourceCount} source renderer(s))");
        sb.AppendLine($"  material: {(_runtimeMat != null ? _runtimeMat.shader.name : "NULL — shader not found")}");
        sb.AppendLine($"  main camera: {(_cam != null ? _cam.name : "NULL — nothing is tagged MainCamera")}");
        sb.AppendLine($"  foliage layer mask: {foliageLayers.value} (Nature=1<<15, Damageable=1<<9)");
        sb.AppendLine($"  currently occluded: {_wantVisible}   fade: {_alpha:F2}");

        // The one that actually bites: a probe can only hit a COLLIDER, and the
        // bush prefabs in this project have none at all. Trees do. Say so
        // plainly rather than letting it look like the shader is broken.
        int n = Physics.OverlapSphereNonAlloc(transform.position, 6f, s_probeCols, foliageLayers,
                                              QueryTriggerInteraction.Collide);
        sb.AppendLine($"  colliders on the foliage layers within 6m: {n}");
        if (n == 0)
            sb.AppendLine("  -> Nothing here can be detected. Foliage needs a collider on one of those " +
                          "layers for the probe to see it; the bush prefabs currently have none, so the " +
                          "silhouette will only trigger behind trees.");

        Debug.Log(sb.ToString(), this);
    }

    private static readonly Collider[] s_probeCols = new Collider[24];

    private void Update()
    {
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        SelfReport();

        // The armour system swaps renderers when the player equips something, so
        // a ghost set built once goes stale. Cheap to notice, cheap to redo.
        if (Time.time >= _nextValidate)
        {
            _nextValidate = Time.time + 0.5f;
            if (CountSourceRenderers() != _sourceCount) Build();
        }

        if (Time.time >= _nextCheck)
        {
            _nextCheck = Time.time + 1f / Mathf.Max(10f, checksPerSecond);
            _wantVisible = IsBehindFoliage();
        }

        float target = _wantVisible ? silhouetteColour.a : 0f;
        _alpha = Mathf.MoveTowards(_alpha, target, fadeSpeed * silhouetteColour.a * Time.unscaledDeltaTime);

        if (_runtimeMat != null)
        {
            Color c = silhouetteColour;
            c.a = _alpha;
            _runtimeMat.SetColor(s_colorId, c);
        }
    }

    private bool IsBehindFoliage()
    {
        if (_cam == null) return false;

        Vector3 from = _cam.transform.position;
        Vector3 to = transform.position + Vector3.up * 1.1f;
        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist <= cameraNearIgnore + playerNearIgnore) return false;

        Vector3 dir = delta / dist;
        float span = dist - cameraNearIgnore - playerNearIgnore;

        // TRIGGERS COUNT. Foliage a player can walk through must not be solid,
        // so the only colliders a bush can reasonably carry are triggers — and
        // ignoring them here would mean the probe could never see the one thing
        // this whole effect exists for. Trees are solid and hit either way.
        int n = Physics.SphereCastNonAlloc(from + dir * cameraNearIgnore, probeRadius, dir,
                                           s_probe, span, ProbeMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            var c = s_probe[i].collider;
            if (c == null) continue;
            // The player's own hitboxes sit on a Damageable layer too.
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            // ==== THE GROUND IS NOT A BUSH, BUT A PAINTED BUSH IS ALSO THE TERRAIN ====
            //
            // Rejecting every TerrainCollider outright was my first attempt and
            // it was wrong: Unity reports a painted TREE's collider through the
            // TerrainCollider as well, so throwing those away discards exactly
            // the bushes this mask was widened to catch.
            //
            // Height separates them cleanly instead. A bush standing between the
            // camera and the player is struck around chest height; the ground is
            // struck at or below the player's feet, because the camera looks
            // DOWN at them. So a terrain hit below the ankles is the hill, and
            // anything above it is something growing out of the hill.
            //
            // This also holds whichever way the collider is reported, which
            // matters because that detail is a Unity implementation choice this
            // code should not be betting on.
            if (c is TerrainCollider && s_probe[i].point.y < transform.position.y + 0.35f) continue;
            return true;
        }
        return false;
    }

    // ---- ghost construction -------------------------------------------------

    private int CountSourceRenderers()
    {
        int n = 0;
        foreach (var r in GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || IsGhost(r)) continue;
            if (r is SkinnedMeshRenderer || r is MeshRenderer) n++;
        }
        return n;
    }

    private static bool IsGhost(Renderer r) => r.gameObject.name.EndsWith(GhostSuffix);
    private const string GhostSuffix = " [Silhouette]";

    private void Build()
    {
        Teardown();

        if (_runtimeMat == null)
        {
            Material src = silhouetteMaterial;
            if (src == null)
            {
                Shader sh = Shader.Find("HollowSiege/OccludedSilhouette");
                if (sh == null)
                {
                    Debug.LogWarning("[Silhouette] Shader 'HollowSiege/OccludedSilhouette' not found — " +
                                     "the effect is off. It must be in the project and, for a build, " +
                                     "referenced by a material or listed under Always Included Shaders.", this);
                    enabled = false;
                    return;
                }
                src = new Material(sh);
            }
            // One instance shared by every ghost, so fading is a single SetColor
            // rather than one per renderer. Owned, and destroyed with this object.
            _runtimeMat = new Material(src) { name = "SilhouetteRuntime", hideFlags = HideFlags.HideAndDontSave };
        }

        _sourceCount = 0;
        foreach (var src in GetComponentsInChildren<Renderer>(false))
        {
            if (src == null || IsGhost(src)) continue;

            if (src is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                _sourceCount++;
                MakeGhost(smr);
            }
            else if (src is MeshRenderer mr)
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                _sourceCount++;
                MakeGhost(mr, mf);
            }
        }

        SetGhostsEnabled(false);
    }

    private GameObject NewGhostObject(Transform src)
    {
        var go = new GameObject(src.name + GhostSuffix);
        go.layer = src.gameObject.layer;
        go.transform.SetParent(src.parent, false);
        go.transform.localPosition = src.localPosition;
        go.transform.localRotation = src.localRotation;
        go.transform.localScale = src.localScale;
        return go;
    }

    private void MakeGhost(SkinnedMeshRenderer src)
    {
        var go = NewGhostObject(src.transform);
        var dst = go.AddComponent<SkinnedMeshRenderer>();

        dst.sharedMesh = src.sharedMesh;
        // The SAME bone array, so the ghost is skinned by the animation that is
        // already running. No second animator, no sync to get wrong.
        dst.bones = src.bones;
        dst.rootBone = src.rootBone;
        dst.localBounds = src.localBounds;
        dst.updateWhenOffscreen = src.updateWhenOffscreen;
        dst.quality = SkinQuality.Bone2;   // it is a flat silhouette; four weights buy nothing

        Finish(dst, src.sharedMesh.subMeshCount);
    }

    private void MakeGhost(MeshRenderer src, MeshFilter srcFilter)
    {
        var go = NewGhostObject(src.transform);
        go.AddComponent<MeshFilter>().sharedMesh = srcFilter.sharedMesh;
        var dst = go.AddComponent<MeshRenderer>();
        Finish(dst, srcFilter.sharedMesh.subMeshCount);
    }

    private void Finish(Renderer dst, int subMeshCount)
    {
        // Every sub-mesh, or the silhouette has holes where the armour is.
        var mats = new Material[Mathf.Max(1, subMeshCount)];
        for (int i = 0; i < mats.Length; i++) mats[i] = _runtimeMat;
        dst.sharedMaterials = mats;

        dst.shadowCastingMode = ShadowCastingMode.Off;
        dst.receiveShadows = false;
        dst.lightProbeUsage = LightProbeUsage.Off;
        dst.reflectionProbeUsage = ReflectionProbeUsage.Off;
        dst.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        dst.allowOcclusionWhenDynamic = false;

        _ghosts.Add(dst);
    }

    private void SetGhostsEnabled(bool on)
    {
        for (int i = 0; i < _ghosts.Count; i++)
            if (_ghosts[i] != null && _ghosts[i].enabled != on) _ghosts[i].enabled = on;
    }

    private void Teardown()
    {
        for (int i = 0; i < _ghosts.Count; i++)
            if (_ghosts[i] != null) Destroy(_ghosts[i].gameObject);
        _ghosts.Clear();
    }
}
