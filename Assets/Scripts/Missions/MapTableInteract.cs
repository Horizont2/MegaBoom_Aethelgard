using UnityEngine;
using System.Collections;

public class MapTableInteract : MonoBehaviour
{
    public static event System.Action OnMapFullyOpened;
    public static bool IsMapActive = false;

    [Header("Camera Flight Target")]
    public Transform mapCameraPosition;

    [Header("UI & Scene Objects")]
    public CanvasGroup mapCanvasGroup;
    public MapPanelUI mapPanelUI;
    public GameObject floatingIcon;

    [Header("Flight Settings (Juice)")]
    public float flightDuration = 1.5f;
    public float uiFadeDuration = 0.5f;

    private bool playerInRange = false;
    private bool isMapOpen = false;
    private bool isTransitioning = false;

    public bool IsMapOpen => isMapOpen;

    private PlayerController playerController;
    private CameraFollow cameraFollow;

    private Vector3 savedCamPos;
    private Quaternion savedCamRot;
    private Coroutine activeSequence;

    // Cached so opening the map is not a GetComponent. Null is fine and simply
    // means the group is not on a Canvas, in which case nothing is toggled.
    private Canvas _mapCanvas;
    private bool _mapCanvasResolved;

    private void SetMapCanvasEnabled(bool on)
    {
        if (!_mapCanvasResolved)
        {
            _mapCanvasResolved = true;
            if (mapCanvasGroup != null) _mapCanvas = mapCanvasGroup.GetComponent<Canvas>();
        }
        if (_mapCanvas != null) _mapCanvas.enabled = on;
    }

    private IEnumerator Start()
    {
        IsMapActive = false;
        if (mapCanvasGroup != null)
        {
            mapCanvasGroup.alpha = 0f;
            mapCanvasGroup.interactable = false;
            mapCanvasGroup.blocksRaycasts = false;
            mapCanvasGroup.gameObject.SetActive(true);

            // ==== AN INVISIBLE CANVAS IS STILL A CANVAS ====
            //
            // Alpha 0 is not culling. UGUI has no alpha-zero early-out, so the
            // map's 192 CanvasRenderers — 168 Images and 67 labels, including
            // full-screen backdrops — were still being laid out, batched and
            // blended every frame of every camp session, behind a nine-thousand
            // sorting order, for a screen nobody was looking at.
            //
            // And it was not idle: MapInteractiveViewer writes the map rect's
            // scale and position every frame, and twenty-four RegionUI instances
            // lerp a colour driven by Mathf.Sin — which never converges — so the
            // canvas was marked dirty and rebuilt on every single frame.
            //
            // Disabling the Canvas COMPONENT stops the rebuild and the draw
            // while leaving every object alive, so nothing's Awake, state or
            // coroutine changes. Re-enabled the moment the map opens.
            SetMapCanvasEnabled(false);
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerController = player.GetComponent<PlayerController>();

        if (Camera.main != null) cameraFollow = Camera.main.GetComponent<CameraFollow>();

        if (PlayerPrefs.GetInt("AutoOpenMap", 0) == 1)
        {
            PlayerPrefs.SetInt("AutoOpenMap", 0);
            PlayerPrefs.Save();

            yield return new WaitForSeconds(1.5f);
            activeSequence = StartCoroutine(OpenMapSequence());
        }
    }

    private void Update()
    {
        // === ОПТИМІЗАЦІЯ І ФІКС: Блокуємо стіл карти під час туторіалу ===
        if (isTransitioning || TutorialPanelUI.IsTutorialActive) return;

        if (playerInRange && !isMapOpen && Input.GetKeyDown(KeyCode.E))
        {
            int lodgeLevel = PlayerPrefs.GetInt("SaveBld_ScoutsLodge", 1);

            if (lodgeLevel < 2)
            {
                if (GlobalHUD.Instance != null)
                {
                    GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_UPGRADE_ELIAS_FIRST"));
                }
                return;
            }

            activeSequence = StartCoroutine(OpenMapSequence());
        }
        else if (isMapOpen && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.E)))
        {
            activeSequence = StartCoroutine(CloseMapSequence());
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !isMapOpen)
        {
            playerInRange = true;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_OPEN_MAP"));
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            if (GlobalHUD.Instance != null && !isMapOpen) GlobalHUD.Instance.HidePrompt();
        }
    }

    private IEnumerator OpenMapSequence()
    {
        // Back on before the fade, so the first frame of the open already has
        // something to draw.
        SetMapCanvasEnabled(true);
        IsMapActive = true;
        isTransitioning = true;
        isMapOpen = true;

        // Record that the player has opened the world map at least once —
        // the camp guide's "Open the Map Table" step keys off this flag.
        // Previously that step was gated on Elias_TableBuilt (set by a
        // second Elias conversation), so opening the map never advanced
        // the guide.
        if (PlayerPrefs.GetInt("MapOpenedOnce", 0) == 0)
        {
            PlayerPrefs.SetInt("MapOpenedOnce", 1);
            PlayerPrefs.Save();
        }

        if (TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("Map",
                "Drag to pan, scroll to zoom. Click an available region to see its rewards and deploy when ready.", 6f);

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HidePrompt();
            GlobalHUD.Instance.SetGameplayPanelsActive(false);
        }

        if (floatingIcon != null) floatingIcon.SetActive(false);

        if (playerController != null) playerController.enabled = false;
        if (cameraFollow != null) cameraFollow.isCinematicMode = true;

        // Freeze the player's animator too — disabling PlayerController stops
        // input but the animator keeps whatever loop it was in (usually Run).
        // Zero the movement params so the blend tree returns to Idle, and
        // hard-stop root-motion drift for good measure.
        if (playerController != null)
        {
            var pAnim = playerController.GetComponentInChildren<Animator>();
            if (pAnim != null)
            {
                pAnim.SetBoolSafe("IsGrounded", true);
                pAnim.SetFloatSafe("Speed", 0f);
                pAnim.SetFloatSafe("MoveX", 0f);
                pAnim.SetFloatSafe("MoveZ", 0f);
                pAnim.applyRootMotion = false;
            }
        }

        // Guard against a null main camera — a pause / cutscene camera
        // can disable Camera.main mid-open. Previously the NRE aborted
        // the coroutine leaving IsMapActive + isTransitioning stuck true
        // (permanent map soft-lock).
        Camera cam = Camera.main;
        if (cam == null)
        {
            IsMapActive = false;
            isTransitioning = false;
            yield break;
        }
        Transform mainCam = cam.transform;

        if (savedCamPos == Vector3.zero)
        {
            savedCamPos = mainCam.position;
            savedCamRot = mainCam.rotation;
        }

        float elapsed = 0f;
        while (elapsed < flightDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / flightDuration;
            float smoothT = t * t * (3f - 2f * t);

            mainCam.position = Vector3.Lerp(savedCamPos, mapCameraPosition.position, smoothT);
            mainCam.rotation = Quaternion.Slerp(savedCamRot, mapCameraPosition.rotation, smoothT);
            yield return null;
        }
        mainCam.position = mapCameraPosition.position;
        mainCam.rotation = mapCameraPosition.rotation;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        mapCanvasGroup.gameObject.SetActive(true);
        yield return StartCoroutine(FadeCanvas(mapCanvasGroup, 1f, uiFadeDuration));

        OnMapFullyOpened?.Invoke();
        isTransitioning = false;
    }

    private IEnumerator CloseMapSequence()
    {
        isTransitioning = true;
        isMapOpen = false;

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
        if (mapPanelUI != null) mapPanelUI.ClosePanel();

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        yield return StartCoroutine(FadeCanvas(mapCanvasGroup, 0f, uiFadeDuration));

        IsMapActive = false;

        if (floatingIcon != null) floatingIcon.SetActive(true);

        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(true);

        // Guard as in OpenMapSequence — a null Camera.main mid-close used
        // to NRE and leave isTransitioning=true forever.
        Camera cam2 = Camera.main;
        if (cam2 == null)
        {
            isTransitioning = false;
            yield break;
        }
        Transform mainCam = cam2.transform;

        float elapsed = 0f;
        while (elapsed < flightDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / flightDuration;
            float smoothT = t * t * (3f - 2f * t);

            mainCam.position = Vector3.Lerp(mapCameraPosition.position, savedCamPos, smoothT);
            mainCam.rotation = Quaternion.Slerp(mapCameraPosition.rotation, savedCamRot, smoothT);
            yield return null;
        }

        if (cameraFollow != null)
        {
            cameraFollow.SyncRotation(savedCamRot.eulerAngles.y, savedCamRot.eulerAngles.x);
            cameraFollow.isCinematicMode = false;
        }
        if (playerController != null) playerController.enabled = true;

        if (playerInRange && GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_OPEN_MAP"));
        // Off again, now that the fade has finished and it is genuinely
        // invisible. See the note in Start.
        SetMapCanvasEnabled(false);
        isTransitioning = false;
    }

    private IEnumerator FadeCanvas(CanvasGroup cg, float targetAlpha, float duration)
    {
        if (cg == null) yield break;

        if (targetAlpha > 0.5f) { cg.blocksRaycasts = true; cg.interactable = true; }

        float startAlpha = cg.alpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            yield return null;
        }
        cg.alpha = targetAlpha;

        if (targetAlpha < 0.5f) { cg.blocksRaycasts = false; cg.interactable = false; }
    }

    private void OnDestroy() { IsMapActive = false; }
    private void OnDisable() { IsMapActive = false; }
}