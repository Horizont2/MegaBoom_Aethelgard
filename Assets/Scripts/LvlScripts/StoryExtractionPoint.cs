using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

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
    public float horseHeightOffset = 0f;

    private bool isPlayerInRange = false;
    private bool isEvacuating = false;

    // UI �������� ��� �������������
    private CanvasGroup fadeGroup;
    private RectTransform topBar;
    private RectTransform bottomBar;

    private Vector3 shakePosOffset = Vector3.zero;

    private void Start()
    {
        CreateCinematicUI();
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
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_MOUNT_HORSE"));
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

        var brain = Camera.main.GetComponent<Unity.Cinemachine.CinemachineBrain>();
        if (brain != null) brain.enabled = false;

        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HidePrompt();
            GlobalHUD.Instance.SetGameplayPanelsActive(false);
            GlobalHUD.Instance.HideLevelObjective();
        }

        yield return StartCoroutine(FadeRoutine(1f, 0.4f));

        if (npcToHide != null) npcToHide.SetActive(false);

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            PlayerController pc = player.GetComponent<PlayerController>();
            if (pc != null) pc.isControlBlocked = true;
            player.SetActive(false);
        }

        if (riderDummy != null)
        {
            riderDummy.SetActive(true);

            // ���� ��'��� ������ �� � ��������, ������ ���� �����, ��� ���в����� ���� ������� �������
            if (riderDummy.transform.parent != horseTransform)
            {
                riderDummy.transform.SetParent(horseTransform, true);
            }

            // �������� Root Motion, ��� �������� �� �������� ��������
            Animator riderAnim = riderDummy.GetComponent<Animator>();
            if (riderAnim != null)
            {
                riderAnim.applyRootMotion = false;
            }
        }

        EnemyAI[] activeEnemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        foreach (EnemyAI enemy in activeEnemies)
        {
            if (enemy != null) enemy.target = horseTransform;
        }

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            CameraFollow camFollow = mainCam.GetComponent<CameraFollow>();
            if (camFollow != null)
            {
                camFollow.isCinematicMode = true;
                camFollow.enabled = false;
            }
            mainCam.fieldOfView = 45f;
        }

        if (dustVFXPrefab != null)
            Instantiate(dustVFXPrefab, horseTransform.position, Quaternion.identity, horseTransform);

        Animator horseAnim = horseTransform.GetComponentInChildren<Animator>();
        if (horseAnim != null) horseAnim.SetTrigger("Run");

        StartCoroutine(LetterboxRoutine(0.12f, 0.8f));
        yield return StartCoroutine(FadeRoutine(0f, 0.4f));

        Vector3 startPos = horseTransform.position;
        Vector3 targetPos = horseDestination != null ? horseDestination.position : (startPos + horseTransform.forward * 20f);

        float elapsed = 0f;
        float totalTime = Vector3.Distance(startPos, targetPos) / horseRunSpeed;
        bool hasImpacted = false;

        List<Collider> horseColliders = new List<Collider>();
        if (horseTransform != null) horseColliders.AddRange(horseTransform.GetComponentsInChildren<Collider>(true));
        if (riderDummy != null) horseColliders.AddRange(riderDummy.GetComponentsInChildren<Collider>(true));

        while (elapsed < totalTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / totalTime;

            Vector3 nextPos = Vector3.Lerp(startPos, targetPos, t * t);

            foreach (var col in horseColliders) if (col != null) col.enabled = false;

            if (Physics.Raycast(nextPos + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                nextPos.y = hit.point.y + horseHeightOffset;
            }

            foreach (var col in horseColliders) if (col != null) col.enabled = true;

            horseTransform.position = nextPos;

            if (horseDestination != null)
            {
                Vector3 lookDir = horseDestination.position - horseTransform.position;
                lookDir.y = 0;
                if (lookDir != Vector3.zero) horseTransform.rotation = Quaternion.LookRotation(lookDir);
            }

            if (mainCam != null && cinematicCameraPoint != null)
            {
                mainCam.transform.position = cinematicCameraPoint.position + shakePosOffset;
                mainCam.transform.rotation = cinematicCameraPoint.rotation;

                if (!hasImpacted)
                {
                    Vector3 horseToCam = cinematicCameraPoint.position - horseTransform.position;
                    float distanceAhead = Vector3.Dot(horseToCam, horseTransform.forward);

                    if (distanceAhead < 1.0f)
                    {
                        hasImpacted = true;
                        StartCoroutine(HeavyImpactRoutine(horseTransform.forward));
                    }
                }
            }

            yield return null;
        }

        float lingerDuration = 4.0f;
        float lingerElapsed = 0f;

        while (lingerElapsed < lingerDuration)
        {
            lingerElapsed += Time.deltaTime;

            Vector3 nextPos = horseTransform.position + horseTransform.forward * horseRunSpeed * Time.deltaTime;

            foreach (var col in horseColliders) if (col != null) col.enabled = false;

            if (Physics.Raycast(nextPos + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                nextPos.y = hit.point.y + horseHeightOffset;
            }

            foreach (var col in horseColliders) if (col != null) col.enabled = true;

            horseTransform.position = nextPos;

            if (mainCam != null && cinematicCameraPoint != null)
            {
                mainCam.transform.position = cinematicCameraPoint.position + shakePosOffset;
                mainCam.transform.rotation = cinematicCameraPoint.rotation;
            }

            yield return null;
        }

        Time.timeScale = 1f;

        yield return StartCoroutine(FadeRoutine(1f, 0.5f));

        if (ResourceManager.Instance != null) ResourceManager.Instance.EvacuateRunToStash();
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.FadeAndLoadScene("CampScene");
    }

    private IEnumerator HeavyImpactRoutine(Vector3 horseDirection)
    {
        Time.timeScale = 0.15f;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Player_HitResource);

        float duration = 0.6f;
        float elapsed = 0f;

        Vector3 impactPush = horseDirection * 0.8f - Vector3.up * 0.3f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;

            float springWobble = Mathf.Exp(-t * 6f) * Mathf.Sin(t * Mathf.PI * 12f);

            shakePosOffset = impactPush * springWobble;

            yield return null;
        }

        shakePosOffset = Vector3.zero;
        Time.timeScale = 1f;
    }

    private void CreateCinematicUI()
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

        fadeGroup = imageObj.AddComponent<CanvasGroup>();
        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = false;

        GameObject topObj = new GameObject("TopBar");
        topObj.transform.SetParent(canvasObj.transform, false);
        Image topImg = topObj.AddComponent<Image>();
        topImg.color = Color.black;
        topBar = topImg.GetComponent<RectTransform>();
        topBar.anchorMin = new Vector2(0, 1);
        topBar.anchorMax = new Vector2(1, 1);
        topBar.pivot = new Vector2(0.5f, 1);
        topBar.sizeDelta = new Vector2(0, 0);

        GameObject botObj = new GameObject("BottomBar");
        botObj.transform.SetParent(canvasObj.transform, false);
        Image botImg = botObj.AddComponent<Image>();
        botImg.color = Color.black;
        bottomBar = botImg.GetComponent<RectTransform>();
        bottomBar.anchorMin = new Vector2(0, 0);
        bottomBar.anchorMax = new Vector2(1, 0);
        bottomBar.pivot = new Vector2(0.5f, 0);
        bottomBar.sizeDelta = new Vector2(0, 0);
    }

    private IEnumerator FadeRoutine(float targetAlpha, float duration)
    {
        if (fadeGroup == null) CreateCinematicUI();

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

    private IEnumerator LetterboxRoutine(float targetHeightRatio, float duration)
    {
        if (topBar == null || bottomBar == null) yield break;

        float startHeight = topBar.sizeDelta.y;
        float targetHeight = Screen.height * targetHeightRatio;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            float smoothStep = t * t * (3f - 2f * t);
            float currentHeight = Mathf.Lerp(startHeight, targetHeight, smoothStep);

            topBar.sizeDelta = new Vector2(0, currentHeight);
            bottomBar.sizeDelta = new Vector2(0, currentHeight);
            yield return null;
        }

        topBar.sizeDelta = new Vector2(0, targetHeight);
        bottomBar.sizeDelta = new Vector2(0, targetHeight);
    }
}