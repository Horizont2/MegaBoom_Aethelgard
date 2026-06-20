using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space indicator that points at the latest enemy starting a telegraph.
/// Rewritten on an Update-driven timeline instead of a coroutine so that a
/// burst of ShowThreat calls in the same frame doesn't kill the previous run
/// before it can set alpha=1.
/// </summary>
public class ThreatUI : MonoBehaviour
{
    public static ThreatUI Instance;

    public Image threatIndicatorImage;
    public float displayDuration = 0.8f;

    private Transform playerTrans;
    private Camera mainCam;
    private Transform currentAttacker;

    private float visibleUntil = -1f;
    private float showStartedAt = -1f;
    private float currentDuration = 0.8f;

    private static readonly Color WarningColor = new Color(1f, 0.4f, 0f, 1f);
    private static readonly Color FadedColor = new Color(1f, 0f, 0f, 0f);
    private static readonly Vector3 PopScale = Vector3.one * 1.3f;

    private void Awake()
    {
        // Don't overwrite the live singleton if HUD_Canvas got duplicated by a
        // scene reload — the duplicate is about to be destroyed by GlobalHUD,
        // and stealing the slot here would leave Instance pointing at a dead
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
        // displayDuration field, so a 0 from a caller (e.g. a boss with
        // attackTelegraphTime = 0) would have the routine immediately
        // finalize back to alpha=0.
        duration = Mathf.Max(0.3f, duration);

        currentAttacker = attacker;
        showStartedAt = Time.time;
        visibleUntil = Time.time + duration;
        currentDuration = duration;
        displayDuration = duration;

        // Set alpha synchronously so even one frame of visibility is honored —
        // we no longer depend on a coroutine surviving past the first yield.
        threatIndicatorImage.color = WarningColor;
        transform.localScale = PopScale;

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

        // Rotate toward attacker
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

        // Pop scale at start, settle to 1 after 20% of duration
        transform.localScale = t < 0.2f
            ? Vector3.Lerp(PopScale, Vector3.one, t / 0.2f)
            : Vector3.one;

        // Hold full color, fade out the last 20%
        if (t > 0.8f)
        {
            float fadeT = (t - 0.8f) / 0.2f;
            threatIndicatorImage.color = Color.Lerp(WarningColor, FadedColor, fadeT);
        }
        else
        {
            threatIndicatorImage.color = WarningColor;
        }
    }
}
