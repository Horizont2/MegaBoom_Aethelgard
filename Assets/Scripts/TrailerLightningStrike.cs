using UnityEngine;

// A real, visible lightning STRIKE (there's no bolt prefab in the project, so we
// generate one): a jagged bolt from the sky down to a ground point, a hard white
// flash that lights the whole scene, a point-light burst at the impact, and a
// thunder crack. Call Strike(groundPoint); it self-animates and hides.
[RequireComponent(typeof(LineRenderer))]
public class TrailerLightningStrike : MonoBehaviour
{
    public float height = 45f;           // how high the bolt comes from
    public int segments = 12;
    public float jaggedness = 2.2f;      // sideways wander of the bolt
    public float boltWidth = 0.5f;
    public float visibleTime = 0.16f;    // how long the bolt is on screen
    public float flashIntensity = 6f;
    public float flashRange = 60f;
    public string thunderId = "AMB/AMB_Thunder";

    private LineRenderer _lr;
    private Light _flash;
    private float _t = -1f;

    private void Awake()
    {
        _lr = GetComponent<LineRenderer>();
        _lr.useWorldSpace = true;
        _lr.widthMultiplier = boltWidth;
        _lr.numCapVertices = 2;
        _lr.textureMode = LineTextureMode.Stretch;
        _lr.material = new Material(Shader.Find("Sprites/Default"));
        _lr.startColor = new Color(0.8f, 0.9f, 1f, 1f);
        _lr.endColor = new Color(0.7f, 0.85f, 1f, 1f);
        _lr.enabled = false;

        var lgo = new GameObject("StrikeFlash");
        lgo.transform.SetParent(transform, false);
        _flash = lgo.AddComponent<Light>();
        _flash.type = LightType.Point;
        _flash.color = new Color(0.85f, 0.92f, 1f);
        _flash.range = flashRange;
        _flash.shadows = LightShadows.None;
        _flash.intensity = 0f;
    }

    public void Strike(Vector3 ground)
    {
        Vector3 top = ground + Vector3.up * height;
        _lr.positionCount = segments + 1;
        for (int i = 0; i <= segments; i++)
        {
            float f = (float)i / segments;
            Vector3 p = Vector3.Lerp(top, ground, f);
            if (i != 0 && i != segments)
                p += new Vector3(Random.Range(-jaggedness, jaggedness), 0f, Random.Range(-jaggedness, jaggedness)) * (1f - f * 0.5f);
            _lr.SetPosition(i, p);
        }
        _lr.enabled = true;

        _flash.transform.position = ground + Vector3.up * 2f;
        _flash.intensity = flashIntensity;
        _t = 0f;

        if (AudioManager.Instance != null && !string.IsNullOrEmpty(thunderId))
            AudioManager.Instance.PlaySFX(thunderId);
    }

    private void Update()
    {
        if (_t < 0f) return;
        _t += Time.deltaTime;
        float k = 1f - Mathf.Clamp01(_t / visibleTime);
        // Flicker the bolt while it's visible.
        _lr.enabled = _t < visibleTime && (Mathf.FloorToInt(_t * 60f) % 2 == 0);
        _flash.intensity = flashIntensity * k * k;
        if (_t >= visibleTime) { _t = -1f; _lr.enabled = false; _flash.intensity = 0f; }
    }
}
