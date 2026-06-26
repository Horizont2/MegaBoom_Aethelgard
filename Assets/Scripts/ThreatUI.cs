using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space indicator that points at the latest enemy starting a telegraph.
/// Alpha ramps from 0 to 1 across the telegraph window so the player can time
/// their dodge — peak brightness == strike landing.
/// </summary>
public class ThreatUI : MonoBehaviour
{
    public static ThreatUI Instance;

    public Image threatIndicatorImage;
    public float displayDuration = 0.8f;

    [Header("Strike Timing (Visual Countdown)")]
    [Tooltip("Fraction of the duration at which alpha peaks. The peak should line up with when the enemy actually strikes. EnemyAI passes attackTelegraphTime+0.2 as the duration, so for the default attackTelegraphTime=0.5 the strike lands at ≈ 0.71 of the window — 0.7 is a safe default that nudges the peak slightly BEFORE the hit (player dodges in time).")]
    [Range(0.4f, 1f)] public float peakFraction = 0.7f;

    [Tooltip("Curve shape of the ramp into the peak. 1 = linear, >1 = ease-in (slow start, dramatic acceleration as the strike approaches).")]
    [Range(1f, 3f)] public float rampShape = 1.5f;

    [Tooltip("Indicator scale at peak alpha — bigger value, more urgent feel.")]
    [Range(1f, 2f)] public float maxScale = 1.35f;

    private Transform playerTrans;
    private Camera mainCam;
    private Transform currentAttacker;

    private float visibleUntil = -1f;
    private float showStartedAt = -1f;
    private float currentDuration = 0.8f;

    private static readonly Color WarningColor = new Color(1f, 0.4f, 0f, 1f);
    private static readonly Color FadedColor = new Color(1f, 0f, 0f, 0f);

    private void Awake()
    {
        // Don't overwrite the live singleton if HUD_Canvas got duplicated by a
        // scene reload — the duplicate is about to be destroyed by GlobalHUD,
        // and stealing the slot would leave Instance pointing at a dead
        // GameObject (fake-null) and silently break every future ShowThreat.
        if (Instance != null && Instance != this) return;
        Instance = this;

        if (threatIndicatorImage != null)
            threatIndicatorImage.color = FadedColor;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        mainCam = Camera.main;
        FindPlayerIfNeeded();
    }

    private void FindPlayerIfNeeded()
    {
        if (playerTrans == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTrans = p.transform;
        }
        if (mainCam == null) mainCam = Camera.main;
    }

    public void ShowThreat(Transform attacker, float duration = 0.8f)
    {
        FindPlayerIfNeeded();

        if (threatIndicatorImage == null)
        {
            Debug.LogWarning("[ThreatUI] threatIndicatorImage isn't wired in the inspector — threat icon will never display.", this);
            return;
        }
        if (playerTrans == null) return;

        // Floor the duration. The old code stored `duration` straight into the
        // displayDuration field, so a 0 from a misconfigured caller would have
        // the routine immediately finalize back to alpha=0.
        duration = Mathf.Max(0.3f, duration);

        currentAttacker = attacker;
        showStartedAt = Time.time;
        visibleUntil = Time.time + duration;
        currentDuration = duration;
        displayDuration = duration;

        // Start invisible — we ramp UP across the telegraph window. The first
        // Update tick will refine, but we also set it here so a frame between
        // ShowThreat and Update doesn't see stale state.
        Color c = WarningColor;
        c.a = 0f;
        threatIndicatorImage.color = c;
        transform.localScale = Vector3.one;

        // Belt-and-suspenders: if the indicator GameObject got hidden by
        // somebody else, undo that.
        if (!threatIndicatorImage.gameObject.activeSelf)
            threatIndicatorImage.gameObject.SetActive(true);
    }

    public Transform GetCurrentThreat()
    {
        return currentAttacker;
    }

    private void Update()
    {
        if (threatIndicatorImage == null) return;

        if (Time.time >= visibleUntil)
        {
            if (threatIndicatorImage.color.a > 0.001f)
            {
                threatIndicatorImage.color = FadedColor;
                transform.localScale = Vector3.one;
                currentAttacker = null;
            }
            return;
        }

        float elapsed = Time.time - showStartedAt;
        float t = currentDuration > 0f ? Mathf.Clamp01(elapsed / currentDuration) : 1f;

        // Rotate toward attacker so the icon points at the threat
        if (currentAttacker != null)
        {
            if (mainCam == null) mainCam = Camera.main;
            if (mainCam != null && playerTrans != null)
            {
                Vector3 toEnemy = currentAttacker.position - playerTrans.position;
                toEnemy.y = 0f;

                Vector3 fwd = mainCam.transform.forward;
                fwd.y = 0f;

                float angle = Vector3.SignedAngle(fwd, toEnemy, Vector3.up);
                transform.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }
        }

        // Alpha ramp: 0 → 1 across the first peakFraction of the duration,
        // then 1 → 0 across the remainder. Player learns "peak brightness =
        // strike landing NOW — DODGE!".
        float alpha;
        if (t <= peakFraction)
        {
            float k = t / Mathf.Max(0.0001f, peakFraction);
            // rampShape > 1 gives ease-in (slow start, fast peak approach)
            alpha = Mathf.Pow(k, rampShape);
        }
        else
        {
            float k = (t - peakFraction) / Mathf.Max(0.0001f, 1f - peakFraction);
            alpha = 1f - k;
        }
        alpha = Mathf.Clamp01(alpha);

        Color c = WarningColor;
        c.a = alpha;
        threatIndicatorImage.color = c;

        // Scale inflates with alpha for extra "incoming!" urgency
        float scale = Mathf.Lerp(1f, maxScale, alpha);
        transform.localScale = new Vector3(scale, scale, scale);
    }
}
