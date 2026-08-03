using UnityEngine;
using UnityEngine.SceneManagement;

public class ExtractionPortal : MonoBehaviour
{
    [Header("Transition Settings")]
    public string campSceneName = "CampScene";
    public string promptMessage = "Press E to Return to Camp";

    [Header("Visuals (Optional)")]
    public ParticleSystem portalVFX; // ���� �����, ��� ������ �������
    public AudioSource extractAudio;

    private bool isPlayerNear = false;
    private PlayerController playerRef;
    private bool isExtracting = false;

    private void Start()
    {
        if (portalVFX != null && !portalVFX.isPlaying)
        {
            portalVFX.Play();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isExtracting) return;

        if (other.CompareTag("Player"))
        {
            isPlayerNear = true;
            playerRef = other.GetComponent<PlayerController>();

            if (GlobalHUD.Instance != null)
            {
                GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr(promptMessage));
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (isExtracting) return;

        if (other.CompareTag("Player"))
        {
            isPlayerNear = false;
            playerRef = null;

            if (GlobalHUD.Instance != null)
            {
                GlobalHUD.Instance.HidePrompt();
            }
        }
    }

    private void Update()
    {
        if (isPlayerNear && !isExtracting && Input.GetKeyDown(KeyCode.E))
        {
            StartExtraction();
        }
    }

    private void StartExtraction()
    {
        isExtracting = true; // ������� �������� ����������
        isPlayerNear = false;

        if (extractAudio != null) extractAudio.Play();

        // 1. ������� ϲ������
        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HidePrompt();
        }

        // 2. ���в����� ��� �� �������
        if (playerRef != null)
        {
            // �������� ��������, �� ������� ����� �� ��� ����
            SaveManager.AddCrystals(playerRef.crystalsCollected);

            // ���� � ���� ��������� ��� ���������� (��������� �����) - �� ����� ������ ����
            PlayerPrefs.SetFloat("SavedXP", playerRef.currentXP);
            PlayerPrefs.SetInt("SavedLevel", playerRef.currentLevel);
            PlayerPrefs.Save();
        }

        // 3. ������� ����ղ�
        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.FadeAndLoadScene(campSceneName);
        }
        else
        {
            // ��������� ������, ���� HUD ��������
            SceneLoader.LoadScene(campSceneName);
        }
    }
}