using UnityEngine;

public class FlickeringLight : MonoBehaviour
{
    private Light lightSource;
    private float baseIntensity;
    private float phaseOffset;
    private bool skipFrame;

    [Range(0.1f, 1f)] public float rangePercent = 0.2f;
    public float speed = 0.1f;

    void Start()
    {
        lightSource = GetComponent<Light>();
        if (lightSource != null) baseIntensity = lightSource.intensity;
        // De-sync many lights so they don't all hit Update on the same boundary
        phaseOffset = Random.Range(0f, 100f);
        skipFrame = Random.value > 0.5f;
    }

    void Update()
    {
        if (lightSource == null) return;

        // Update every other frame — flicker is fast but a 30 Hz refresh on a
        // single light is invisible vs. 60 Hz. With many torches this halves
        // the per-frame cost across the scene.
        skipFrame = !skipFrame;
        if (skipFrame) return;

        // PerlinNoise involves a hash lookup; two cheap sine waves give the
        // same "irregular flicker" feel at a fraction of the cost.
        float t = Time.time * speed * 10f + phaseOffset;
        float noise = (Mathf.Sin(t) + Mathf.Sin(t * 2.7f) * 0.5f) * 0.25f + 0.5f;
        lightSource.intensity = baseIntensity * Mathf.Lerp(1f - rangePercent, 1f + rangePercent, noise);
    }
}