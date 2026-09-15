using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

[RequireComponent(typeof(CanvasGroup))]
public class MissionUIElement : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;

    public Image backgroundImage; // �� ��� ��'��� Bg, ���� �� ������ ������

    [Header("Settings")]
    [Tooltip("RETIRED — every objective now animates in. Kept only so existing prefabs and scenes do not lose a serialized field; setting it does nothing.")]
    public bool animateAppearance = false;
    public float animationDuration = 0.5f;

    private CanvasGroup canvasGroup;
    public bool isCompleted = false;

    private string baseDescription = "";
    // Cache the background image's authored colour so we can restore it if
    // the CompleteMission flash coroutine gets StopAllCoroutines'd mid-flash
    // (which happened when Setup was called during the 0.5s white flash —
    // the flash never lerped back and the tile stayed pure white).
    private Color originalBgColor = Color.white;
    private bool originalBgCached = false;

    private bool _setupCalled = false;

    [Header("Polish")]
    [Tooltip("Reveal the objective text a character at a time. Motion in the corner of the screen is what actually gets a new objective noticed — a line that simply appears does not.")]
    public bool typewriter = true;
    public float typeSpeed = 55f;

    private Coroutine _typing;

    // ==== WHERE THE PLATE ACTUALLY LIVES, READ ONCE ====
    //
    // Both animations used to take the card's CURRENT local position as its
    // home. Setup StopAllCoroutines() first, so a new objective arriving while
    // the last one was still sliding caught the card mid-flight — 180px left of
    // home, say — and adopted that as home. Every interrupted objective walked
    // the plate up to another 300px left, permanently, until it was sitting in
    // the middle of the screen instead of in the container it was authored in.
    //
    // The authored position is a fact about the prefab, so it is read once,
    // before anything has had a chance to move it, and every animation returns
    // to that.
    private Vector3 _home;
    private bool _homeCached;

    private void Awake()
    {
        // Declared and used but never fetched — the fade-in has never actually
        // run, despite the RequireComponent above guaranteeing one is present.
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        CacheHome();
    }

    private void CacheHome()
    {
        if (_homeCached || backgroundImage == null) return;
        _home = backgroundImage.transform.localPosition;
        _homeCached = true;
    }

    public void Setup(string title, string description, int current, int target)
    {
        isCompleted = false;
        _setupCalled = true;
        CacheHome();
        StopAllCoroutines();
        _typing = null;

        // Whatever the interrupted coroutines were part-way through, the card
        // belongs at home before the next one starts from it.
        if (_homeCached) backgroundImage.transform.localPosition = _home;

        // Restore the background if a previous CompleteMission flash was
        // interrupted before its own lerp finished, otherwise the tile shows
        // the raw white flash sprite instead of the mission background.
        if (originalBgCached && backgroundImage != null)
            backgroundImage.color = originalBgColor;

        if (titleText != null) titleText.text = title;

        baseDescription = description;
        _progressCurrent = current;
        _progressTarget = target;

        // A stopped TypeRoutine leaves maxVisibleCharacters part-way, and
        // nothing else ever puts it back — the rest of the line would simply
        // never appear.
        if (descriptionText != null) descriptionText.maxVisibleCharacters = int.MaxValue;
        ApplyDescription();

        // EVERY new objective slides in, not only the first.
        //
        // animateAppearance used to switch itself off after one use, so the
        // opening step arrived with a flourish and every step after it silently
        // swapped its text — which is the one moment the player most needs to
        // notice, and the one that got the least attention.
        StartCoroutine(AppearRoutine());
        if (typewriter && descriptionText != null)
            _typing = StartCoroutine(TypeRoutine());
    }

    // ==== ONE PLACE BUILDS THE LINE ====
    //
    // There used to be three: Setup composed it with the progress counter,
    // TypeRoutine then overwrote it with the bare label so the counter was
    // typed away, and UpdateProgress — the one that would have put it back —
    // was gated behind `_typing == null` and so could not. That is why the
    // plates stopped showing progress.
    //
    // The typewriter no longer touches the text at all. It reveals whatever is
    // there, so progress can change mid-reveal and the counter simply appears
    // with the rest of the line.
    private int _progressCurrent;
    private int _progressTarget;

    private string ComposeDescription()
    {
        if (isCompleted)
            return $"{baseDescription} <color=#00FF00>({LocalizationManager.Tr("MISSION_DONE_TAG")})</color>";
        if (_progressTarget > 1)
            return $"{baseDescription} (<color=#FFD700>{_progressCurrent}</color>/{_progressTarget})";
        return baseDescription;
    }

    private void ApplyDescription()
    {
        if (descriptionText == null) return;
        descriptionText.text = ComposeDescription();
        // While the typewriter is running it owns the reveal; otherwise the
        // whole line is visible.
        if (_typing == null) descriptionText.maxVisibleCharacters = int.MaxValue;
    }

    // Reveals the objective a character at a time. Unscaled: objectives are
    // handed out during dialogue and menus, where the game clock is often
    // stopped, and a line that never finishes typing is worse than none.
    private IEnumerator TypeRoutine()
    {
        // Glyph count, not string length: maxVisibleCharacters counts rendered
        // characters, so the <color> markup around the counter must not be
        // included or the line finishes typing early and looks truncated.
        descriptionText.maxVisibleCharacters = 0;
        descriptionText.ForceMeshUpdate();
        int total = descriptionText.textInfo.characterCount;

        if (total <= 0)
        {
            descriptionText.maxVisibleCharacters = int.MaxValue;
            _typing = null;
            yield break;
        }

        float shown = 0f;
        try
        {
            while (shown < total)
            {
                shown += Time.unscaledDeltaTime * typeSpeed;
                descriptionText.maxVisibleCharacters = Mathf.Clamp(Mathf.FloorToInt(shown), 0, total);
                yield return null;
            }
        }
        finally
        {
            // Interrupted or finished, the line ends up fully readable.
            if (descriptionText != null) descriptionText.maxVisibleCharacters = int.MaxValue;
            _typing = null;
        }
    }

    private IEnumerator AppearRoutine()
    {
        // ��ò� ���: �� �� ������ �������� ��'��� (���� ����� Layout Group).
        // �� ������ ����� �������� ������� (Bg) � ������ ��!
        if (backgroundImage == null) yield break;

        Transform visualTransform = backgroundImage.transform;

        CacheHome();
        Vector3 targetPos = _home;

        // ³������� �� �� 300 ������ ���� ��� ������
        Vector3 startPos = targetPos + new Vector3(-300f, 0, 0);

        float t = 0;
        while (t < 1)
        {
            // Unscaled: objectives are handed out mid-dialogue and mid-menu,
            // where the game clock is often stopped, and a plate frozen halfway
            // through sliding in is worse than one that simply appeared.
            t += Time.unscaledDeltaTime / Mathf.Max(0.05f, animationDuration);
            float curve = Mathf.SmoothStep(0, 1, t);

            // ������ ���������� �������� ��'���
            if (canvasGroup != null) canvasGroup.alpha = curve;

            // ������ �������� �������
            visualTransform.localPosition = Vector3.Lerp(startPos, targetPos, curve);
            yield return null;
        }
        visualTransform.localPosition = targetPos;
    }

    public void UpdateProgress(int current, int target)
    {
        if (isCompleted) return;

        _progressCurrent = current;
        _progressTarget = target;
        ApplyDescription();
    }

    public void CompleteMission()
    {
        if (isCompleted) return;
        isCompleted = true;

        if (titleText != null) titleText.text = $"<s>{titleText.text}</s>";

        // A half-typed line must not be left half-typed by being ticked off.
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        ApplyDescription();

        StartCoroutine(CompleteAnimationRoutine());
    }

    public void SetCompletedStateInstant()
    {
        isCompleted = true;
        _setupCalled = true;
        CacheHome();
        if (_homeCached) backgroundImage.transform.localPosition = _home;
        if (titleText != null) titleText.text = $"<s>{titleText.text}</s>";

        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        ApplyDescription();

        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    // The completion beat: a flash that sweeps back, and a physical nudge.
    //
    // Deliberately restrained. An earlier pass added a generated tick badge and
    // an accent stripe on top of this, and it was too much furniture bolted to a
    // plate that was authored as a clean text card — procedural decoration
    // rarely sits well next to art somebody designed. The nudge stays because it
    // costs no new objects: it moves the card that is already there.
    private IEnumerator CompleteAnimationRoutine()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_QuestComplete);

        Transform visual = backgroundImage != null ? backgroundImage.transform : transform;
        CacheHome();
        Vector3 restPos = _homeCached ? _home : visual.localPosition;
        Color originalColor = backgroundImage != null ? backgroundImage.color : Color.white;
        if (backgroundImage != null) backgroundImage.color = Color.white;

        float t = 0f;
        const float beat = 0.5f;
        while (t < beat)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / beat);

            if (backgroundImage != null)
                backgroundImage.color = Color.Lerp(Color.white, originalColor, k);

            // A nudge to the right and back: the plate reacts physically to
            // being ticked off instead of only changing colour.
            visual.localPosition = restPos + new Vector3(Mathf.Sin(k * Mathf.PI) * 12f, 0f, 0f);
            yield return null;
        }

        visual.localPosition = restPos;
        if (backgroundImage != null) backgroundImage.color = originalColor;
    }
}