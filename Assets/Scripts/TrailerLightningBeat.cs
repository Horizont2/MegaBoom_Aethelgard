using UnityEngine;
using UnityEngine.Playables;

// Timeline-synced lightning: at each flash time it briefly blasts a directional
// light (a burst that actually LIGHTS the scene, so everything stays visible)
// and fires a thunder crack a moment later. Placed on the rig; times come from
// the Act I tool so the flashes land on dramatic beats.
public class TrailerLightningBeat : MonoBehaviour
{
    public PlayableDirector director;
    [Tooltip("Timeline times (seconds) to strike.")]
    public float[] flashTimes;
    public float thunderDelay = 0.55f;
    public float flashDuration = 0.2f;
    public float flashIntensity = 2.5f;
    public Color flashColor = new Color(0.85f, 0.9f, 1f);

    private Light _light;
    private int _idx;
    private float _flashT = -1f;
    private bool _pendingThunder;
    private float _thunderAt;

    private void OnEnable()
    {
        _idx = 0; _flashT = -1f; _pendingThunder = false;
        if (_light == null)
        {
            var go = new GameObject("Trailer_LightningFlash");
            go.transform.SetParent(transform, false);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Directional;
            _light.color = flashColor;
            _light.shadows = LightShadows.None;
            _light.transform.rotation = Quaternion.Euler(55f, 35f, 0f);
        }
        _light.intensity = 0f;
    }

    private void Update()
    {
        float t = director != null ? (float)director.time : 0f;

        if (flashTimes != null && _idx < flashTimes.Length && t >= flashTimes[_idx])
        {
            _flashT = 0f;
            _pendingThunder = true;
            _thunderAt = flashTimes[_idx] + thunderDelay;
            _idx++;
        }

        if (_flashT >= 0f)
        {
            _flashT += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(_flashT / Mathf.Max(0.01f, flashDuration));
            _light.intensity = flashIntensity * k * k;      // sharp fall-off
            if (_flashT >= flashDuration) { _flashT = -1f; _light.intensity = 0f; }
        }

        if (_pendingThunder && t >= _thunderAt)
        {
            _pendingThunder = false;
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Env_Thunder);
        }
    }
}
