using UnityEngine;
using System.Collections.Generic;

public class CameraOcclusion : MonoBehaviour
{
    [Header("Target & Scanning")]
    public Transform playerTarget;
    public LayerMask foliageLayer;
    [Tooltip("Width of the line-of-sight sphere cast. Keep small (~0.5). Larger values fade trees that are merely *near* the line, not blocking it.")]
    public float raycastRadius = 0.5f;
    [Tooltip("Don't fade hits closer than this to the player. Stops trees the player is brushing against from going transparent.")]
    public float playerProximityIgnore = 1.6f;
    [Tooltip("Don't fade hits closer than this to the camera (e.g. handheld weapon, hair).")]
    public float cameraProximityIgnore = 0.4f;

    [Header("Fade Settings")]
    [Range(0f, 1f)] public float fadeAlpha = 0.25f;
    public float fadeSpeed = 4f;

    // Conservative-NonAlloc path: keep List<FadingObject> (Contains is O(N) but N is
    // tiny — a couple of trees max), use SphereCastNonAlloc to skip the per-frame
    // RaycastHit[] allocation, and throttle to ~30 Hz instead of every frame.
    private readonly List<FadingObject> currentlyFaded = new List<FadingObject>(8);
    private readonly List<FadingObject> hitsThisFrame = new List<FadingObject>(8);
    private readonly Dictionary<Collider, FadingObject> faderCache = new Dictionary<Collider, FadingObject>(32);
    private static readonly RaycastHit[] s_hitBuffer = new RaycastHit[16];

    [Header("Performance")]
    [Tooltip("Скільки разів на секунду перевіряти оклюзію. 30 Hz практично невідрізнимо від 60 Hz, але вдвічі дешевше")]
    [Range(15f, 60f)] public float scansPerSecond = 30f;
    private float nextScanTime = 0f;

    private void Start()
    {
        if (playerTarget == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTarget = player.transform;
        }
    }

    private void Update()
    {
        if (playerTarget == null || Time.frameCount < 10) return;

        // Throttle to scansPerSecond so we don't re-spherecast every render frame
        if (Time.time < nextScanTime) return;
        nextScanTime = Time.time + (1f / Mathf.Max(15f, scansPerSecond));

        hitsThisFrame.Clear();

        Vector3 startPos = transform.position;
        Vector3 endPos = playerTarget.position + Vector3.up * 1.5f;

        Vector3 toPlayer = endPos - startPos;
        float dist = toPlayer.magnitude;
        if (dist < 0.01f) return;
        Vector3 dir = toPlayer / dist;

        // checkDistance: stop short of the player to avoid SphereCast'ing INTO
        // the player capsule. We'll do per-hit proximity filtering below as
        // well so trees the player is brushing against don't fade.
        float checkDistance = Mathf.Max(0f, dist - playerProximityIgnore * 0.5f);

        int hitCount = Physics.SphereCastNonAlloc(startPos, raycastRadius, dir, s_hitBuffer, checkDistance, foliageLayer);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit raw = s_hitBuffer[i];
            Collider col = raw.collider;
            if (col == null) continue;
            if (col is TerrainCollider) continue;

            // Per-hit proximity gates. raw.distance is the SphereCast travel
            // distance, NOT the world distance from camera to hit point, so
            // measure both ends from the actual hit point. This is the key
            // fix that lets the script say "this tree is between us" without
            // also flagging "this tree is just nearby".
            Vector3 hp = raw.point;
            if (hp == Vector3.zero) hp = col.bounds.center;

            float distFromCam = Vector3.Distance(hp, startPos);
            float distFromPlayer = Vector3.Distance(hp, endPos);
            if (distFromCam < cameraProximityIgnore) continue;
            if (distFromPlayer < playerProximityIgnore) continue;

            FadingObject fader = AcquireOrCreateFader(col);
            if (fader == null) continue;

            hitsThisFrame.Add(fader);
            fader.FadeOut();
            if (!currentlyFaded.Contains(fader)) currentlyFaded.Add(fader);
        }

        for (int i = currentlyFaded.Count - 1; i >= 0; i--)
        {
            FadingObject fader = currentlyFaded[i];
            if (!hitsThisFrame.Contains(fader))
            {
                if (fader != null) fader.FadeIn();
                currentlyFaded.RemoveAt(i);
            }
        }
    }

    // Locates or attaches a FadingObject to a hit collider's renderable parent.
    // The old code used GetComponentInParent<MeshRenderer>(), which misses two
    // common tree hierarchies: (a) collider on a child node with renderers
    // sitting on siblings, and (b) renderers on the same node as the collider
    // (works), but for SpeedTree / SkinnedMeshRenderer setups Renderer is the
    // correct base type. Falls through cleanly when nothing renderable exists.
    private FadingObject AcquireOrCreateFader(Collider col)
    {
        if (faderCache.TryGetValue(col, out FadingObject cached)) return cached;

        FadingObject fader = col.GetComponentInParent<FadingObject>();
        if (fader == null) fader = col.GetComponentInChildren<FadingObject>();

        if (fader == null)
        {
            // Find a host transform that actually owns renderable geometry,
            // preferring the parent that aggregates all the tree's pieces.
            Transform host = null;
            Renderer renderUp = col.GetComponentInParent<Renderer>();
            if (renderUp != null)
            {
                host = renderUp.transform;
                // Climb to the topmost ancestor that still has renderers
                // somewhere inside it — for typical tree prefabs this lands
                // on the prefab root so the entire tree fades together.
                while (host.parent != null && host.parent.GetComponentInChildren<Renderer>(true) != null)
                {
                    host = host.parent;
                }
            }
            else
            {
                Renderer renderDown = col.GetComponentInChildren<Renderer>();
                if (renderDown != null) host = col.transform;
            }

            if (host == null)
            {
                faderCache[col] = null;
                return null;
            }

            fader = host.gameObject.AddComponent<FadingObject>();
            fader.Initialize(fadeAlpha, fadeSpeed);
        }

        faderCache[col] = fader;
        return fader;
    }
}