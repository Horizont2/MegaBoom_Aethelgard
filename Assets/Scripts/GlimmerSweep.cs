using UnityEngine;

[RequireComponent(typeof(Light))]
public class GlimmerSweep : MonoBehaviour
{
    [Header("Sweep Points")]
    public Transform startPoint; // ����� ������
    public Transform endPoint;   // ���� ������

    [Header("Settings")]
    public float speed = 0.5f;   // �������� ��������
    public float maxIntensity = 50f; // ����������� ��������� �� ������

    // ==== IT HAS TO MOVE WITHOUT ANYBODY WIRING TWO TRANSFORMS ====
    //
    // The positional sweep only ran when startPoint AND endPoint were both
    // assigned, and on the camp buildings neither is — so the light pulsed on
    // the spot and never travelled. "Стоїть на місці" was literally true: the
    // brightness was breathing, the position never changed.
    //
    // Two hand-placed markers per building is exactly the kind of wiring that
    // gets forgotten on five of six prefabs, so the default is now a sweep the
    // component works out for itself, around wherever the light was authored.
    // Points still win when they are there.
    [Header("Fallback sweep (used when no points are wired)")]
    [Tooltip("Metres the light travels either side of its authored position when startPoint/endPoint are empty.")]
    public float fallbackDistance = 1.6f;
    [Tooltip("Direction of that travel, in the light's own parent space. Sideways along the building face by default.")]
    public Vector3 fallbackAxis = Vector3.right;
    [Tooltip("Metres the light also rises and falls by, so the sweep is a drift rather than a rail.")]
    public float fallbackBob = 0.35f;

    private Light glimmerLight;
    private float progress = 0f;
    private Vector3 _anchor;
    private bool _anchored;

    private void Awake()
    {
        glimmerLight = GetComponent<Light>();
    }

    private void OnEnable()
    {
        // ������� ������� ������, ���� ����� ���������
        progress = 0f;
        // Remember where the light was AUTHORED, once. Re-reading it on every
        // enable would let the fallback sweep drift the anchor along with it.
        if (!_anchored) { _anchor = transform.localPosition; _anchored = true; }
    }

    private void Update()
    {
        // Advance the sweep progress unconditionally. The old code early-returned
        // when startPoint/endPoint were unwired — so on any building whose glimmer
        // prefab lacked those references (the lumberjack, per report) the light
        // just sat frozen. Now the pulse ALWAYS animates; the positional sweep is
        // the only part that needs the points.
        // UNSCALED. This is decoration on a camp building, and the camp opens
        // panels that stop the clock — on scaled time the glimmer freezes the
        // moment the player opens the very upgrade screen it is advertising.
        progress += Time.unscaledDeltaTime * Mathf.Max(0.01f, speed);
        if (progress > 1f) progress -= 1f;

        if (startPoint != null && endPoint != null)
        {
            transform.position = Vector3.Lerp(startPoint.position, endPoint.position, progress);
        }
        else if (_anchored)
        {
            // A there-and-back sweep rather than a loop that snaps home: a light
            // that teleports back to the start every cycle reads as a glitch.
            float sway = Mathf.Sin(progress * Mathf.PI * 2f);
            Vector3 axis = fallbackAxis.sqrMagnitude > 0.0001f ? fallbackAxis.normalized : Vector3.right;
            transform.localPosition = _anchor
                                    + axis * (sway * fallbackDistance)
                                    + Vector3.up * (Mathf.Sin(progress * Mathf.PI * 4f) * fallbackBob);
        }

        // Sin(progress * PI): 0 at the ends, 1 in the middle — a soft breathing glow.
        if (glimmerLight != null)
            glimmerLight.intensity = Mathf.Sin(progress * Mathf.PI) * maxIntensity;
    }
}