using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class GrenadeUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Перетягни сюди об'єкт Icon (саму гранату)")]
    public Image grenadeIcon; // НОВЕ: посилання на іконку для анімації

    [Tooltip("Перетягни сюди об'єкт Cooldown_Overlay")]
    public Image cooldownOverlay;

    [Tooltip("Перетягни сюди об'єкт KD_Text")]
    public TextMeshProUGUI cooldownText;

    private PlayerController player;
    private bool wasOnCooldown = false; // Відстежуємо стан, щоб зіграти анімацію один раз

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

        float timeSinceLastThrow = Time.time - player.lastGrenadeTime;

        if (timeSinceLastThrow < player.grenadeCooldown)
        {
            wasOnCooldown = true; // Граната зараз на кулдауні
            float remainingTime = player.grenadeCooldown - timeSinceLastThrow;

            if (cooldownOverlay != null)
            {
                cooldownOverlay.fillAmount = remainingTime / player.grenadeCooldown;
            }

            if (cooldownText != null)
            {
                cooldownText.enabled = true;
                cooldownText.text = Mathf.CeilToInt(remainingTime).ToString();
            }
        }
        else
        {
            // МОМЕНТ ЗАВЕРШЕННЯ: Якщо вона ЩОЙНО перезарядилася
            if (wasOnCooldown)
            {
                wasOnCooldown = false;

                // Скидаємо UI
                if (cooldownOverlay != null) cooldownOverlay.fillAmount = 0f;
                if (cooldownText != null) cooldownText.enabled = false;

                // Запускаємо соковиту анімацію готовності!
                if (grenadeIcon != null)
                {
                    StopAllCoroutines();
                    StartCoroutine(ReadyPulseRoutine());
                }
            }
        }
    }

    // НОВЕ: Корутина анімації "Пульс"
    private IEnumerator ReadyPulseRoutine()
    {
        float duration = 0.3f; // Тривалість пульсу (швидкий "поп")
        float elapsed = 0f;

        Vector3 originalScale = Vector3.one;
        Vector3 targetScale = new Vector3(1.25f, 1.25f, 1.25f); // Збільшуємо на 25%

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // Математика: використовуємо синусоїду, щоб значення плавно пішло від 0 до 1, а потім назад до 0
            float t = elapsed / duration;
            float curve = Mathf.Sin(t * Mathf.PI);

            // Інтерполюємо розмір
            grenadeIcon.transform.localScale = Vector3.Lerp(originalScale, targetScale, curve);

            yield return null;
        }

        // Жорстко повертаємо розмір на місце в кінці
        grenadeIcon.transform.localScale = originalScale;
    }
}