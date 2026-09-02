using UnityEngine;

// Sets up the trailer's soundscape through the game's FMOD AudioManager:
//   * a looping WIND bed and a looping RAIN bed (both start on enable),
//   * a lonely RAVEN/crow cry (one or two, timed over the opening),
//   * occasional DISTANT THUNDER for the storm.
//
// Horse sounds (hoofbeats / breathing / snorts) are handled separately by
// HorseAudioController on the horse itself — this covers the *ambience*.
//
// PlaySFX de-duplicates looping beds, so playing the rain here can't stack with
// the rain DayNightCycle already starts under a storm. All events are wired on
// the AudioManager (AMB_Wind / AMB_Rain / AMB_Crow / AMB_Distant_Thunder); if
// any is silent, assign its FMOD event in the AudioManager inspector.
public class TrailerAmbience : MonoBehaviour
{
    [Header("Looping beds")]
    public bool wind = true;
    public bool rain = true;

    [Header("Raven / crow cry")]
    public bool raven = true;
    [Tooltip("Seconds after start for the first caw (over the lonely opening).")]
    public float ravenFirstCryAt = 2.5f;
    [Tooltip("Seconds for a second caw (0 = only one).")]
    public float ravenSecondCryAt = 12f;

    [Header("Distant thunder")]
    public bool distantThunder = true;
    public float thunderIntervalMin = 7f;
    public float thunderIntervalMax = 15f;

    private float _t;
    private bool _ravenA, _ravenB;
    private float _nextThunder;
    private bool _bedsStarted;

    private void OnEnable()
    {
        _t = 0f; _ravenA = false; _ravenB = false;
        _bedsStarted = false;
        TryStartBeds();   // start immediately if the AudioManager is already up…
        _nextThunder = Random.Range(thunderIntervalMin, thunderIntervalMax);
    }

    // …otherwise keep retrying each frame until it's ready, so the wind/rain
    // beds come on at the very start instead of "somewhere in the middle" when
    // the AudioManager happened to be initialised a few frames late.
    private void TryStartBeds()
    {
        if (_bedsStarted) return;
        var am = AudioManager.Instance;
        if (am == null) return;
        if (wind) am.PlaySFX(AudioID.Ambient_Wind);
        if (rain) am.PlaySFX(AudioID.Ambient_Rain);
        _bedsStarted = true;
    }

    private void OnDisable()
    {
        var am = AudioManager.Instance;
        if (am == null) return;
        if (wind) am.StopLoopedBed(AudioID.Ambient_Wind);
        if (rain) am.StopLoopedBed(AudioID.Ambient_Rain);
    }

    private void Update()
    {
        var am = AudioManager.Instance;
        if (am == null) return;

        TryStartBeds();   // no-op once the wind/rain beds are running

        // Real-time clock so slow-mo doesn't drag the ambience timing.
        _t += Time.unscaledDeltaTime;

        if (raven && !_ravenA && _t >= ravenFirstCryAt) { _ravenA = true; am.PlaySFX(AudioID.Ambient_Crow); }
        if (raven && !_ravenB && ravenSecondCryAt > 0.01f && _t >= ravenSecondCryAt) { _ravenB = true; am.PlaySFX(AudioID.Ambient_Crow); }

        if (distantThunder && _t >= _nextThunder)
        {
            am.PlaySFX(AudioID.Ambient_DistantThunder);
            _nextThunder = _t + Random.Range(thunderIntervalMin, thunderIntervalMax);
        }
    }
}
