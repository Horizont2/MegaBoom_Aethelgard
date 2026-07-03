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

    // Բ��: �������� ����� �����'�����, �� ������� ����� � �������� ������ ���
    private static bool hasPlayedThisSession = false;

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

        if (mainGameUI != null) mainGameUI.SetActive(false);
        if (cinematicCanvasGroup != null)
        {
            cinematicCanvasGroup.alpha = 1f;
            cinematicCanvasGroup.blocksRaycasts = true;
        }

        if (director != null)
        {
            director.stopped += OnCinematicFinished;
            director.Play();
        }
    }

    // ����� ��� ������� � TIMELINE
    public void SetSubtitle(string fullText)
    {
        // ������: ���� ��'��� ��� ���������, ������ �� ������
        if (!gameObject.activeInHierarchy) return;

        if (typingCoroutine != null) StopCoroutine(typingCoroutine);

        List<string> autoChunks = AutoSplitText(fullText);
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
        for (int c = 0; c < chunks.Count; c++)
        {
            subtitleText.text = chunks[c];
            subtitleText.maxVisibleCharacters = 0;
            subtitleText.ForceMeshUpdate();

            int totalVisibleCharacters = chunks[c].Length;

            // ������� �������� ������
            for (int i = 0; i <= totalVisibleCharacters; i++)
            {
                subtitleText.maxVisibleCharacters = i;
                yield return new WaitForSecondsRealtime(typingSpeed);
            }

            // ���� �� �� �������� ������ ������, ������ �����
            if (c < chunks.Count - 1)
            {
                yield return new WaitForSecondsRealtime(delayBetweenChunks);
            }
        }

        subtitleText.maxVisibleCharacters = 99999;
    }

    void Update()
    {
        if (isSkipping) return;

        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape))
        {
            SkipCinematic();
        }
    }

    public void SkipCinematic()
    {
        // ������: ���������� �� �������� ��'���
        if (!gameObject.activeInHierarchy || isSkipping) return;
        isSkipping = true;
        StartCoroutine(FadeOutCinematic());
    }

    private void OnCinematicFinished(PlayableDirector dir)
    {
        if (!isSkipping)
        {
            StartCoroutine(FadeOutCinematic());
        }
    }

    private IEnumerator FadeOutCinematic()
    {
        if (director != null && director.state == PlayState.Playing)
        {
            director.Stop();
        }

        if (subtitleText != null) StartCoroutine(FadeSubtitleOut());

        if (mainGameUI != null) mainGameUI.SetActive(true);

        if (cinematicCanvasGroup != null)
        {
            float fadeDuration = 1.5f;
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                cinematicCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
                yield return null;
            }
            cinematicCanvasGroup.alpha = 0f;
            cinematicCanvasGroup.blocksRaycasts = false;
            cinematicCanvasGroup.gameObject.SetActive(false);
        }
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
    }
}