using System.Collections;
using UnityEngine;
using TMPro;

// A cinematic location-name reveal (e.g. "THE BLIGHTED WOODS") that fades and
// gently eases in, holds, then fades out — optionally descending the gameplay
// camera from a high vantage down to the player while the title shows.
// Call Play() (StoryIntroPlayer does this when its slides finish).
public class LocationTitleReveal : MonoBehaviour
{
    [Header("Title UI")]
    public CanvasGroup group;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI subtitleText;      // optional smaller line under the title
    [Tooltip("Localization key / literal for the location name.")]
    public string locationKey = "THE BLIGHTED WOODS";
    [Tooltip("Optional smaller line, e.g. 'Region I'. Empty = hidden.")]
    public string subtitleKey = "";

    [Header("Timing")]
    public float fadeIn = 1.2f;
    public float hold = 2.5f;
    public float fadeOut = 1.5f;
    [Tooltip("Subtle ease-in scale for the title (1 = none).")]
    public float startScale = 1.08f;

    [Header("Camera descend (optional)")]
    public bool descendCamera = false;
    [Tooltip("The gameplay camera's transform to move down.")]
    public Transform cameraToMove;
    [Tooltip("Final camera pose — usually where the follow camera normally sits.")]
    public Transform descendTarget;
    [Tooltip("High-vantage offset from the target the descend starts at.")]
    public Vector3 startOffset = new Vector3(0f, 22f, -6f);
    public float descendDuration = 4f;
    [Tooltip("Optional camera-follow script to disable during the descend so it doesn't fight the move.")]
    public Behaviour disableDuringDescend;

    [Header("On finish")]
    public UnityEngine.Events.UnityEvent onFinished;

    public void Play()
    {
        StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        if (titleText != null) titleText.text = LocalizationManager.Tr(locationKey);
        if (subtitleText != null)
            subtitleText.text = string.IsNullOrEmpty(subtitleKey) ? "" : LocalizationManager.Tr(subtitleKey);
        if (group != null) { group.alpha = 0f; group.gameObject.SetActive(true); }

        Coroutine cam = null;
        if (descendCamera && cameraToMove != null && descendTarget != null)
            cam = StartCoroutine(DescendCamera());

        RectTransform rt = titleText != null ? titleText.rectTransform : null;

        // fade + ease in
        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeIn);
            if (group != null) group.alpha = k;
            if (rt != null) { float sc = Mathf.Lerp(startScale, 1f, k); rt.localScale = new Vector3(sc, sc, 1f); }
            yield return null;
        }
        if (group != null) group.alpha = 1f;
        if (rt != null) rt.localScale = Vector3.one;

        yield return new WaitForSecondsRealtime(hold);

        // fade out
        t = 0f;
        while (t < fadeOut)
        {
            t += Time.unscaledDeltaTime;
            if (group != null) group.alpha = 1f - Mathf.Clamp01(t / fadeOut);
            yield return null;
        }
        if (group != null) { group.alpha = 0f; group.gameObject.SetActive(false); }

        // make sure the camera has fully settled before handing back control
        if (cam != null) yield return cam;

        onFinished?.Invoke();
    }

    private IEnumerator DescendCamera()
    {
        if (disableDuringDescend != null) disableDuringDescend.enabled = false;

        Vector3 endPos = descendTarget.position;
        Quaternion endRot = descendTarget.rotation;
        Vector3 startPos = endPos + startOffset;

        // Aim the high shot roughly at the target so the descend reads as a
        // "look down at the hero" move settling into the gameplay pose.
        Vector3 lookDir = (endPos - startPos);
        Quaternion startRot = lookDir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(lookDir.normalized) : endRot;

        float t = 0f;
        while (t < descendDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / descendDuration));
            cameraToMove.position = Vector3.Lerp(startPos, endPos, k);
            cameraToMove.rotation = Quaternion.Slerp(startRot, endRot, k);
            yield return null;
        }
        cameraToMove.position = endPos;
        cameraToMove.rotation = endRot;

        // Hand control back to the follow camera.
        if (disableDuringDescend != null) disableDuringDescend.enabled = true;
    }
}
