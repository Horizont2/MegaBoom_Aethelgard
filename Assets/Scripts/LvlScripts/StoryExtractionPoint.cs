using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class StoryExtractionPoint : MonoBehaviour
{
    [Header("Cinematic References")]
    public Transform horseTransform;
    public GameObject dustVFXPrefab;
    public GameObject riderDummy;

    [Header("Cinematic Extras")]
    public GameObject npcToHide;

    [Header("Manual Cinematic Path")]
    public Transform cinematicCameraPoint;
    public Transform horseDestination;

    [Header("Debug")]
    public bool forcePlayOnStart = false;

    [Header("Cinematic Settings")]
    public float horseRunSpeed = 12f;

    private bool isPlayerInRange = false;
    private bool isEvacuating = false;
    private CanvasGroup fadeGroup;

    // Глобальна змінна тільки для зсуву позиції від удару
    private Vector3 shakePosOffset = Vector3.zero;

    private void Start()
    {
        CreateFadeOverlay();
        if (forcePlayOnStart) StartCoroutine(DebugStartRoutine());
    }

    private IEnumerator DebugStartRoutine()
    {
        yield return new WaitForSeconds(0.1f);

        var brain = Camera.main.GetComponent<Unity.Cinemachine.CinemachineBrain>();
        if (brain != null) brain.enabled = false;

        if (Level1_QuestManager.Instance != null && Level1_QuestManager.Instance.introDirector != null)
        {
            Level1_QuestManager.Instance.introDirector.Stop();
        }

        yield return new WaitForSeconds(0.9f);
        StartCoroutine(CinematicEscapeRoutine());
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !isEvacuating)
        {
            isPlayerInRange = true;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt("[E] Mount Horse & Escape");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInRange = false;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
        }
    }

    private void Update()
    {
        if (!forcePlayOnStart && isPlayerInRange && !isEvacuating && Input.GetKeyDown(KeyCode.E))
        {
            StartCoroutine(CinematicEscapeRoutine());
        }
    }

    private IEnumerator CinematicEscapeRoutine()
    {
        isEvacuating = true;

        // --- ПРИБИРАЄМО АБСОЛЮТНО ВЕСЬ UI ---
        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HidePrompt();
            GlobalHUD.Instance.SetGameplayPanelsActive(false);
            GlobalHUD.Instance.HideLevelObjective(); // Ховаємо табличку квесту
        }

        // --- 1. ПЛАВНЕ ЗАТЕМНЕННЯ ---
        yield return StartCoroutine(FadeRoutine(1f, 0.4f));

        // --- 2. ПІДГОТОВКА СЦЕНИ ---
        if (npcToHide != null) npcToHide.SetActive(false);

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            PlayerController pc = player.GetComponent<PlayerController>();
            if (pc != null) pc.isControlBlocked = true;
            player.SetActive(false);
        }

        if (riderDummy != null) riderDummy.SetActive(true);

        EnemyAI[] activeEnemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        foreach (EnemyAI enemy in activeEnemies)
        {
            if (enemy != null) enemy.target = horseTransform;
        }

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            CameraFollow camFollow = mainCam.GetComponent<CameraFollow>();
            if (camFollow != null) camFollow.isCinematicMode = true;

            mainCam.fieldOfView = 45f;
        }

        if (dustVFXPrefab != null)
            Instantiate(dustVFXPrefab, horseTransform.position, Quaternion.identity, horseTransform);

        Animator horseAnim = horseTransform.GetComponentInChildren<Animator>();
        if (horseAnim != null) horseAnim.SetTrigger("Run");

        // if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Horse_Gallop);

        // --- 3. ПЛАВНИЙ ПРОЯВ ---
        yield return StartCoroutine(FadeRoutine(0f, 0.4f));

        // --- 4. ЗАПУСК РУХУ КОНЯ ---
        Vector3 startPos = horseTransform.position;
        Vector3 targetPos = horseDestination != null ? horseDestination.position : (startPos + horseTransform.forward * 20f);

        float elapsed = 0f;
        float totalTime = Vector3.Distance(startPos, targetPos) / horseRunSpeed;
        bool hasImpacted = false;

        while (elapsed < totalTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / totalTime;

            // Рух коня
            Vector3 nextPos = Vector3.Lerp(startPos, targetPos, t * t);
            if (Terrain.activeTerrain != null)
            {
                nextPos.y = Terrain.activeTerrain.SampleHeight(nextPos) + Terrain.activeTerrain.transform.position.y;
            }
            horseTransform.position = nextPos;

            if (horseDestination != null)
            {
                Vector3 lookDir = horseDestination.position - horseTransform.position;
                lookDir.y = 0;
                if (lookDir != Vector3.zero) horseTransform.rotation = Quaternion.LookRotation(lookDir);
            }

            // Жорстка фіксація камери + трясучка
            if (mainCam != null && cinematicCameraPoint != null)
            {
                mainCam.transform.position = cinematicCameraPoint.position + shakePosOffset;
                mainCam.transform.rotation = cinematicCameraPoint.rotation; // Камера не крутиться!

                if (!hasImpacted)
                {
                    // ФІКС: Математичне визначення, коли коняча морда перетинає об'єктив
                    Vector3 horseToCam = cinematicCameraPoint.position - horseTransform.position;
                    float distanceAhead = Vector3.Dot(horseToCam, horseTransform.forward);

                    // Як тільки кінь опиняється менш ніж за 1 метр перед камерою
                    if (distanceAhead < 1.0f)
                    {
                        hasImpacted = true;
                        StartCoroutine(HeavyImpactRoutine(horseTransform.forward));
                    }
                }
            }

            yield return null;
        }

        // --- 5. ПРОДОВЖЕННЯ СЦЕНИ (Скелети біжать далі) ---
        // Замість телепорту, кінь просто скаче вперед ще 4 секунди
        float lingerDuration = 4.0f;
        float lingerElapsed = 0f;

        while (lingerElapsed < lingerDuration)
        {
            lingerElapsed += Time.deltaTime;

            // Кінь продовжує свій біг
            Vector3 nextPos = horseTransform.position + horseTransform.forward * horseRunSpeed * Time.deltaTime;
            if (Terrain.activeTerrain != null)
            {
                nextPos.y = Terrain.activeTerrain.SampleHeight(nextPos) + Terrain.activeTerrain.transform.position.y;
            }
            horseTransform.position = nextPos;

            // Камера продовжує стояти на місці (і дотрясується, якщо ще треба)
            if (mainCam != null && cinematicCameraPoint != null)
            {
                mainCam.transform.position = cinematicCameraPoint.position + shakePosOffset;
                mainCam.transform.rotation = cinematicCameraPoint.rotation;
            }

            yield return null;
        }

        Time.timeScale = 1f;

        // --- 6. ФІНАЛЬНЕ ЗАТЕМНЕННЯ ПЕРЕД ТАБОРОМ ---
        yield return StartCoroutine(FadeRoutine(1f, 0.5f));

        if (ResourceManager.Instance != null) ResourceManager.Instance.EvacuateRunToStash();
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene("CampScene");
    }

    // --- ФІЗИЧНА ТРЯСУЧКА (ТІЛЬКИ ПОЗИЦІЯ) ---
    private IEnumerator HeavyImpactRoutine(Vector3 horseDirection)
    {
        Time.timeScale = 0.15f; // Різке кінематографічне сповільнення

        // Звук зламаної гілки / удару (він у тебе є, тому я його увімкнув)
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_HitResource);

        float duration = 0.6f;
        float elapsed = 0f;

        // Посилений вектор удару (вітер від коня)
        Vector3 impactPush = horseDirection * 0.8f - Vector3.up * 0.3f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;

            // Затухаюча синусоїда
            float springWobble = Mathf.Exp(-t * 6f) * Mathf.Sin(t * Mathf.PI * 12f);

            // Зміщуємо лише позицію, без жодного повороту!
            shakePosOffset = impactPush * springWobble;

            yield return null;
        }

        shakePosOffset = Vector3.zero;
        Time.timeScale = 1f;
    }

    // --- СИСТЕМА ЗАТЕМНЕННЯ ЕКРАНУ ---
    private void CreateFadeOverlay()
    {
        if (fadeGroup != null) return;

        GameObject canvasObj = new GameObject("CinematicFadeCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        GameObject imageObj = new GameObject("FadeImage");
        imageObj.transform.SetParent(canvasObj.transform, false);
        Image img = imageObj.AddComponent<Image>();
        img.color = Color.black;

        RectTransform rt = img.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        fadeGroup = canvasObj.AddComponent<CanvasGroup>();
        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = false;
    }

    private IEnumerator FadeRoutine(float targetAlpha, float duration)
    {
        if (fadeGroup == null) CreateFadeOverlay();

        float startAlpha = fadeGroup.alpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            yield return null;
        }

        fadeGroup.alpha = targetAlpha;
    }
}