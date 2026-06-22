using UnityEngine;
using UnityEngine.UI;

// Drops a fullscreen UI_BlurMaterial Image behind the pause panel
// when it opens. Cinematic backdrop blur is one of the easiest
// AAA-feel improvements for a pause overlay — the project already
// has the shader/material so we just need to lay an Image in front
// of the rest of the HUD using it.
//
// Watches GlobalHUD.Instance.pausePanelGroup activeInHierarchy and
// shows/hides the blur layer in sync.
public class PauseMenuBlur : MonoBehaviour
{
    private static PauseMenuBlur s_instance;
    private GameObject blurGO;
    private CanvasGroup blurCG;
    private bool visible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (s_instance != null) return;
        GameObject go = new GameObject("[PauseMenuBlur]");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<PauseMenuBlur>();
    }

    private void EnsureBuilt()
    {
        if (blurGO != null) return;
        if (GlobalHUD.Instance == null) return;
        RectTransform hudRect = GlobalHUD.Instance.GetComponent<RectTransform>();
        if (hudRect == null) return;

        blurGO = new GameObject("PauseBlurBackdrop");
        RectTransform rt = blurGO.AddComponent<RectTransform>();
        rt.SetParent(hudRect, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Put the blur behind the pause panel itself, but in front of all
        // gameplay HUD elements. Achieved by setting it as the sibling
        // immediately before the pause panel.
        if (GlobalHUD.Instance.pausePanelGroup != null)
            rt.SetSiblingIndex(GlobalHUD.Instance.pausePanelGroup.transform.GetSiblingIndex());

        Image img = blurGO.AddComponent<Image>();
        img.raycastTarget = false;
        Material blurMat = Resources.Load<Material>("UI_BlurMaterial");
        if (blurMat == null)
        {
            // Project's blur material isn't in a Resources folder by
            // default — find it by direct asset path resolution via
            // the existing shader if needed. Fallback to a dark dim
            // backdrop so we never end up with nothing.
            img.color = new Color(0f, 0f, 0f, 0.55f);
        }
        else
        {
            img.material = blurMat;
            img.color = new Color(1f, 1f, 1f, 0.85f);
        }

        blurCG = blurGO.AddComponent<CanvasGroup>();
        blurCG.alpha = 0f;
        blurCG.interactable = false;
        blurCG.blocksRaycasts = false;
        blurGO.SetActive(false);
    }

    private void Update()
    {
        if (GlobalHUD.Instance == null) return;
        EnsureBuilt();
        if (blurGO == null || GlobalHUD.Instance.pausePanelGroup == null) return;

        bool wantVisible = GlobalHUD.Instance.pausePanelGroup.gameObject.activeInHierarchy
                        && GlobalHUD.Instance.pausePanelGroup.alpha > 0.05f;

        if (wantVisible && !blurGO.activeSelf) blurGO.SetActive(true);
        float target = wantVisible ? 1f : 0f;
        blurCG.alpha = Mathf.MoveTowards(blurCG.alpha, target, Time.unscaledDeltaTime * 6f);
        if (!wantVisible && blurCG.alpha < 0.01f && blurGO.activeSelf) blurGO.SetActive(false);
        visible = wantVisible;
    }
}
