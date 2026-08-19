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

    // True while the camp intro narration is playing. GlobalHUD checks
    // this so ESC can't open the pause menu mid-story, and we re-assert
    // the player control block every frame (LoadingManager clears it once
    // on scene-ready, which used to un-freeze the player mid-cutscene).
    public static bool IsPlaying { get; private set; }

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
            GameLog.Info("[CampDirector] ������� ������ ������� �� ���������!");
        }
    }

    private void Start()
    {
        // The camp always begins at normal time. The intro cinematic runs
        // on unscaled time and never resets Time.timeScale, so if anything
        // left it at 0 (a paused-world intro, a cinematic guard), it would
        // carry into the camp and silently freeze the tutorial narration
        // (first line stuck on screen, never advancing). Reset defensively.
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

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

        IsPlaying = true;
        if (player != null) player.isControlBlocked = true;
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(false);

        // Re-assert the movement block every frame — LoadingManager clears
        // isControlBlocked once when the scene is ready, which races this
        // coroutine and used to un-freeze the player mid-narration.
        StartCoroutine(HoldControlBlock());

        SetCinematicMode(true);

        var brain = Camera.main.GetComponent<CinemachineBrain>();
        if (brain != null) brain.enabled = true;

        if (timelineDirector != null) timelineDirector.Play();
        StartCoroutine(TutorialDialogueRoutine());
    }

    private IEnumerator HoldControlBlock()
    {
        while (IsPlaying)
        {
            if (player == null)
            {
                GameObject pObj = GameObject.FindGameObjectWithTag("Player");
                if (pObj != null) player = pObj.GetComponent<PlayerController>();
            }
            if (player != null && !player.isControlBlocked) player.isControlBlocked = true;
            yield return null;
        }
    }

    private void StopCutsceneImmediately()
    {
        IsPlaying = false;
        var brain = Camera.main.GetComponent<CinemachineBrain>();
        if (brain != null) brain.enabled = false;

        if (timelineDirector != null) timelineDirector.Stop();
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(true);

        SetCinematicMode(false);
    }

    private void OnDestroy()
    {
        // Never leave the pause/movement guard latched across a scene load.
        IsPlaying = false;
    }

    private void SetCinematicMode(bool isCinematic)
    {
        // Guard Camera.main — a pause/cinematic scene can disable it,
        // and NRE'ing here aborts SetCinematicMode leaving both mode
        // flags in the wrong state (cinematic locks stay stuck).
        Camera cam = Camera.main;
        if (cam == null) return;

        CameraFollow cf = cam.GetComponent<CameraFollow>();
        if (cf != null) cf.isCinematicMode = isCinematic;

        CameraCollision cc = cam.GetComponent<CameraCollision>();
        if (cc != null) cc.isCinematicMode = isCinematic;
    }

    private IEnumerator TutorialDialogueRoutine()
    {
        yield return new WaitForSecondsRealtime(1f);

        // dialogueId maps 1:1 to AudioManager.dialogue1..10 FMOD slots.
        // Camp tutorial owns slots 1-5, tutorial mission owns 6-10.
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Welcome to your new Camp. This is your safe haven between dangerous journeys.", 2f, 1));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Up there is the Camp Stash. All the resources you manage to bring back from the forest are stored here safely.", 2.5f, 2));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: You can use those resources to rebuild this place. Walk up to a plot and hold [E]. Restored buildings will generate resources over time!", 3f, 3));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: Check the Notice Board over there. You can take on special missions to earn resources and valuable Diamonds.", 2.5f, 4));
        yield return StartCoroutine(ShowSubtitleTypewriter("Stranger: At the edge of the camp is the mysterious Shop. Use your Diamonds there to buy permanent meta-upgrades for your future runs.", 3.2f, 5));
        yield return StartCoroutine(ShowTutorialHint("[TIP] Try to prioritize upgrading your Storage Vault early on, so you have enough space for all your hard-earned loot!", 3.8f));

        EndTutorial();
    }

    private void EndTutorial()
    {
        IsPlaying = false;
        PlayerPrefs.SetInt("CampTutorialPlayed", 1);
        PlayerPrefs.SetInt("SaveBld_ScoutsLodge", 1);
        PlayerPrefs.Save();
        if (player != null) player.isControlBlocked = false; if (player != null) player.enabled = true;
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.SetGameplayPanelsActive(true);

        Camera cam2 = Camera.main;
        if (cam2 != null)
        {
            var brain = cam2.GetComponent<CinemachineBrain>();
            if (brain != null) brain.enabled = false;

            CameraFollow cf = cam2.GetComponent<CameraFollow>();
            if (cf != null)
            {
                Vector3 currentRot = cam2.transform.eulerAngles;
                cf.SyncRotation(currentRot.y, currentRot.x);
            }
        }

        SetCinematicMode(false);
    }

    private IEnumerator ShowSubtitleTypewriter(string text, float stayDuration, int dialogueId = 0)
    {
        if (subtitleText == null) yield break;

        // Localise the incoming line. Callers pass English strings that
        // double as dictionary keys — LocalizationManager.Tr falls back
        // to the English literal when the active locale doesn't ship
        // this line, so no key is ever "missing".
        text = LocalizationManager.Tr(text);

        // Dip the score for the line so the reader isn't fighting the
        // music. Ducks in fast, releases when the line finishes typing.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.DuckMusic(0.35f, 0.25f, -1f, 0f);
            // Fire the matching VO slot if one is wired. PlayDialogue is
            // a no-op when the FMOD event is null so this is safe even
            // before voice acting is recorded.
            if (dialogueId > 0) AudioManager.Instance.PlayDialogue(dialogueId);
        }

        // Typewriter via an <alpha=#00> tag: the full line is always laid out
        // (reveal "in place", no layout jumps), the untyped tail transparent.
        // Uses plain `.text =` + ForceMeshUpdate — renders reliably here, unlike
        // maxVisibleCharacters which left later lines un-rendered on this TMP.
        // Realtime waits keep it immune to a stray Time.timeScale = 0.
        subtitleText.maxVisibleCharacters = 99999;
        for (int i = 0; i <= text.Length; i++)
        {
            if (subtitleText == null) yield break;
            subtitleText.text = text.Substring(0, i) + "<alpha=#00>" + text.Substring(i);
            subtitleText.ForceMeshUpdate();
            yield return new WaitForSecondsRealtime(typingSpeed);
        }

        yield return new WaitForSecondsRealtime(stayDuration);
        if (subtitleText == null) yield break;
        subtitleText.text = "";
        subtitleText.ForceMeshUpdate();

        // Restore the score once the subtitle clears.
        if (AudioManager.Instance != null) AudioManager.Instance.UnduckMusic(0.5f);
    }

    private IEnumerator ShowTutorialHint(string text, float duration)
    {
        if (subtitleText == null) yield break;

        text = LocalizationManager.Tr(text);
        subtitleText.maxVisibleCharacters = 99999;
        for (int i = 0; i <= text.Length; i++)
        {
            if (subtitleText == null) yield break;
            // Tint the whole line; reveal in place via the <alpha> tag.
            subtitleText.text = $"<color=#88CCFF>{text.Substring(0, i)}<alpha=#00>{text.Substring(i)}</color>";
            subtitleText.ForceMeshUpdate();
            yield return new WaitForSecondsRealtime(typingSpeed);
        }

        yield return new WaitForSecondsRealtime(duration);
        if (subtitleText == null) yield break;
        subtitleText.text = "";
        subtitleText.ForceMeshUpdate();
    }
}