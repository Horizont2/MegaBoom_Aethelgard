using UnityEngine;

public class WeatherController : MonoBehaviour
{
    [Header("Weather States")]
    public float weatherChangeInterval = 60f; // Змінювати погоду кожні 60 секунд
    public float transitionSpeed = 0.5f;      // Швидкість плавного переходу

    [Header("Sunny Settings")]
    public float sunnyLightIntensity = 1.5f;
    public float sunnyFogDensity = 0.002f;
    public Color sunnyFogColor = new Color(0.9f, 0.95f, 1f);

    [Header("Foggy Settings")]
    public float foggyLightIntensity = 0.6f;
    public float foggyFogDensity = 0.015f;
    public Color foggyFogColor = new Color(0.6f, 0.6f, 0.65f);

    private Light sun;
    private bool isSunny = true;
    private float timer = 0f;

    private void Start()
    {
        sun = GetComponent<Light>();

        // Встановлюємо початкову погоду
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
    }

    private void Update()
    {
        // Таймер зміни погоди
        timer += Time.deltaTime;
        if (timer >= weatherChangeInterval)
        {
            isSunny = !isSunny; // Перемикаємо стан
            timer = 0f;
            GameLog.Info("Weather changed! Is Sunny: " + isSunny);
        }

        // Визначаємо цільові значення
        float targetIntensity = isSunny ? sunnyLightIntensity : foggyLightIntensity;
        float targetDensity = isSunny ? sunnyFogDensity : foggyFogDensity;
        Color targetColor = isSunny ? sunnyFogColor : foggyFogColor;

        // Плавно переходимо до нових значень
        sun.intensity = Mathf.Lerp(sun.intensity, targetIntensity, transitionSpeed * Time.deltaTime);
        RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, targetDensity, transitionSpeed * Time.deltaTime);
        RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, targetColor, transitionSpeed * Time.deltaTime);
    }
}