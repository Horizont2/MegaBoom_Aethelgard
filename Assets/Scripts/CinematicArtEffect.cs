using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CinematicArtEffect : MonoBehaviour
{
    [Header("Ken Burns (Повільний Зум)")]
    [Tooltip("Швидкість наближення картинки")]
    public float zoomSpeed = 0.015f;
    [Tooltip("Максимальний масштаб, до якого картинка збільшиться")]
    public float maxZoom = 1.12f;

    [Header("Cinematic Panning (Зсув камери)")]
    [Tooltip("Напрямок руху картинки (наприклад, X: 15, Y: 5 — легкий рух вбік і вгору)")]
    public Vector2 panDirection = new Vector2(12f, 4f);
    public float panSpeed = 0.4f;

    [Header("Camera Breathing (Дихання камери)")]
    public bool enableBreathing = true;
    [Tooltip("Швидкість коливання (дихання)")]
    public float breathingSpeed = 1.2f;
    [Tooltip("Амплітуда коливання в пікселях (наскільки сильно качає екраном)")]
    public float breathingAmount = 4f;

    private RectTransform rectTransform;
    private Vector3 initialScale;
    private Vector2 initialPosition;
    private float currentZoom = 1f;
    private float timeElapsed = 0f;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            initialScale = rectTransform.localScale;
            initialPosition = rectTransform.anchoredPosition;
        }
    }

    private void OnEnable()
    {
        // КОЖЕН РАЗ, коли Timeline вмикає цей слайд, анімація скидається в початок
        currentZoom = 1f;
        timeElapsed = 0f;

        if (rectTransform != null)
        {
            rectTransform.localScale = initialScale;
            rectTransform.anchoredPosition = initialPosition;
        }
    }

    private void Update()
    {
        if (rectTransform == null) return;

        // Використовуємо deltaTime для плавності
        timeElapsed += Time.deltaTime;

        // 1. ЕФЕКТ КЕНА БЕРНСА (Плавне наближення)
        currentZoom = Mathf.MoveTowards(currentZoom, maxZoom, zoomSpeed * Time.deltaTime);
        rectTransform.localScale = initialScale * currentZoom;

        // 2. ПАНОРАМУВАННЯ (Повільний зсув у заданому напрямку)
        Vector2 targetPan = initialPosition + (panDirection * (timeElapsed * panSpeed * 0.1f));

        // 3. ЖИВЕ ДИХАННЯ КАМЕРИ (Математична синусоїда для ефекту рук оператора)
        if (enableBreathing)
        {
            float waveX = Mathf.Sin(timeElapsed * breathingSpeed) * breathingAmount;
            // Косинус із невеликим зсувом фази (0.7f), щоб рух був хаотичним коловим, а не просто по діагоналі
            float waveY = Mathf.Cos(timeElapsed * breathingSpeed * 0.7f) * breathingAmount;

            rectTransform.anchoredPosition = targetPan + new Vector2(waveX, waveY);
        }
        else
        {
            rectTransform.anchoredPosition = targetPan;
        }
    }
}