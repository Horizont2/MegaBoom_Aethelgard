using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

public class ActiveBuildWidget : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI timerText;
    public Slider progressSlider;
    public Image buildingIcon;
    public RectTransform hammerIcon;

    private string buildingID;
    private DateTime endTime;
    private float totalDuration;
    private bool isActive = false;
    private bool isAnimatingExit = false;

    public void Setup(string bId, string bName, Sprite bIcon, DateTime targetTime, float duration)
    {
        buildingID = bId;
        if (titleText != null) titleText.text = LocalizationManager.Tr(bName);
        if (buildingIcon != null && bIcon != null) buildingIcon.sprite = bIcon;

        endTime = targetTime;
        totalDuration = duration;
        isActive = true;
    }

    private void Update()
    {
        if (!isActive) return;

        TimeSpan remaining = endTime - DateTime.UtcNow;
        float remainingSeconds = (float)remaining.TotalSeconds;

        if (remainingSeconds <= 0 && !isAnimatingExit)
        {
            isActive = false;

            // 1. ����������� ���������� ������ �� �����, �� ��� ������
            PlayerPrefs.SetInt("IsUpgrading_" + buildingID, 0);
            PlayerPrefs.SetInt("UpgradeFinished_" + buildingID, 1);
            PlayerPrefs.Save();

            // 2. ��������� ���� � ���'�� HUD � �������� �����
            if (GlobalHUD.Instance != null)
            {
                GlobalHUD.Instance.RemoveUpgradeFromList(buildingID);
                if (titleText != null)
                    GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("TOAST_BUILDING_UPGRADED", titleText.text));
            }

            // 3. ³��������� ���� �������� ������
            StartCoroutine(ExitAnimationRoutine());
            return;
        }

        if (timerText != null) timerText.text = string.Format("{0:00}:{1:00}", Mathf.Max(0, remaining.Minutes), Mathf.Max(0, remaining.Seconds));
        if (progressSlider != null) progressSlider.value = 1f - (Mathf.Max(0, remainingSeconds) / totalDuration);

        if (hammerIcon != null)
        {
            float angle = Mathf.Sin(Time.time * 15f) * 20f;
            hammerIcon.localRotation = Quaternion.Euler(0, 0, angle);
        }
    }

    private IEnumerator ExitAnimationRoutine()
    {
        isAnimatingExit = true;
        RectTransform rect = GetComponent<RectTransform>();
        Vector2 startPos = rect.anchoredPosition;

        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime * 6f;
            float curve = Mathf.Sin(t * Mathf.PI * 0.5f);
            rect.anchoredPosition = Vector2.Lerp(startPos, startPos + new Vector2(-40f, 0), curve);
            yield return null;
        }

        t = 0;
        Vector2 midPos = rect.anchoredPosition;
        while (t < 1f)
        {
            t += Time.deltaTime * 3f;
            float curve = t * t * t;
            rect.anchoredPosition = Vector2.Lerp(midPos, midPos + new Vector2(600f, 0), curve);

            CanvasGroup cg = GetComponent<CanvasGroup>();
            if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 1f - t;

            yield return null;
        }

        Destroy(gameObject);
    }
}