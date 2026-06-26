using UnityEngine;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class BlinkingSkipText : MonoBehaviour
{
    [Header("Blink Settings")]
    [Tooltip("Швидкість пульсації")]
    public float blinkSpeed = 2.5f;

    [Tooltip("Мінімальна прозорість (щоб текст не зникав повністю)")]
    [Range(0f, 1f)]
    public float minAlpha = 0.15f;

    [Tooltip("Максимальна прозорість")]
    [Range(0f, 1f)]
    public float maxAlpha = 0.85f;

    private TextMeshProUGUI textMesh;
    private Color originalColor;

    private void Awake()
    {
        textMesh = GetComponent<TextMeshProUGUI>();
        if (textMesh != null)
        {
            originalColor = textMesh.color;
        }
    }

    private void Update()
    {
        if (textMesh == null) return;

        // Генеруємо плавну хвилю від 0 до 1 за допомогою Time.unscaledTime (щоб працювало навіть на паузі)
        float wave = (Mathf.Sin(Time.unscaledTime * blinkSpeed) + 1f) / 2f;

        // Інтерполюємо між мінімальною та максимальною прозорістю
        float currentAlpha = Mathf.Lerp(minAlpha, maxAlpha, wave);

        // Застосовуємо нову прозорість до тексту
        originalColor.a = currentAlpha;
        textMesh.color = originalColor;
    }
}