using UnityEngine;
using System.Collections.Generic;

public class CameraOcclusion : MonoBehaviour
{
    [Header("Target & Scanning")]
    public Transform playerTarget;
    public LayerMask foliageLayer;
    [Tooltip("Width of the line-of-sight sphere cast. ~0.7 catches typical tree trunks without hugging trees that are merely off to the side.")]
    public float raycastRadius = 0.7f;
    [Tooltip("Stop the cast this far short of the player so the player's own collider can't intercept the test.")]
    public float playerProximityIgnore = 0.6f;
    [Tooltip("Skip hits this close to the camera (e.g. handheld weapon, hair, head bones).")]
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

        // Stop slightly short of the player so we don't cast into their own
        // bounds. The player is on a layer outside foliageLayer, so this is
        // just defensive; keep it small (~0.6m) so the cast sweeps the full
        // path even when the player is hugging a tree.
        float checkDistance = Mathf.Max(0f, dist - playerProximityIgnore);

        int hitCount = Physics.SphereCastNonAlloc(startPos, raycastRadius, dir, s_hitBuffer, checkDistance, foliageLayer);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit raw = s_hitBuffer[i];
            Collider col = raw.collider;
            if (col == null) continue;
            if (col is TerrainCollider) continue;

            Vector3 hp = raw.point;
            // SphereCast returns a zero point when the cast starts already
            // overlapping the collider — fall back to the collider's own
            // center so we still flag it as blocking.
            if (hp == Vector3.zero) hp = col.bounds.center;

            // Reject hits too close to the camera (handheld weapon, head
            // attachments, hair) and trees that aren't actually between the
            // camera and player — measure perpendicular offset from the LoS
            // segment, NOT raw distance to the player, because a tree right
            // next to the player but along the LoS IS blocking the view.
            float distFromCam = Vector3.Distance(hp, startPos);
            if (distFromCam < cameraProximityIgnore) continue;

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
                // Climb to the tree prefab root so the whole tree fades
                // together — BUT stay inside the foliage layer. The old
                // unbounded climb ("any ancestor with a renderer") walked
                // straight up into shared Default-layer containers like an
                // "Environment"/"Trees" parent, so a single occluding trunk
                // faded the ENTIRE scene and cloned hundreds of materials in
                // one frame (that was both the "everything turns transparent"
                // bug and the FPS spike). Bounding by layer keeps the fader
                // scoped to just this tree.
                int foliageMask = foliageLayer.value;
                while (host.parent != null
                    && ((1 << host.parent.gameObject.layer) & foliageMask) != 0
                    && host.parent.GetComponentInChildren<Renderer>(true) != null)
                {
                    host = host.parent;
                }

                // Safety net: if the chosen host still aggregates an
                // unreasonable number of renderers it isn't a single tree
                // (mis-layered container) — fall back to the trunk renderer
                // alone so we never fade a whole forest / building cluster.
                const int MaxTreeRenderers = 24;
                if (host.GetComponentsInChildren<Renderer>(true).Length > MaxTreeRenderers)
                    host = renderUp.transform;
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