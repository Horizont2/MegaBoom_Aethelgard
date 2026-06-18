using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

[RequireComponent(typeof(CanvasGroup))]
public class CinematicTitleUI : MonoBehaviour
{
    public static CinematicTitleUI Instance;

    [Header("UI References")]
    public CanvasGroup canvasGroup;
    public RectTransform textContainer;
    public TextMeshProUGUI mainTitleText;
    public TextMeshProUGUI subtitleText;

    [Header("Victory Wreath (Wind Effect)")]
    public Image victoryWreath;
    private RectTransform wreathRect;

    [Header("Settings")]
    public float fadeDuration = 1.5f;
    public float holdDuration = 3.5f;
    public float zoomSpeed = 0.02f;

    private void Awake()
    {
        Instance = this;
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        if (victoryWreath != null)
        {
            // Повертаємо звичайний тип (нам більше не потрібен Fill)
            victoryWreath.type = Image.Type.Simple;
            wreathRect = victoryWreath.GetComponent<RectTransform>();
            victoryWreath.gameObject.SetActive(false);
        }
    }

    public void ShowTitle(string mainText, string subText, bool isVictory)
    {
        StopAllCoroutines();
        StartCoroutine(AnimateTitleRoutine(mainText, subText, isVictory));
    }

    private IEnumerator AnimateTitleRoutine(string mainText, string subText, bool isVictory)
    {
        mainTitleText.text = mainText;
        subtitleText.text = subText;

        mainTitleText.color = isVictory ? new Color(1f, 0.8f, 0.2f) : Color.white;
        subtitleText.color = new Color(0.8f, 0.8f, 0.8f, 1f);

        textContainer.localScale = Vector3.one * (isVictory ? 0.8f : 1.2f);
        Vector3 targetScale = Vector3.one;

        if (victoryWreath != null)
        {
            victoryWreath.gameObject.SetActive(isVictory);

            // Задаємо стартову точку для вітру (Зліва-знизу, нахилений)
            if (wreathRect != null)
            {
                wreathRect.anchoredPosition = new Vector2(-150f, -80f);
                wreathRect.localRotation = Quaternion.Euler(0, 0, 25f);
                wreathRect.localScale = Vector3.one * 0.8f;
            }

            Color c = victoryWreath.color;
            c.a = 0f;
            victoryWreath.color = c;
        }

        // =====================================
        // 1. ВІТЕР ПРИНОСИТЬ ВІНОК (Поява)
        // =====================================
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            float smoothT = 1f - Mathf.Pow(1f - t, 3f); // Ease Out

            canvasGroup.alpha = Mathf.Lerp(0f, 1f, smoothT);
            textContainer.localScale = Vector3.Lerp(textContainer.localScale, targetScale, smoothT * 0.5f);

            if (isVictory && victoryWreath != null && wreathRect != null)
            {
                wreathRect.anchoredPosition = Vector2.Lerp(new Vector2(-150f, -80f), Vector2.zero, smoothT);
                wreathRect.localRotation = Quaternion.Lerp(Quaternion.Euler(0, 0, 25f), Quaternion.identity, smoothT);
                wreathRect.localScale = Vector3.Lerp(Vector3.one * 0.8f, Vector3.one, smoothT);

                Color c = victoryWreath.color;
                c.a = Mathf.Lerp(0f, 1f, smoothT);
                victoryWreath.color = c;
            }

            yield return null;
        }

        // =====================================
        // 2. ДИХАННЯ ВІТРУ (Утримання на екрані)
        // =====================================
        elapsed = 0f;
        while (elapsed < holdDuration)
        {
            elapsed += Time.deltaTime;
            textContainer.localScale += Vector3.one * (Time.deltaTime * zoomSpeed);

            if (isVictory && wreathRect != null)
            {
                // Легке погойдування (Sway)
                float windSway = Mathf.Sin(Time.time * 2f) * 4f;
                wreathRect.localRotation = Quaternion.Euler(0, 0, windSway);
            }

            yield return null;
        }

        // =====================================
        // 3. ЗДУВАЄТЬСЯ ВІТРОМ (Розвіювання)
        // =====================================
        elapsed = 0f;
        Vector3 currentTextScale = textContainer.localScale;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            float smoothT = t * t; // Ease In (розгін)

            canvasGroup.alpha = Mathf.Lerp(1f, 0f, smoothT);
            textContainer.localScale = Vector3.Lerp(currentTextScale, currentTextScale + Vector3.one * 0.1f, smoothT);

            if (isVictory && victoryWreath != null && wreathRect != null)
            {
                // Відлітає вправо-вгору, нахиляється назад і розсіюється
                wreathRect.anchoredPosition = Vector2.Lerp(Vector2.zero, new Vector2(200f, 100f), smoothT);
                wreathRect.localRotation = Quaternion.Lerp(Quaternion.identity, Quaternion.Euler(0, 0, -35f), smoothT);
                wreathRect.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 1.4f, smoothT);

                Color c = victoryWreath.color;
                c.a = Mathf.Lerp(1f, 0f, smoothT);
                victoryWreath.color = c;
            }

            yield return null;
        }

        canvasGroup.alpha = 0f;
        if (victoryWreath != null) victoryWreath.gameObject.SetActive(false);
    }
}