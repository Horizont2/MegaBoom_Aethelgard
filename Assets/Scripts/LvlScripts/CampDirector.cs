using UnityEngine;
using UnityEngine.Playables;
using TMPro;
using System.Collections;
using Unity.Cinemachine;

public class CampDirector : MonoBehaviour
{
    [Header("Cinematic & UI")]
    public PlayableDirector timelineDirector;
    public PlayerController player;

    public TextMeshProUGUI subtitleText;
    public float typingSpeed = 0.04f;

    private void Awake()
    {
        if (PlayerPrefs.GetInt("SaveBld_ScoutsLodge", 0) == 0)
        {
            PlayerPrefs.SetInt("SaveBld_ScoutsLodge", 1);
            PlayerPrefs.Save();
        }

        // --- Բ�� ���� � ����в���� ---
        // ���� ������� �������� � ����� ������, ������� �������� ����� (Lvl_1) ����� ��������!
        if (PlayerPrefs.GetInt("TutorialCompleted", 0) == 0)
        {
            PlayerPrefs.SetInt("TutorialCompleted", 1);
            PlayerPrefs.Save();
            Debug.Log("[CampDirector] ������� ������ ������� �� ���������!");
        }
    }

    private void Start()
    {
        bool tutorialPlayed = PlayerPrefs.GetInt("CampTutorialPlayed", 0) == 1;

        if (!tutorialPlayed) StartCampTutorial();
        else StopCutsceneImmediately();
    }

    private void StartCampTutorial()
    {
        if (player == null)
        {
            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null) player = pObj.GetComponent<PlayerController>();
        }

        if (player != null) player.isControlBlocked = true;
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(false);

        SetCinematicMode(true);

        var brain = Camera.main.GetComponent<CinemachineBrain>();
        if (brain != null) brain.enabled = true;

        if (timelineDirector != null) timelineDirector.Play();
        StartCoroutine(TutorialDialogueRoutine());
    }

    private void StopCutsceneImmediately()
    {
        var brain = Camera.main.GetComponent<CinemachineBrain>();
        if (brain != null) brain.enabled = false;

        if (timelineDirector != null) timelineDirector.Stop();
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(true);

        SetCinematicMode(false);
    }

    private void SetCinematicMode(bool isCinematic)
    {
        CameraFollow cf = Camera.main.GetComponent<CameraFollow>();
        if (cf != null) cf.isCinematicMode = isCinematic;

        CameraCollision cc = Camera.main.GetComponent<CameraCollision>();
        if (cc != null) cc.isCinematicMode = isCinematic;
    }

    private IEnumerator TutorialDialogueRoutine()
    {
        yield return new WaitForSeconds(1f);

        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Welcome to your new Camp. This is your safe haven between dangerous journeys.", 2f));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Up there is the Camp Stash. All the resources you manage to bring back from the forest are stored here safely.", 2.5f));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: You can use those resources to rebuild this place. Walk up to a plot and hold [E]. Restored buildings will generate resources over time!", 3f));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Check the Notice Board over there. You can take on special missions to earn resources and valuable Diamonds.", 2.5f));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: At the edge of the camp is the mysterious Shop. Use your Diamonds there to buy permanent meta-upgrades for your future runs.", 3.2f));
        yield return StartCoroutine(ShowTutorialHint("[TIP] Try to prioritize upgrading your Storage Vault early on, so you have enough space for all your hard-earned loot!", 3.8f));

        EndTutorial();
    }

    private void EndTutorial()
    {
        PlayerPrefs.SetInt("CampTutorialPlayed", 1);
        PlayerPrefs.SetInt("SaveBld_ScoutsLodge", 1);
        PlayerPrefs.Save();
        if (player != null) player.isControlBlocked = false; if (player != null) player.enabled = true;
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(true);

        var brain = Camera.main.GetComponent<CinemachineBrain>();
        if (brain != null) brain.enabled = false;

        CameraFollow cf = Camera.main.GetComponent<CameraFollow>();
        if (cf != null)
        {
            Vector3 currentRot = Camera.main.transform.eulerAngles;
            cf.SyncRotation(currentRot.y, currentRot.x);
        }

        SetCinematicMode(false);
    }

    private IEnumerator ShowSubtitleTypewriter(string text, float stayDuration)
    {
        if (subtitleText == null) yield break;

        // Localise the incoming line. Callers pass English strings that
        // double as dictionary keys — LocalizationManager.Tr falls back
        // to the English literal when the active locale doesn't ship
        // this line, so no key is ever "missing".
        text = LocalizationManager.Tr(text);
        subtitleText.text = text;
        subtitleText.ForceMeshUpdate();
        int totalChars = subtitleText.textInfo.characterCount;
        subtitleText.maxVisibleCharacters = 0;

        for (int i = 0; i <= totalChars; i++)
        {
            subtitleText.maxVisibleCharacters = i;
            yield return new WaitForSeconds(typingSpeed);
        }

        yield return new WaitForSeconds(stayDuration);
        subtitleText.maxVisibleCharacters = 99999;
        subtitleText.text = "";
    }

    private IEnumerator ShowTutorialHint(string text, float duration)
    {
        if (subtitleText == null) yield break;

        text = LocalizationManager.Tr(text);
        subtitleText.text = $"<color=#88CCFF>{text}</color>";
        subtitleText.ForceMeshUpdate();
        int totalChars = subtitleText.textInfo.characterCount;
        subtitleText.maxVisibleCharacters = 0;

        for (int i = 0; i <= totalChars; i++)
        {
            subtitleText.maxVisibleCharacters = i;
            yield return new WaitForSeconds(typingSpeed);
        }

        yield return new WaitForSeconds(duration);
        subtitleText.maxVisibleCharacters = 99999;
        subtitleText.text = "";
    }
}