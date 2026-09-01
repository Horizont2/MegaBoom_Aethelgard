using UnityEngine;

// Drives the horse's ambient audio (hoofbeats, breathing, occasional snorts)
// through the game's FMOD-backed AudioManager. Attach it to the horse root
// (the object that actually MOVES — e.g. StoryExtractionPoint.horseTransform).
//
// It has no hard dependency on how the horse is animated or moved: it derives
// speed from the object's own position change each frame, so it works both for
// the Lvl_1 evacuation cutscene (transform-lerped) and for the lore trailer
// (Cinemachine/Timeline-driven), with no extra wiring.
//
//   * Breathing  — a looping bed that plays the whole time the horse is active.
//   * Gallop     — a looping hoofbeat bed that fades in while moving and out
//                  when the horse stops.
//   * Snort      — a one-shot fired at random intervals while moving.
//
// The three FMOD events are wired on the AudioManager (horseGallop / horseBreath
// / horseSnort) — this component only references them by AudioID and never
// touches raw AudioSources, so the SFX volume bus + spatialisation apply.
public class HorseAudioController : MonoBehaviour
{
    [Header("Movement detection")]
    [Tooltip("Metres/sec above which the horse counts as 'moving' and the gallop loop plays.")]
    public float moveSpeedThreshold = 0.6f;
    [Tooltip("How quickly the measured speed smooths (higher = snappier start/stop).")]
    public float speedSmoothing = 8f;

    [Header("Breathing")]
    [Tooltip("Play the breathing loop the whole time this object is active (even when standing).")]
    public bool breatheWhenIdle = true;

    [Header("Snorts")]
    [Tooltip("Fire an occasional snort one-shot while galloping. 0 = never.")]
    public bool snortWhileMoving = true;
    public float snortIntervalMin = 4f;
    public float snortIntervalMax = 9f;

    [Header("Fades")]
    public float gallopStopFade = 0.35f;
    public float breathStopFade = 0.6f;

    private Vector3 _lastPos;
    private float _smoothedSpeed;
    private bool _moving;
    private int _gallopHandle = -1;
    private int _breathHandle = -1;
    private float _nextSnortTime;

    private void OnEnable()
    {
        _lastPos = transform.position;
        _smoothedSpeed = 0f;
        _moving = false;
        // Start the breathing bed immediately if it should run at idle.
        if (breatheWhenIdle) StartBreath();
        ScheduleNextSnort();
    }

    private void OnDisable() => StopAll();
    private void OnDestroy() => StopAll();

    private void Update()
    {
        if (AudioManager.Instance == null) return;

        // Speed from raw position delta — independent of how the horse is moved.
        float dt = Time.deltaTime;
        float instSpeed = 0f;
        if (dt > 0.0001f)
        {
            instSpeed = (transform.position - _lastPos).magnitude / dt;
            _lastPos = transform.position;
        }
        _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, instSpeed, Mathf.Clamp01(dt * speedSmoothing));

        bool shouldMove = _smoothedSpeed >= moveSpeedThreshold;
        if (shouldMove && !_moving) StartGallop();
        else if (!shouldMove && _moving) StopGallop();

        // Breathing runs continuously when idle-breathing is on; otherwise it
        // rides along with the gallop.
        if (!breatheWhenIdle)
        {
            if (_moving && _breathHandle == -1) StartBreath();
            else if (!_moving && _breathHandle != -1) StopBreath();
        }

        if (_moving && snortWhileMoving && Time.time >= _nextSnortTime)
        {
            AudioManager.Instance.PlaySFX3DAttached(AudioID.Horse_Snort, transform);
            ScheduleNextSnort();
        }
    }

    private void StartGallop()
    {
        _moving = true;
        if (_gallopHandle == -1)
            _gallopHandle = AudioManager.Instance.PlayLoopingSFX3D(AudioID.Horse_Gallop, transform);
    }

    private void StopGallop()
    {
        _moving = false;
        if (_gallopHandle != -1)
        {
            AudioManager.Instance.StopLoopingSFX(_gallopHandle, gallopStopFade);
            _gallopHandle = -1;
        }
    }

    private void StartBreath()
    {
        if (AudioManager.Instance == null || _breathHandle != -1) return;
        _breathHandle = AudioManager.Instance.PlayLoopingSFX3D(AudioID.Horse_Breath, transform);
    }

    private void StopBreath()
    {
        if (_breathHandle != -1)
        {
            AudioManager.Instance.StopLoopingSFX(_breathHandle, breathStopFade);
            _breathHandle = -1;
        }
    }

    private void StopAll()
    {
        if (AudioManager.Instance == null) { _gallopHandle = -1; _breathHandle = -1; return; }
        StopGallop();
        StopBreath();
    }

    private void ScheduleNextSnort()
    {
        _nextSnortTime = Time.time + Random.Range(snortIntervalMin, snortIntervalMax);
    }
}
