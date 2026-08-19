using UnityEngine;
using UnityEngine.Playables;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class IntroCinematicManager : MonoBehaviour
{
    [Header("References")]
    public PlayableDirector director;
    public CanvasGroup cinematicCanvasGroup;
    public GameObject mainGameUI;

    [Header("Subtitles")]
    public TextMeshProUGUI subtitleText;
    public float typingSpeed = 0.05f;
    [Tooltip("������ ������ ������ ����� ��������� ������ ��� ���������� ������")]
    public float delayBetweenChunks = 1.5f;

    [Header("Auto Split Settings")]
    [Tooltip("����������� ������� ������� �� ������ ����� ���, �� ����� ����������� ����'�����")]
    public int maxCharactersPerScreen = 70;

    private Coroutine typingCoroutine;
    private bool isSkipping = false;
    // Guards the single transition into the menu so the director.stopped
    // callback, the skip path, and the WebGL safety-timeout can never
    // double-fire FadeOutCinematic.
    private bool _finished = false;

    // Բ��: �������� ����� �����'�����, �� ������� ����� � �������� ������ ���
    private static bool hasPlayedThisSession = false;
    public static bool HasPlayedThisSession => hasPlayedThisSession;

    // True while the intro cinematic is actively showing. GlobalHUD's ESC
    // handler checks this to keep the pause menu out, and we block player
    // movement for the duration — running around mid-narration broke the
    // storytelling completely.
    public static bool IsPlaying { get; private set; }

    private void Awake()
    {
        if (cinematicCanvasGroup == null)
            cinematicCanvasGroup = GetComponent<CanvasGroup>();

        if (subtitleText != null) subtitleText.text = "";
    }

    private void Start()
    {
        if (!gameObject.activeInHierarchy) return;

        // Якщо це вже друга поява меню в цій сесії — приховуємо катсцену І зупиняємо
        // director, інакше Timeline автоматично запустить свій AudioTrack навіть коли
        // сама катсцена візуально не показується.
        if (hasPlayedThisSession)
        {
            if (director != null)
            {
                director.playOnAwake = false;
                if (director.state == PlayState.Playing) director.Stop();
                director.gameObject.SetActive(false);
            }
            if (mainGameUI != null) mainGameUI.SetActive(true);
            if (cinematicCanvasGroup != null)
            {
                cinematicCanvasGroup.alpha = 0f;
                cinematicCanvasGroup.blocksRaycasts = false;
                cinematicCanvasGroup.gameObject.SetActive(false);
            }
            return;
        }

        hasPlayedThisSession = true;
        IsPlaying = true;

        if (mainGameUI != null) mainGameUI.SetActive(false);
        if (cinematicCanvasGroup != null)
        {
            cinematicCanvasGroup.alpha = 1f;
            cinematicCanvasGroup.blocksRaycasts = true;
        }

        // Freeze the player for the whole narration — poll briefly in case
        // the PlayerController spawns a frame or two after us.
        StartCoroutine(BlockPlayerWhilePlaying());

        if (director != null)
        {
            director.stopped += OnCinematicFinished;
            director.Play();
        }

        // Safety net: the menu (mainGameUI) is only revealed when the
        // cinematic finishes. On WebGL the Timeline can hold its last frame
        // without ever raising `stopped`, which stranded the player on the
        // final intro image with the menu never appearing. Force the reveal
        // after the intro's own length elapses if the stop callback hasn't.
        StartCoroutine(SafetyRevealAfterDuration());
    }

    private IEnumerator SafetyRevealAfterDuration()
    {
        // Wait the cinematic's real length (realtime, so a stray
        // timeScale = 0 can't stall it) plus a small buffer.
        float wait = (director != null && director.duration > 0.01)
            ? (float)director.duration + 1.5f
            : 15f;
        yield return new WaitForSecondsRealtime(wait);

        if (IsPlaying && !isSkipping && !_finished)
        {
            Debug.LogWarning("[IntroCinematic] director.stopped never fired — forcing menu reveal.");
            StartCoroutine(FadeOutCinematic());
        }
    }

    private IEnumerator BlockPlayerWhilePlaying()
    {
        PlayerController pc = null;
        // Grab the player as soon as they exist, then hold the block for
        // the duration of the cinematic.
        while (IsPlaying)
        {
            if (pc == null)
            {
                pc = PlayerController.LocalInstance;
                if (pc == null) pc = FindFirstObjectByType<PlayerController>();
            }
            if (pc != null && !pc.isControlBlocked) pc.isControlBlocked = true;
            yield return null;
        }
        if (pc != null) pc.isControlBlocked = false;
    }

    // ����� ��� ������� � TIMELINE
    public void SetSubtitle(string fullText)
    {
        // ������: ���� ��'��� ��� ���������, ������ �� ������
        if (!gameObject.activeInHierarchy) return;

        if (typingCoroutine != null) StopCoroutine(typingCoroutine);

        // Route Timeline-authored English through Tr so the cinematic
        // subtitles localise. Register each line as a self-keyed entry
        // in LocalizationManager for the languages you ship.
        List<string> autoChunks = AutoSplitText(LocalizationManager.Tr(fullText));
        typingCoroutine = StartCoroutine(TypeTextChunks(autoChunks));
    }

    // ��ò�� ������������� ���Ĳ����� ������
    private List<string> AutoSplitText(string text)
    {
        List<string> chunks = new List<string>();
        string[] words = text.Split(' '); // ��������� ���� ����� �� ����� �����
        string currentChunk = "";

        foreach (string word in words)
        {
            // ���� ��������� ���������� ����� ���������� ���...
            if ((currentChunk.Length + word.Length + 1) > maxCharactersPerScreen)
            {
                // ...�������� �������� ������ � �������� �����
                chunks.Add(currentChunk.Trim());
                currentChunk = word + " ";
            }
            else
            {
                currentChunk += word + " ";
            }
        }

        // ������ ������� ������, ���� �� �
        if (!string.IsNullOrWhiteSpace(currentChunk))
        {
            chunks.Add(currentChunk.Trim());
        }

        return chunks;
    }

    private IEnumerator TypeTextChunks(List<string> chunks)
    {
        // Robust typewriter: set the visible SUBSTRING as full text each step
        // + ForceMeshUpdate. The maxVisibleCharacters path left later chunks
        // un-rendered on this TMP version (intro froze on the first chunk).
        subtitleText.maxVisibleCharacters = 99999;
        for (int c = 0; c < chunks.Count; c++)
        {
            string chunk = chunks[c];
            for (int i = 0; i <= chunk.Length; i++)
            {
                subtitleText.text = chunk.Substring(0, i);
                subtitleText.ForceMeshUpdate();
                yield return new WaitForSecondsRealtime(typingSpeed);
            }

            if (c < chunks.Count - 1)
            {
                yield return new WaitForSecondsRealtime(delayBetweenChunks);
            }
        }
    }

    // Hold-to-skip: an accidental Esc used to kill the intro instantly
    // (Esc is muscle-memory for "open settings"). Now the player must
    // hold Esc/Space for `holdToSkipSeconds` before the cutscene aborts.
    [Header("Skip UX")]
    [Tooltip("How long the player must hold Esc/Space to skip the intro. Accidental single presses no longer abort the cutscene.")]
    public float holdToSkipSeconds = 0.6f;
    public TMPro.TextMeshProUGUI holdToSkipPrompt;
    private float holdSkipTimer = 0f;

    void Update()
    {
        if (isSkipping) return;

        // Cover keyboard (Space/Escape/Enter) + gamepad (Start / East /
        // South via InputCompat) — original only listened to Space+Escape
        // which fell silent if some other Update swallowed those specific
        // keys or the player was on a controller.
        bool held = Input.GetKey(KeyCode.Space)
                 || Input.GetKey(KeyCode.Escape)
                 || Input.GetKey(KeyCode.Return)
                 || Input.GetKey(KeyCode.KeypadEnter)
                 || Input.GetKey(KeyCode.JoystickButton7)   // Start on most pads
                 || Input.GetKey(KeyCode.JoystickButton1);  // East (B / Circle)

        // Also short-circuit on any GetKeyDown edge of the same set — a
        // single tap skips immediately IF the player already has the
        // hold-prompt on screen (they clearly want out).
        bool tapped = Input.GetKeyDown(KeyCode.Space)
                   || Input.GetKeyDown(KeyCode.Escape)
                   || Input.GetKeyDown(KeyCode.Return);

        if (held)
        {
            holdSkipTimer += Time.unscaledDeltaTime;
            if (holdToSkipPrompt != null && !holdToSkipPrompt.gameObject.activeSelf)
                holdToSkipPrompt.gameObject.SetActive(true);
            if (holdSkipTimer >= holdToSkipSeconds)
            {
                SkipCinematic();
            }
        }
        else
        {
            if (holdSkipTimer > 0f) holdSkipTimer = 0f;
            if (holdToSkipPrompt != null && holdToSkipPrompt.gameObject.activeSelf)
                holdToSkipPrompt.gameObject.SetActive(false);
        }

        // Belt-and-braces: if the player has tapped the skip key many
        // times without a full hold, they clearly want to skip. Count
        // taps and let them off after 3.
        if (tapped)
        {
            skipTapCount++;
            if (skipTapCount >= 3) SkipCinematic();
        }
    }

    private int skipTapCount = 0;

    public void SkipCinematic()
    {
        // ������: ���������� �� �������� ��'���
        if (!gameObject.activeInHierarchy || isSkipping) return;
        isSkipping = true;
        StartCoroutine(FadeOutCinematic());
    }

    private void OnCinematicFinished(PlayableDirector dir)
    {
        if (!isSkipping && !_finished)
        {
            StartCoroutine(FadeOutCinematic());
        }
    }

    private IEnumerator FadeOutCinematic()
    {
        // One-way latch — director.stopped, the skip path, and the safety
        // timeout all route here; only the first may run.
        if (_finished) yield break;
        _finished = true;

        if (director != null && director.state == PlayState.Playing)
        {
            director.Stop();
        }

        // ==========================================
        // ФІКС: Передаємо естафету аудіоменеджеру після катсцени
        // ==========================================
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayMusic(AudioID.Music_Camp);
        }

        if (subtitleText != null) StartCoroutine(FadeSubtitleOut());

        if (mainGameUI != null) mainGameUI.SetActive(true);

        if (cinematicCanvasGroup != null)
        {
            float fadeDuration = 1.5f;
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                // Unscaled — a stray Time.timeScale = 0 (pause menu
                // opened over the intro, whatever) used to freeze the
                // fade forever and pin blocksRaycasts=true, so ESC/
                // Space presses hit the intro overlay instead of the
                // menu underneath. Realtime clears cleanly regardless.
                elapsed += Time.unscaledDeltaTime;
                cinematicCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
                yield return null;
            }
            cinematicCanvasGroup.alpha = 0f;
            cinematicCanvasGroup.blocksRaycasts = false;
            cinematicCanvasGroup.gameObject.SetActive(false);
        }

        // Release the movement/pause locks once the fade completes.
        IsPlaying = false;
    }

    private IEnumerator FadeSubtitleOut()
    {
        float t = 1f;
        while (t > 0)
        {
            t -= Time.deltaTime * 2f;
            subtitleText.alpha = t;
            yield return null;
        }
    }

    private void OnDestroy()
    {
        if (director != null) director.stopped -= OnCinematicFinished;
        // Never leave the global locks latched across a scene unload.
        IsPlaying = false;
    }
}