using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class GrenadeUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("ѕерет€гни сюди об'Їкт Icon (саму гранату)")]
    public Image grenadeIcon;

    [Tooltip("ѕерет€гни сюди об'Їкт Cooldown_Overlay")]
    public Image cooldownOverlay;

    [Tooltip("ѕерет€гни сюди об'Їкт KD_Text")]
    public TextMeshProUGUI cooldownText;

    private PlayerController player;
    private bool wasOnCooldown = false;

    private void Start()
    {
        if (cooldownText != null) cooldownText.enabled = false;
        if (cooldownOverlay != null) cooldownOverlay.fillAmount = 0f;
    }

    private int _lastShownSeconds = int.MinValue;
    private static readonly string[] SecondsText =
        { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12" };

    private void Update()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<PlayerController>();
            if (player == null) return;
        }

        // --- √ќЋќ¬Ќ»… ‘≤ —: —»Ќ’–ќЌ≤«ј÷≤я √ќƒ»ЌЌ» ≤¬ ---
        // ¬икористовуЇмо Time.unscaledTime, щоб ≥деально зб≥гатис€ з лог≥кою PlayerController
        float timeSinceLastThrow = Time.unscaledTime - player.lastGrenadeTime;
        float remainingTime = player.grenadeCooldown - timeSinceLastThrow;

        if (remainingTime > 0 && timeSinceLastThrow >= 0)
        {
            wasOnCooldown = true;

            float fillTime = Mathf.Clamp(remainingTime, 0, player.grenadeCooldown);

            if (cooldownOverlay != null)
            {
                cooldownOverlay.fillAmount = fillTime / player.grenadeCooldown;
            }

            if (cooldownText != null)
            {
                cooldownText.enabled = true;
                // ==== THE NUMBER CHANGES ONCE A SECOND, NOT SIXTY TIMES ====
                //
                // int.ToString() allocates a fresh string on every call, and
                // assigning TMP text marks the canvas dirty and forces a batch
                // rebuild - HUD_Canvas carries 42 CanvasRenderers. Both were
                // happening every frame of every cooldown, to write the same
                // digit that was already on screen.
                //
                // The lookup table keeps even the once-a-second change
                // allocation-free. Anything past it falls back to ToString,
                // which for a grenade cooldown is a case that does not arise.
                int secs = Mathf.CeilToInt(fillTime);
                if (secs != _lastShownSeconds)
                {
                    _lastShownSeconds = secs;
                    cooldownText.text = (secs >= 0 && secs < SecondsText.Length)
                                      ? SecondsText[secs]
                                      : secs.ToString();
                }
            }
        }
        else
        {
            if (wasOnCooldown)
            {
                wasOnCooldown = false;

                if (cooldownOverlay != null) cooldownOverlay.fillAmount = 0f;
                if (cooldownText != null) cooldownText.enabled = false;
                // Forget the last digit so the next cooldown writes its first frame.
                _lastShownSeconds = int.MinValue;

                if (grenadeIcon != null)
                {
                    StopAllCoroutines();
                    StartCoroutine(ReadyPulseRoutine());
                }
            }
        }
    }

    private IEnumerator ReadyPulseRoutine()
    {
        float duration = 0.3f;
        float elapsed = 0f;

        Vector3 originalScale = Vector3.one;
        Vector3 targetScale = new Vector3(1.25f, 1.25f, 1.25f);

        while (elapsed < duration)
        {
            // ¬икористовуЇмо unscaledDeltaTime, щоб ан≥мац≥€ миттЇво грала нав≥ть у слоу-мо
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            float curve = Mathf.Sin(t * Mathf.PI);

            if (grenadeIcon != null)
                grenadeIcon.transform.localScale = Vector3.Lerp(originalScale, targetScale, curve);

            yield return null;
        }

        if (grenadeIcon != null)
            grenadeIcon.transform.localScale = originalScale;
    }
}