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
                cooldownText.text = Mathf.CeilToInt(fillTime).ToString();
            }
        }
        else
        {
            if (wasOnCooldown)
            {
                wasOnCooldown = false;

                if (cooldownOverlay != null) cooldownOverlay.fillAmount = 0f;
                if (cooldownText != null) cooldownText.enabled = false;

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