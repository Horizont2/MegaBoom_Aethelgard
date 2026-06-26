using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class AnimatedBootLogo : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Перетягни сюди об'єкт логотипу, на якому висить Canvas Group")]
    public CanvasGroup logoCanvasGroup;

    [Header("Animation Timings")]
    public float fadeInDuration = 1.5f;   // Скільки секунд лого з'являється
    public float stayDuration = 2.5f;     // Скільки секунд висить на екрані
    public float fadeOutDuration = 1.5f;  // Скільки секунд зникає

    [Header("Scene Transition")]
    [Tooltip("Точна назва сцени з твоєю катсценою або головним меню")]
    public string nextSceneName = "1_IntroCinematic";

    private void Start()
    {
        // Робимо лого невидимим на самому старті
        if (logoCanvasGroup != null)
        {
            logoCanvasGroup.alpha = 0f;
            StartCoroutine(PlayLogoSequence());
        }
    }

    private IEnumerator PlayLogoSequence()
    {
        // 1. ПЛАВНА ПОЯВА (Fade In)
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            logoCanvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeInDuration);
            yield return null; // Чекаємо наступного кадру
        }
        logoCanvasGroup.alpha = 1f;

        // 2. ПАУЗА (Щоб гравець встиг прочитати)
        yield return new WaitForSeconds(stayDuration);

        // 3. ПЛАВНЕ ЗНИКНЕННЯ (Fade Out)
        elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            logoCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeOutDuration);
            yield return null;
        }
        logoCanvasGroup.alpha = 0f;

        // 4. ЗАВАНТАЖЕННЯ НАСТУПНОЇ СЦЕНИ
        // Можеш додати коротку чорну паузу перед завантаженням, щоб було ще кінематографічніше
        yield return new WaitForSeconds(0.5f);

        SceneManager.LoadScene(nextSceneName);
    }
}