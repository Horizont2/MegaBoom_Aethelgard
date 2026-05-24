using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

[RequireComponent(typeof(CanvasGroup))]
public class MissionUIElement : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;

    public Image backgroundImage; // Це наш об'єкт Bg, який ми будемо рухати

    [Header("Settings")]
    [Tooltip("Увімкни це ТІЛЬКИ для сюжетної місії на сцені (щоб вона гарно виїжджала)")]
    public bool animateAppearance = false;
    public float animationDuration = 0.5f;

    private CanvasGroup canvasGroup;
    public bool isCompleted = false;

    private string baseDescription = "";

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();

        if (animateAppearance)
        {
            canvasGroup.alpha = 0f;
        }
    }

    public void Setup(string title, string description, int current, int target)
    {
        isCompleted = false;
        StopAllCoroutines();

        if (titleText != null) titleText.text = title;

        baseDescription = description;
        UpdateProgress(current, target);

        if (animateAppearance)
        {
            StartCoroutine(AppearRoutine());
            animateAppearance = false; // Скидаємо, щоб наступні оновлення не викликали виїзд знову
        }
        else
        {
            if (canvasGroup != null) canvasGroup.alpha = 1f;
        }
    }

    private IEnumerator AppearRoutine()
    {
        // МАГІЯ ТУТ: Ми не чіпаємо головний об'єкт (його тримає Layout Group).
        // Ми беремо тільки внутрішню графіку (Bg) і рухаємо її!
        if (backgroundImage == null) yield break;

        Transform visualTransform = backgroundImage.transform;

        // Зчитуємо локальну позицію Bg (зазвичай це 0,0,0)
        Vector3 targetPos = visualTransform.localPosition;

        // Відсуваємо її на 300 пікселів вліво для старту
        Vector3 startPos = targetPos + new Vector3(-300f, 0, 0);

        float t = 0;
        while (t < 1)
        {
            t += Time.deltaTime / animationDuration;
            float curve = Mathf.SmoothStep(0, 1, t);

            // Плавно проявляємо головний об'єкт
            if (canvasGroup != null) canvasGroup.alpha = curve;

            // Плавно висуваємо графіку
            visualTransform.localPosition = Vector3.Lerp(startPos, targetPos, curve);
            yield return null;
        }
        visualTransform.localPosition = targetPos;
    }

    public void UpdateProgress(int current, int target)
    {
        if (isCompleted) return;

        if (descriptionText != null)
        {
            if (target > 1)
                descriptionText.text = $"{baseDescription} (<color=#FFD700>{current}</color>/{target})";
            else
                descriptionText.text = baseDescription;
        }
    }

    public void CompleteMission()
    {
        if (isCompleted) return;
        isCompleted = true;

        if (titleText != null) titleText.text = $"<s>{titleText.text}</s>";
        if (descriptionText != null) descriptionText.text = $"{baseDescription} <color=#00FF00>(DONE)</color>";

        StartCoroutine(CompleteAnimationRoutine());
    }

    public void SetCompletedStateInstant()
    {
        isCompleted = true;
        if (titleText != null) titleText.text = $"<s>{titleText.text}</s>";
        if (descriptionText != null) descriptionText.text = $"{baseDescription} <color=#00FF00>(DONE)</color>";

        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    private IEnumerator CompleteAnimationRoutine()
    {
        if (backgroundImage != null)
        {
            Color originalColor = backgroundImage.color;
            backgroundImage.color = Color.white;
            yield return new WaitForSeconds(0.1f);

            float flashT = 0;
            while (flashT < 1)
            {
                flashT += Time.deltaTime * 3f;
                backgroundImage.color = Color.Lerp(Color.white, originalColor, flashT);
                yield return null;
            }
        }
    }
}