using UnityEngine;
using System.Collections.Generic;

public class CameraOcclusion : MonoBehaviour
{
    [Header("Target & Scanning")]
    public Transform playerTarget;
    public LayerMask foliageLayer;
    public float raycastRadius = 0.1f;

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

        Vector3 dir = (endPos - startPos).normalized;
        float dist = Vector3.Distance(startPos, endPos);
        float checkDistance = Mathf.Max(0f, dist - 1.5f);

        int hitCount = Physics.SphereCastNonAlloc(startPos, raycastRadius, dir, s_hitBuffer, checkDistance, foliageLayer);

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = s_hitBuffer[i].collider;
            if (col == null) continue;
            if (col is TerrainCollider) continue;

            FadingObject fader;
            if (!faderCache.TryGetValue(col, out fader))
            {
                fader = col.GetComponentInParent<FadingObject>();
                if (fader == null && col.GetComponentInParent<MeshRenderer>() != null)
                {
                    fader = col.gameObject.AddComponent<FadingObject>();
                    fader.Initialize(fadeAlpha, fadeSpeed);
                }
                faderCache[col] = fader;
            }

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
}