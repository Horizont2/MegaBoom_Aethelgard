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

    private PlayerController playerController;
    private CameraFollow cameraFollow;

    private Vector3 savedCamPos;
    private Quaternion savedCamRot;
    private Coroutine activeSequence;

    private IEnumerator Start()
    {
        IsMapActive = false;
        if (mapCanvasGroup != null)
        {
            mapCanvasGroup.alpha = 0f;
            mapCanvasGroup.interactable = false;
            mapCanvasGroup.blocksRaycasts = false;
            mapCanvasGroup.gameObject.SetActive(true);
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerController = player.GetComponent<PlayerController>();

        if (Camera.main != null) cameraFollow = Camera.main.GetComponent<CameraFollow>();

        // --- ����: ����������� ²������� ���� ϲ��� ���������� ��ò��� ---
        if (PlayerPrefs.GetInt("AutoOpenMap", 0) == 1)
        {
            PlayerPrefs.SetInt("AutoOpenMap", 0);
            PlayerPrefs.Save();

            // ������, ���� ����� �� UI ��������� �������������
            yield return new WaitForSeconds(1.5f);
            activeSequence = StartCoroutine(OpenMapSequence());
        }
    }

    private void Update()
    {
        if (isTransitioning) return;

        if (playerInRange && !isMapOpen && Input.GetKeyDown(KeyCode.E))
        {
            int lodgeLevel = PlayerPrefs.GetInt("SaveBld_ScoutsLodge", 1);

            if (lodgeLevel < 2)
            {
                if (GlobalHUD.Instance != null)
                {
                    GlobalHUD.Instance.ShowPrompt("<color=#FF4444>Upgrade Elias's Lodge first!</color>");
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
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt("[E] Open Map");
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
        IsMapActive = true;
        isTransitioning = true;
        isMapOpen = true;

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

        Transform mainCam = Camera.main.transform;

        // �������� ������� ������ Ҳ���� ���� ���� �� �� ��������� � �������� ���� (���� �� ����-������� ���� � ������, �� ����� �� �������������� ������)
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

        Transform mainCam = Camera.main.transform;

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

        if (playerInRange && GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt("[E] Open Map");
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