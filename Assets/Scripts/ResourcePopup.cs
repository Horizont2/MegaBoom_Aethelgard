using UnityEngine;
using TMPro;

public class ResourcePopup : MonoBehaviour
{
    private TextMeshProUGUI popupText;
    private RectTransform rectTransform;
    private Vector2 startPos;

    [Header("Settings")]
    public float displayTime = 1.5f; // ������ ������ ������ �����
    public float floatSpeed = 40f;   // �������� ������� �����

    private int accumulatedValue = 0;
    private float currentTimer = 0f;

    private void Awake()
    {
        popupText = GetComponent<TextMeshProUGUI>();
        rectTransform = GetComponent<RectTransform>();
        startPos = rectTransform.anchoredPosition;

        // ������ ����� �� �����
        Color c = popupText.color;
        c.a = 0f;
        popupText.color = c;
    }

    public void ShowChange(int amount)
    {
        if (amount == 0) return;

        // ���� ����� ��� ����, ������� �������� � �������
        if (currentTimer <= 0)
        {
            accumulatedValue = 0;
            rectTransform.anchoredPosition = startPos;
        }

        // ��������Ӫ�� ���� �������� �� ������� (���� �����������!)
        accumulatedValue += amount;

        // ��������� ���� � ����
        if (accumulatedValue > 0)
        {
            popupText.text = $"+{accumulatedValue}";
            popupText.color = Color.green;
        }
        else if (accumulatedValue < 0)
        {
            popupText.text = $"{accumulatedValue}"; // ̳��� ��� � � ������ ����
            popupText.color = Color.red;
        }
        else
        {
            // ���� ������ ������ ���� (+10 � -10)
            popupText.color = new Color(0, 0, 0, 0);
            currentTimer = 0;
            return;
        }

        // ������� ������ � ��������� ����� �� �������� ������� ��� ������ "�����/���������"
        currentTimer = displayTime;
        rectTransform.anchoredPosition = startPos;

        // ������ �������� �������
        Color visibleColor = popupText.color;
        visibleColor.a = 1f;
        popupText.color = visibleColor;
    }

    private void Update()
    {
        if (currentTimer > 0)
        {
            // unscaledDeltaTime so the fade still ticks when Time.timeScale
            // = 0 (cutscenes, scene loads). Previously a popup mid-display
            // when timeScale dropped to 0 froze at full alpha and stayed
            // visible across scene changes.
            currentTimer -= Time.unscaledDeltaTime;
            rectTransform.anchoredPosition += Vector2.up * floatSpeed * Time.unscaledDeltaTime;

            // Fade Out in the last 0.5s
            if (currentTimer < 0.5f)
            {
                Color c = popupText.color;
                c.a = Mathf.Max(0f, currentTimer / 0.5f);
                popupText.color = c;
            }

            // When the timer just hit zero, snap to fully invisible.
            if (currentTimer <= 0f)
            {
                Color c = popupText.color;
                c.a = 0f;
                popupText.color = c;
                accumulatedValue = 0;
                rectTransform.anchoredPosition = startPos;
            }
        }
    }

    // Hard reset — called by ResourceManager when the active scene
    // changes so a popup half-way through fading doesn't stick at full
    // alpha on the next scene's HUD.
    public void ForceHide()
    {
        currentTimer = 0f;
        accumulatedValue = 0;
        if (popupText != null)
        {
            Color c = popupText.color;
            c.a = 0f;
            popupText.color = c;
        }
        if (rectTransform != null)
            rectTransform.anchoredPosition = startPos;
    }
}