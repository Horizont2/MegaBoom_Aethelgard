using UnityEngine;

// Fires the Part-2 climax beats at a point along the ride: a LIGHTNING flash
// (a bright directional burst — reads as a strike) + a thunder crack + the horse
// NEIGH. Progress-driven so it lands at the same spot on the route every time.
//
// NOTE: the horse REAR-UP, the rider LOOK-BACK / FALL and the battle need real
// animations (see the animation list) — this component covers the strike, sound
// and (optionally) a stylised transform-based rear as a placeholder.
public class TrailerRideEvent : MonoBehaviour
{
    public TrailerHorseRide ride;
    [Range(0f, 1f)] public float strikeProgress = 0.9f;

    [Header("Lightning flash")]
    public float flashIntensity = 3f;
    public float flashDuration = 0.25f;
    public Color flashColor = new Color(0.85f, 0.9f, 1f);

    [Header("Sound (routes through AudioManager)")]
    public string thunderId = "AMB/AMB_Thunder";
    public string neighId = "Animals/Horse_Snort";   // placeholder until a real neigh event exists

    [Header("Placeholder rear (until a real rear-up animation is added)")]
    public bool fakeRear = false;
    public Transform horseModel;      // what to tilt; defaults to the ride's transform
    public float rearAngle = 45f;
    public float rearTime = 0.6f;

    private Light _flash;
    private bool _struck;
    private float _flashT = -1f;
    private float _rearT = -1f;
    private Quaternion _rearBase;

    private void OnEnable()
    {
        _struck = false; _flashT = -1f; _rearT = -1f;
        if (_flash == null)
        {
            var go = new GameObject("Trailer_StrikeFlash");
            go.transform.SetParent(transform, false);
            _flash = go.AddComponent<Light>();
            _flash.type = LightType.Directional;
            _flash.color = flashColor;
            _flash.shadows = LightShadows.None;
            _flash.transform.rotation = Quaternion.Euler(55f, 35f, 0f);
        }
        _flash.intensity = 0f;
    }

    private void Update()
    {
        if (!_struck && ride != null && ride.progress01 >= strikeProgress)
        {
            _struck = true;
            _flashT = 0f;
            var am = AudioManager.Instance;
            if (am != null)
            {
                if (!string.IsNullOrEmpty(thunderId)) am.PlaySFX(thunderId);
                if (!string.IsNullOrEmpty(neighId)) am.PlaySFX(neighId);
            }
            if (fakeRear)
            {
                if (horseModel == null && ride != null) horseModel = ride.transform;
                if (horseModel != null) { _rearBase = horseModel.localRotation; _rearT = 0f; }
            }
        }

        if (_flashT >= 0f)
        {
            _flashT += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(_flashT / Mathf.Max(0.01f, flashDuration));
            _flash.intensity = flashIntensity * k * k;
            if (_flashT >= flashDuration) { _flashT = -1f; _flash.intensity = 0f; }
        }

        if (_rearT >= 0f && horseModel != null)
        {
            _rearT += Time.deltaTime;
            float t = Mathf.Clamp01(_rearT / Mathf.Max(0.01f, rearTime));
            float pitch = Mathf.Sin(t * Mathf.PI) * rearAngle;      // up then down
            horseModel.localRotation = _rearBase * Quaternion.Euler(-pitch, 0f, 0f);
            if (_rearT >= rearTime) { _rearT = -1f; horseModel.localRotation = _rearBase; }
        }
    }
}
