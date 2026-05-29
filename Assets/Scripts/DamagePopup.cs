using UnityEngine;
using TMPro;
using System.Collections;

public class DamagePopup : MonoBehaviour
{
    [Header("Popup Settings")]
    public TextMeshPro textMesh;
    public float lifetime = 1f;

    [Header("Visual Styles")]
    public Color normalColor = Color.white;
    public Color critColor = new Color(1f, 0.8f, 0f);
    public float normalSize = 5f;
    public float critSize = 8f;

    private Color textColor;
    private Transform camTransform;

    private void Awake()
    {
        if (textMesh == null) textMesh = GetComponent<TextMeshPro>();
    }

    private void Start()
    {
        if (Camera.main != null) camTransform = Camera.main.transform;
    }

    public void Setup(float damageAmount, bool isCrit = false)
    {
        // Додаємо знак мінуса перед цифрою
        textMesh.text = "-" + Mathf.CeilToInt(damageAmount).ToString();

        // Застосовуємо твої кольори та розміри
        if (isCrit)
        {
            textMesh.text += "!";
            textMesh.color = critColor;
            textMesh.fontSize = critSize;
        }
        else
        {
            textMesh.color = normalColor;
            textMesh.fontSize = normalSize;
        }

        textColor = textMesh.color;

        // Початковий мікро-зсув, щоб цифри не злипалися в одній точці, якщо ударів багато
        float randomX = Random.Range(-0.5f, 0.5f);
        float randomZ = Random.Range(-0.5f, 0.5f);
        transform.position += new Vector3(randomX, 1f, randomZ);

        // Починаємо з нульового масштабу для ефекту появи (Pop)
        transform.localScale = Vector3.zero;

        // Запускаємо соковиту анімацію
        StartCoroutine(AnimatePopupRoutine(isCrit));
    }

    private IEnumerator AnimatePopupRoutine(bool isCrit)
    {
        // Крит висить на екрані трохи довше
        float duration = isCrit ? lifetime + 0.5f : lifetime;
        float elapsed = 0f;

        Vector3 startPos = transform.position;

        // Випадковий напрямок розльоту вбік і трохи вперед/назад
        Vector3 randomDir = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;

        // Критичні удари відлітають далі
        Vector3 targetPos = startPos + randomDir * (isCrit ? 2.5f : 1.5f);

        Vector3 baseScale = Vector3.one;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration; // Прогрес від 0 до 1

            // 1. Рух по дузі (Lerp + Sin для ефекту підстрибування/гравітації)
            Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
            currentPos.y += Mathf.Sin(t * Mathf.PI) * 2f; // Висота дуги

            // 2. Мікро-трясіння для крита на самому початку (ефект сильного імпакту)
            if (isCrit && t < 0.2f)
            {
                currentPos += (Vector3)Random.insideUnitCircle * 0.15f;
            }

            transform.position = currentPos;

            // 3. Скейлінг (Juicy Pop Effect: різко збільшується, пружинить, плавно до норми)
            float scaleCurve;
            if (t < 0.15f) scaleCurve = Mathf.Lerp(0f, 1.3f, t / 0.15f);
            else if (t < 0.3f) scaleCurve = Mathf.Lerp(1.3f, 1f, (t - 0.15f) / 0.15f);
            else scaleCurve = 1f;

            transform.localScale = baseScale * scaleCurve;

            // 4. Плавне згасання прозорості у другій половині анімації
            if (t > 0.5f)
            {
                textColor.a = Mathf.Lerp(1f, 0f, (t - 0.5f) / 0.5f);
                textMesh.color = textColor;
            }

            // 5. Завжди дивимося прямо в камеру
            if (camTransform != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - camTransform.position);
            }

            yield return null;
        }

        // Знищуємо об'єкт після завершення анімації
        ObjectPoolManager.Instance.ReturnToPool(gameObject);
    }
}