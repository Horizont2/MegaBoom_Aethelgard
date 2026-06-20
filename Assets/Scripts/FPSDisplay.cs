using UnityEngine;
using TMPro;

public class FPSDisplay : MonoBehaviour
{
    public static FPSDisplay Instance;
    private TextMeshProUGUI fpsText;
    private float deltaTime = 0.0f;
    private float displayTimer = 0f;
    private const float DisplayInterval = 0.25f;

    private void Awake()
    {
        Instance = this;
        fpsText = GetComponent<TextMeshProUGUI>();
    }

    private void Start()
    {
        // ���������� ���� ��� ������������ �����
        UpdateVisibility();
    }

    private void Update()
    {
        // Smooth FPS calculation
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;

        // Throttle text update to 4 times per second (avoids costly TMP rebuild every frame)
        displayTimer += Time.unscaledDeltaTime;
        if (displayTimer < DisplayInterval) return;
        displayTimer = 0f;

        if (fpsText == null || !fpsText.enabled) return;
        float fps = 1.0f / deltaTime;
        fpsText.text = $"{Mathf.CeilToInt(fps)} FPS";
    }

    public void UpdateVisibility()
    {
        // ������� ��� �������� ��'��� ������� �� �����������
        bool isEnabled = PlayerPrefs.GetInt("Settings_ShowFPS", 0) == 1;

        if (fpsText != null) fpsText.enabled = isEnabled;
    }
}