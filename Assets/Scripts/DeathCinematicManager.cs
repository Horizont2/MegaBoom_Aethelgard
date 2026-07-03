using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.SceneManagement;

public class DeathCinematicManager : MonoBehaviour
{
    public static DeathCinematicManager Instance;

    [Header("UI References")]
    public GameObject deathCanvas;
    public Image backgroundOverlay;
    public CanvasGroup textCanvasGroup;
    public RectTransform textTransform;
    public CanvasGroup buttonsCanvasGroup;

    [Header("Cinematic Settings")]
    public float slowMoTimeScale = 0.2f;
    public float textFadeDuration = 2.5f;
    public float buttonsDelay = 1.5f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null) canvas.enabled = false;
        else if (deathCanvas != null && deathCanvas != gameObject) deathCanvas.SetActive(false);
    }

    public void TriggerDeathCinematic()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.UI_GameOver);
        StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
        Time.timeScale = slowMoTimeScale;

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null) canvas.enabled = true;
        else if (deathCanvas != null) deathCanvas.SetActive(true);

        if (backgroundOverlay != null) backgroundOverlay.color = new Color(0f, 0f, 0f, 0f);
        if (textCanvasGroup != null) textCanvasGroup.alpha = 0f;
        if (textTransform != null) textTransform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
        if (buttonsCanvasGroup != null) buttonsCanvasGroup.alpha = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        float elapsed = 0f;

        while (elapsed < textFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / textFadeDuration;

            // Բ��: ����� ����� ��� �� 98% ������ (����� �������)
            if (backgroundOverlay != null) backgroundOverlay.color = new Color(0f, 0f, 0f, t * 0.98f);

            if (textCanvasGroup != null) textCanvasGroup.alpha = t;
            if (textTransform != null) textTransform.localScale = Vector3.Lerp(new Vector3(0.7f, 0.7f, 0.7f), Vector3.one, Mathf.SmoothStep(0f, 1f, t));

            yield return null;
        }

        yield return new WaitForSecondsRealtime(buttonsDelay);

        elapsed = 0f;
        float buttonsFadeTime = 1f;
        while (elapsed < buttonsFadeTime)
        {
            elapsed += Time.unscaledDeltaTime;
            if (buttonsCanvasGroup != null) buttonsCanvasGroup.alpha = elapsed / buttonsFadeTime;
            yield return null;
        }

        Time.timeScale = 0f;
    }

    // --- Բ��: ����� ����������Ӫ�� ����� ������������ ---
    public void RetryLevel()
    {
        Time.timeScale = 1f;

        // --- Բ��: ������ ������ ����� ����� ����� ������������� ---
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null) canvas.enabled = false;
        else if (deathCanvas != null) deathCanvas.SetActive(false);

        if (LoadingManager.Instance != null) LoadingManager.Instance.LoadScene(SceneManager.GetActiveScene().name);
        else SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ReturnToCamp()
    {
        Time.timeScale = 1f;

        // --- Բ��: ������ ������ ����� ����� ����� ������������� ---
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null) canvas.enabled = false;
        else if (deathCanvas != null) deathCanvas.SetActive(false);

        if (LoadingManager.Instance != null) LoadingManager.Instance.LoadScene("CampScene");
        else SceneManager.LoadScene("CampScene");
    }
}