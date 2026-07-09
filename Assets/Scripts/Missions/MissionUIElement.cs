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

    public Image backgroundImage; // �� ��� ��'��� Bg, ���� �� ������ ������

    [Header("Settings")]
    [Tooltip("������ �� Ҳ���� ��� ������� �� �� ����� (��� ���� ����� ��������)")]
    public bool animateAppearance = false;
    public float animationDuration = 0.5f;

    private CanvasGroup canvasGroup;
    public bool isCompleted = false;

    private string baseDescription = "";
    // Cache the background image's authored colour so we can restore it if
    // the CompleteMission flash coroutine gets StopAllCoroutines'd mid-flash
    // (which happened when Setup was called during the 0.5s white flash —
    // the flash never lerped back and the tile stayed pure white).
    private Color originalBgColor = Color.white;
    private bool originalBgCached = false;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();

        if (backgroundImage != null)
        {
            originalBgColor = backgroundImage.color;
            originalBgCached = true;
        }

        if (animateAppearance)
        {
            canvasGroup.alpha = 0f;
        }
    }

    public void Setup(string title, string description, int current, int target)
    {
        isCompleted = false;
        StopAllCoroutines();

        // Restore the background if a previous CompleteMission flash was
        // interrupted before its own lerp finished, otherwise the tile shows
        // the raw white flash sprite instead of the mission background.
        if (originalBgCached && backgroundImage != null)
            backgroundImage.color = originalBgColor;

        if (titleText != null) titleText.text = title;

        baseDescription = description;
        UpdateProgress(current, target);

        if (animateAppearance)
        {
            StartCoroutine(AppearRoutine());
            animateAppearance = false; // �������, ��� �������� ��������� �� ��������� ���� �����
        }
        else
        {
            if (canvasGroup != null) canvasGroup.alpha = 1f;
        }
    }

    private IEnumerator AppearRoutine()
    {
        // ��ò� ���: �� �� ������ �������� ��'��� (���� ����� Layout Group).
        // �� ������ ����� �������� ������� (Bg) � ������ ��!
        if (backgroundImage == null) yield break;

        Transform visualTransform = backgroundImage.transform;

        // ������� �������� ������� Bg (�������� �� 0,0,0)
        Vector3 targetPos = visualTransform.localPosition;

        // ³������� �� �� 300 ������ ���� ��� ������
        Vector3 startPos = targetPos + new Vector3(-300f, 0, 0);

        float t = 0;
        while (t < 1)
        {
            t += Time.deltaTime / animationDuration;
            float curve = Mathf.SmoothStep(0, 1, t);

            // ������ ���������� �������� ��'���
            if (canvasGroup != null) canvasGroup.alpha = curve;

            // ������ �������� �������
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