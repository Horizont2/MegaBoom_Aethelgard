using UnityEngine;

// A real, visible lightning STRIKE (there's no bolt prefab in the project, so we
// generate one): a jagged bolt from the sky down to a ground point, a hard white
// flash that lights the whole scene, a point-light burst at the impact, and a
// thunder crack. Call Strike(groundPoint); it self-animates and hides.
[RequireComponent(typeof(LineRenderer))]
public class TrailerLightningStrike : MonoBehaviour
{
    public float height = 55f;           // how high the bolt comes from
    public int segments = 14;
    public float jaggedness = 2.6f;      // sideways wander of the bolt
    public float boltWidth = 0.9f;       // thick so it reads on screen
    public float visibleTime = 0.32f;    // how long the bolt is on screen
    public float flashIntensity = 9f;
    public float flashRange = 90f;
    public string thunderId = "AMB/AMB_Thunder";

    private LineRenderer _lr;
    private Light _flash;
    private float _t = -1f;

    private void Awake()
    {
        _lr = GetComponent<LineRenderer>();
        _lr.useWorldSpace = true;
        _lr.widthMultiplier = boltWidth;
        _lr.numCapVertices = 3;
        _lr.textureMode = LineTextureMode.Stretch;
        _lr.material = MakeBoltMaterial();
        _lr.startColor = new Color(1f, 1f, 1f, 1f);
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

    // A bright unlit material that actually shows in URP (Sprites/Default was
    // invisible). HDR emissive white-blue so the bolt glows.
    private static Material MakeBoltMaterial()
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        var m = new Material(sh);
        Color hdr = new Color(1.6f, 1.9f, 2.6f, 1f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", hdr);
        if (m.HasProperty("_Color")) m.SetColor("_Color", hdr);
        if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", hdr); }
        return m;
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
        // Solid for the first 60%, then flicker out.
        float frac = _t / visibleTime;
        _lr.enabled = _t < visibleTime && (frac < 0.6f || Mathf.FloorToInt(_t * 40f) % 2 == 0);
        _flash.intensity = flashIntensity * k;
        if (_t >= visibleTime) { _t = -1f; _lr.enabled = false; _flash.intensity = 0f; }
    }
}
