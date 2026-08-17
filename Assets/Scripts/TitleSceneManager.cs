using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class TitleSceneManager : MonoBehaviour
{
    [Header("UI Groups")]
    [Tooltip("Суцільний чорний екран, який закриває ВСЕ на початку і в кінці")]
    public CanvasGroup globalFadeScreen;
    public CanvasGroup mainTitleGroup; // Група з Hollow Siege
    public CanvasGroup pressAnyKeyGroup; // Група з підказкою

    [Header("FX References")]
    [Tooltip("Перетягни сюди свою систему частинок попелу (Ash_Particles)")]
    public ParticleSystem ashParticles;

    [Header("Animation Settings")]
    [Tooltip("Як довго розсіюється чорний екран на старті")]
    public float sceneFadeInDuration = 2.0f;
    [Tooltip("Як довго назва повільно з'являється з темряви на старті")]
    public float titleFadeInDuration = 4.0f;
    [Tooltip("Затримка перед тим, як з'явиться підказка натиснути кнопку")]
    public float promptDelay = 2.0f;
    [Tooltip("Як довго все згасає після натискання")]
    public float fadeOutDuration = 2.5f;

    [Header("Audio")]
    [Tooltip("Музика, яка грає на фоні")]
    public AudioSource bgmSource;
    [Tooltip("Звук удару при натисканні (Cinematic Boom)")]
    public AudioSource clickBoomSource;

    [Header("Epic Zoom Effect")]
    [Tooltip("Об'єкт самої назви (для постійного зуму). Якщо порожньо - візьме з mainTitleGroup")]
    public RectTransform titleTransform;
    [Tooltip("Швидкість наближення назви в стані спокою")]
    public float slowZoomSpeed = 0.015f;
    [Tooltip("У скільки разів прискорити наближення після натискання клавіші")]
    public float zoomMultiplierOnPress = 5f;

    [Header("Scene Transition")]
    public string nextSceneName = "Menu"; // Заміни на назву своєї сцени в Інспекторі

    private bool isTransitioning = false;
    private float currentZoom = 1f;

    private void Start()
    {
        // На старті ховаємо текст, а чорний екран робимо повністю непрозорим
        if (mainTitleGroup != null) mainTitleGroup.alpha = 0f;
        if (pressAnyKeyGroup != null) pressAnyKeyGroup.alpha = 0f;

        if (globalFadeScreen != null)
        {
            globalFadeScreen.alpha = 1f;
            globalFadeScreen.blocksRaycasts = true; // Блокуємо кліки, поки йде затемнення
        }

        if (titleTransform == null && mainTitleGroup != null)
        {
            titleTransform = mainTitleGroup.GetComponent<RectTransform>();
        }

        // Перевіряємо, чи увімкнені частинки, і запускаємо їх
        if (ashParticles != null && !ashParticles.isPlaying)
        {
            ashParticles.Play();
        }

        // Запускаємо красиву появу
        StartCoroutine(EntranceRoutine());
    }

    private void Update()
    {
        // 1. ПОСТІЙНИЙ ЕФЕКТ "ДИХАННЯ" (Повільний зум назви)
        if (titleTransform != null)
        {
            currentZoom += slowZoomSpeed * Time.deltaTime;
            titleTransform.localScale = new Vector3(currentZoom, currentZoom, 1f);
        }

        // 2. ЧЕКАЄМО НАТИСКАННЯ
        if (!isTransitioning && Input.anyKeyDown)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // On WebGL the first tap is the audio-unlock gesture consumed by
            // WebGLStartGate. Ignore that exact tap here, otherwise the title
            // transitions to the menu before it ever fades in.
            if (WebGLStartGate.BlockingInput) return;
#endif
            if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1))
            {
                StartTransition();
            }
        }
    }

    private IEnumerator EntranceRoutine()
    {
        // Етап 1: Плавно розсіюємо чорну завісу
        if (globalFadeScreen != null)
        {
            float elapsedFade = 0f;
            while (elapsedFade < sceneFadeInDuration)
            {
                elapsedFade += Time.deltaTime;
                globalFadeScreen.alpha = Mathf.Lerp(1f, 0f, elapsedFade / sceneFadeInDuration);
                yield return null;
            }
            globalFadeScreen.alpha = 0f;
            globalFadeScreen.blocksRaycasts = false;
        }

        // Етап 2: Дуже плавна поява головної назви
        if (mainTitleGroup != null)
        {
            float elapsed = 0f;
            while (elapsed < titleFadeInDuration)
            {
                elapsed += Time.deltaTime;
                mainTitleGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / titleFadeInDuration);
                yield return null;
            }
            mainTitleGroup.alpha = 1f;
        }

        // Етап 3: Чекаємо пару секунд, щоб гравець насолодився логотипом
        yield return new WaitForSeconds(promptDelay);

        // Етап 4: Плавна поява підказки
        if (pressAnyKeyGroup != null)
        {
            float elapsed = 0f;
            while (elapsed < 1.0f)
            {
                elapsed += Time.deltaTime;
                pressAnyKeyGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / 1.0f);
                yield return null;
            }
            pressAnyKeyGroup.alpha = 1f;
        }
    }

    public void StartTransition()
    {
        isTransitioning = true;
        StartCoroutine(FadeOutAndLoad());
    }

    private IEnumerator FadeOutAndLoad()
    {
        if (pressAnyKeyGroup != null) pressAnyKeyGroup.gameObject.SetActive(false);

        // Вмикаємо звук удару
        if (clickBoomSource != null) clickBoomSource.Play();

        // Зупиняємо створення нового попелу
        if (ashParticles != null)
        {
            var emission = ashParticles.emission;
            emission.enabled = false;
        }

        slowZoomSpeed *= zoomMultiplierOnPress;

        // Запускаємо згасання музики МИТТЄВО
        float startVolume = bgmSource != null ? bgmSource.volume : 0f;
        float totalFadeTime = 0.4f + fadeOutDuration;
        StartCoroutine(FadeMusicOut(startVolume, totalFadeTime));

        // 🔥 СЕКРЕТНИЙ ІНГРЕДІЄНТ: Починаємо завантажувати сцену у ФОНІ
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(nextSceneName);
        // Failsafe: якщо сцени немає в Build Settings, LoadSceneAsync
        // поверне null. Не залишаємо гравця застряглим на титрах —
        // вантажимо напряму.
        if (asyncLoad == null)
        {
            Debug.LogError($"[TitleScene] Scene '{nextSceneName}' is not in Build Settings — loading directly.");
            SceneManager.LoadScene(nextSceneName);
            yield break;
        }
        // Забороняємо Unity перемикатися на неї, поки ми не дозволимо
        asyncLoad.allowSceneActivation = false;

        // Даємо назві поїхати вперед (Realtime — не залежить від timeScale)
        yield return new WaitForSecondsRealtime(0.4f);

        // Плавно повертаємо чорний екран
        if (globalFadeScreen != null)
        {
            globalFadeScreen.blocksRaycasts = true;
            float elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                globalFadeScreen.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeOutDuration);
                yield return null;
            }
            globalFadeScreen.alpha = 1f;
        }

        // Чекаємо, поки епічний Subboom дограє свій хвіст — АЛЕ з жорстким
        // лімітом. На WebGL AudioSource.isPlaying може «залипнути» на true
        // назавжди, і тоді старий WaitWhile ніколи не завершувався: сцена
        // вантажилась на 90% і НІКОЛИ не активувалась — гравець застрягав
        // на чорному екрані. Обмежуємо очікування.
        float boomWait = 0f;
        const float maxBoomWait = 2f;
        while (clickBoomSource != null && clickBoomSource.isPlaying && boomWait < maxBoomWait)
        {
            boomWait += Time.unscaledDeltaTime;
            yield return null;
        }

        yield return new WaitForSecondsRealtime(0.1f);

        // 🔥 МИТТЄВИЙ ПЕРЕХІД: Екран вже чорний, звук дограв, сцена завантажена. Дозволяємо активацію!
        asyncLoad.allowSceneActivation = true;
    }

    // Допоміжний метод для миттєвого старту згасання музики
    private IEnumerator FadeMusicOut(float startVolume, float duration)
    {
        if (bgmSource == null) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            bgmSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / duration);
            yield return null;
        }
        bgmSource.volume = 0f;
    }
}