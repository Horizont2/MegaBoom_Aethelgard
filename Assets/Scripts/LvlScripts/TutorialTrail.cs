using UnityEngine;
using System.Collections.Generic;

// Animated dashed trail on the ground that leads the player toward a target.
// Spawns a pool of quad "dashes" at world positions between the player and the
// current target, terrain-sampled so they follow the rolling hills. Each dash
// pulses in an offset wave so the whole sequence reads as "flowing" toward the
// goal — like the God of War / Fortnite tutorial trails.
//
// Usage:
//   - Assign dashPrefab (a small textured quad, "Trail_Dash" material).
//   - Call SetTarget(transform) to point the trail at any Transform.
//   - Call Hide() to fade it out.
// Positions are recomputed on the fly, so a moving target (or player) is fine.
[DisallowMultipleComponent]
public class TutorialTrail : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Quad prefab used for each dash. Should have a Renderer with an emissive/unlit material and lie flat on the ground.")]
    public GameObject dashPrefab;
    [Tooltip("Player transform. Auto-found from tag \"Player\" if left empty.")]
    public Transform player;

    [Header("Layout")]
    [Tooltip("Metres between each dash centre.")]
    public float spacing = 1.8f;
    [Tooltip("Skip the first N metres of the path so the trail doesn't overlap the player.")]
    public float startGap = 2.0f;
    [Tooltip("Skip the last N metres so the last dash doesn't clip into the target model.")]
    public float endGap = 1.5f;
    [Tooltip("Maximum length of the visible trail — dashes past this cap far from the player are not rendered.")]
    public float maxLength = 40f;
    [Tooltip("Height (metres) above the terrain each dash floats at.")]
    public float heightOffset = 0.08f;
    [Tooltip("How much a dash grows during its pulse peak.")]
    public float pulseScale = 1.25f;

    [Header("Animation")]
    [Tooltip("Metres per second the pulse wave travels along the trail. Higher = faster flow toward the target.")]
    public float flowSpeed = 6f;
    [Tooltip("Width in metres of the visible peak of each pulse. Narrower peaks give a punchier chase animation.")]
    public float pulseWidth = 4f;
    [Tooltip("Colour of dashes at rest.")]
    public Color baseColor = new Color(1f, 0.85f, 0.35f, 0.35f);
    [Tooltip("Colour of dashes at their pulse peak.")]
    public Color peakColor = new Color(1f, 0.95f, 0.55f, 1f);

    [Header("Behaviour")]
    [Tooltip("Distance from the target at which the trail auto-hides — no need to draw a path once you're standing on it.")]
    public float arriveDistance = 3.5f;
    [Tooltip("Refresh cadence for the path (seconds). Trail geometry rebuilds this often; animation runs every frame regardless.")]
    public float rebuildInterval = 0.15f;

    private Transform target;
    private readonly List<Transform> dashes = new List<Transform>();
    private readonly List<Renderer> dashRenderers = new List<Renderer>();
    private float rebuildTimer;
    private float animTime;
    private bool visible;
    private MaterialPropertyBlock mpb;
    private static readonly int s_colorId = Shader.PropertyToID("_BaseColor");
    private static readonly int s_colorLegacy = Shader.PropertyToID("_Color");

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
    }

    public void SetTarget(Transform t)
    {
        target = t;
        visible = t != null;
        rebuildTimer = 0f;
        SetActiveAll(visible);
    }

    public void Hide()
    {
        visible = false;
        target = null;
        SetActiveAll(false);
    }

    private void Update()
    {
        if (!visible || target == null) return;

        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
            if (player == null) return;
        }

        // Auto-hide when the player is standing next to the goal — reduces
        // clutter and signals the player has arrived without needing a
        // separate "objective reached" pop-up.
        float distToTarget = Vector3.Distance(new Vector3(player.position.x, 0, player.position.z),
                                              new Vector3(target.position.x, 0, target.position.z));
        if (distToTarget < arriveDistance)
        {
            SetActiveAll(false);
            return;
        }

        rebuildTimer -= Time.deltaTime;
        if (rebuildTimer <= 0f)
        {
            rebuildTimer = rebuildInterval;
            RebuildPath();
        }

        animTime += Time.deltaTime * flowSpeed;
        AnimateDashes();
    }

    private void RebuildPath()
    {
        Vector3 from = player.position;
        Vector3 to = target.position;
        Vector3 dir = to - from;
        dir.y = 0;
        float total = dir.magnitude;
        if (total <= startGap + endGap)
        {
            SetActiveAll(false);
            return;
        }
        dir /= total;

        float usable = Mathf.Min(total, maxLength) - startGap - endGap;
        if (usable <= 0f)
        {
            SetActiveAll(false);
            return;
        }

        int needed = Mathf.Max(1, Mathf.FloorToInt(usable / spacing) + 1);
        EnsurePool(needed);

        Quaternion faceForward = Quaternion.LookRotation(dir);
        Vector3 rotEuler = faceForward.eulerAngles;
        // Lie flat on the ground: rotate to face along the path but pitch 90
        // so the quad is horizontal.
        Quaternion flatRot = Quaternion.Euler(90f, rotEuler.y, 0f);

        for (int i = 0; i < dashes.Count; i++)
        {
            Transform dash = dashes[i];
            if (i >= needed)
            {
                if (dash.gameObject.activeSelf) dash.gameObject.SetActive(false);
                continue;
            }

            float d = startGap + i * spacing;
            Vector3 pos = from + dir * d;
            pos.y = SampleGroundY(pos) + heightOffset;

            if (!dash.gameObject.activeSelf) dash.gameObject.SetActive(true);
            dash.position = pos;
            dash.rotation = flatRot;
        }
    }

    private void AnimateDashes()
    {
        for (int i = 0; i < dashes.Count; i++)
        {
            Transform dash = dashes[i];
            if (!dash.gameObject.activeSelf) continue;

            // Each dash has a phase offset based on its index. Subtracting
            // animTime makes the peak WALK from the player toward the target
            // (increasing i = further from player = later in wave).
            float phase = i * spacing - animTime;
            phase = Mathf.Repeat(phase, pulseWidth * 2f) - pulseWidth;
            // Smoothstep peak at phase=0.
            float k = 1f - Mathf.Clamp01(Mathf.Abs(phase) / pulseWidth);
            k = k * k * (3f - 2f * k);

            float scale = Mathf.Lerp(1f, pulseScale, k);
            dash.localScale = new Vector3(scale, scale, scale) * dashBaseScale;

            Color c = Color.Lerp(baseColor, peakColor, k);
            Renderer r = dashRenderers[i];
            if (r != null)
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor(s_colorId, c);
                mpb.SetColor(s_colorLegacy, c);
                r.SetPropertyBlock(mpb);
            }
        }
    }

    private float dashBaseScale = 1f;

    private void EnsurePool(int needed)
    {
        if (dashPrefab == null) return;
        while (dashes.Count < needed)
        {
            GameObject go = Instantiate(dashPrefab, transform);
            Transform t = go.transform;
            dashes.Add(t);
            dashRenderers.Add(go.GetComponentInChildren<Renderer>());
            if (dashes.Count == 1) dashBaseScale = t.localScale.x;
        }
    }

    private void SetActiveAll(bool on)
    {
        for (int i = 0; i < dashes.Count; i++)
            if (dashes[i] != null && dashes[i].gameObject.activeSelf != on)
                dashes[i].gameObject.SetActive(on);
    }

    private static float SampleGroundY(Vector3 worldPos)
    {
        Terrain t = Terrain.activeTerrain;
        if (t == null) return worldPos.y;
        return t.SampleHeight(worldPos) + t.transform.position.y;
    }
}
