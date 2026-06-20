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

    private HashSet<FadingObject> currentlyFaded = new HashSet<FadingObject>();
    private HashSet<FadingObject> hitsThisFrame = new HashSet<FadingObject>();
    private List<FadingObject> faderRemovalCache = new List<FadingObject>();

    // GetComponent cache so we don't pay the lookup cost per ray hit per frame
    private Dictionary<Collider, FadingObject> faderCache = new Dictionary<Collider, FadingObject>();

    private static readonly RaycastHit[] s_hitBuffer = new RaycastHit[32];

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
        if (playerTarget == null || Time.frameCount < 10) return; // ������ ����� ����� ���� ������������

        hitsThisFrame.Clear();

        Vector3 startPos = transform.position;
        Vector3 endPos = playerTarget.position + Vector3.up * 1.5f;

        Vector3 dir = (endPos - startPos).normalized;
        float dist = Vector3.Distance(startPos, endPos);
        float checkDistance = Mathf.Max(0f, dist - 1.5f);

        int hitCount = Physics.SphereCastNonAlloc(startPos, raycastRadius, dir, s_hitBuffer, checkDistance, foliageLayer);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = s_hitBuffer[i];
            Collider col = hit.collider;
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
            currentlyFaded.Add(fader);
        }

        // Restore objects that are no longer occluded
        faderRemovalCache.Clear();
        foreach (FadingObject fader in currentlyFaded)
        {
            if (!hitsThisFrame.Contains(fader)) faderRemovalCache.Add(fader);
        }

        for (int i = 0; i < faderRemovalCache.Count; i++)
        {
            FadingObject fader = faderRemovalCache[i];
            if (fader != null) fader.FadeIn();
            currentlyFaded.Remove(fader);
        }
    }
}