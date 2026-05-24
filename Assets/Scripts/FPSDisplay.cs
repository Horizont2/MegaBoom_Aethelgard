using UnityEngine;
using TMPro;

public class FPSDisplay : MonoBehaviour
{
    public static FPSDisplay Instance;
    private TextMeshProUGUI fpsText;
    private float deltaTime = 0.0f;

    private void Awake()
    {
        Instance = this;
        fpsText = GetComponent<TextMeshProUGUI>();
    }

    private void Start()
    {
        // Перевіряємо стан при завантаженні сцени
        UpdateVisibility();
    }

    private void Update()
    {
        // Розрахунок FPS через незмінений час (працює на паузі)
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        float fps = 1.0f / deltaTime;

        // Просто виводимо значення без агресивних HTML-тегів.
        // Колір тепер налаштовується прямо в Інспекторі Unity!
        fpsText.text = $"{Mathf.CeilToInt(fps)} FPS";
    }

    public void UpdateVisibility()
    {
        // Вмикаємо або вимикаємо об'єкт залежно від налаштувань
        bool isEnabled = PlayerPrefs.GetInt("Settings_ShowFPS", 0) == 1;

        if (fpsText != null) fpsText.enabled = isEnabled;
    }
}