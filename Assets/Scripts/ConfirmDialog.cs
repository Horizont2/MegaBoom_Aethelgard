using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Runtime-built Yes/No modal. Any script can pop one with
//   ConfirmDialog.Show("Really quit?", onYes: () => Application.Quit());
// Persists across scene loads, ignores Time.timeScale, dims the game
// behind it, and consumes clicks so nothing under it fires. Only one
// dialog is on screen at a time — a second Show() while one is open is
// ignored (the caller already has a decision pending from the player).
public class ConfirmDialog : MonoBehaviour
{
    private static ConfirmDialog s_instance;
    private static bool s_open;

    public static void Show(string message, Action onYes, Action onNo = null,
                            string yesLabel = null, string noLabel = null)
    {
        if (s_open) return;
        EnsureInstance();
        s_instance.Build(message, onYes, onNo, yesLabel, noLabel);
    }

    public static bool IsOpen => s_open;

    private static void EnsureInstance()
    {
        if (s_instance != null) return;

        // Drop a prefab at Assets/Resources/UI/ConfirmDialog.prefab and it is
        // used instead of the bare object.
        var prefab = Resources.Load<GameObject>(PrefabResource);
        GameObject go = prefab != null ? Instantiate(prefab) : new GameObject("[ConfirmDialog]");
        go.name = "[ConfirmDialog]";
        DontDestroyOnLoad(go);
        s_instance = go.GetComponent<ConfirmDialog>();
        if (s_instance == null) s_instance = go.AddComponent<ConfirmDialog>();
    }

    public const string PrefabResource = "UI/ConfirmDialog";

    // ==== AUTHOR IT, OR LET THE CODE BUILD IT ====
    //
    // This dialog DESTROYED AND REBUILT its entire hierarchy on every open —
    // panel, border, message and both buttons, from scratch, because the
    // message and the labels change per call. Rebuilding a whole UI tree to
    // change two strings is waste, and it left nothing in the project to open:
    // the one screen that asks the player to confirm something irreversible
    // could not be laid out, restyled or localised in the editor at all.
    //
    // Assign these — on a prefab — and the code only fills in the text and
    // hooks up the callbacks. Leave them empty and it builds what it always
    // did, so nothing changes until somebody authors a dialog.
    [Header("Dialog parts — leave empty to build them in code")]
    [SerializeField] private GameObject root;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Button yesButton;
    [SerializeField] private TextMeshProUGUI yesButtonLabel;
    [SerializeField] private Button noButton;
    [SerializeField] private TextMeshProUGUI noButtonLabel;

    private Canvas canvas;
    private CanvasGroup group;
    private Action pendingYes, pendingNo;

    private bool Authored => root != null && messageText != null && yesButton != null && noButton != null;

    private void Build(string message, Action onYes, Action onNo, string yesLabel, string noLabel)
    {
        s_open = true;
        pendingYes = onYes;
        pendingNo = onNo;

        // ---- canvas ----
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32766; // just under the loading overlay
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
        }
        // We need an EventSystem for UGUI clicks. If the scene has none
        // (Menu / cutscenes sometimes strip it) attach one to us.
        if (EventSystem.current == null)
        {
            var es = new GameObject("[ConfirmDialog.EventSystem]",
                typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }

        // ==== AN AUTHORED DIALOG IS FILLED IN, NOT REBUILT ====
        //
        // Two strings and two callbacks. Everything below this branch exists
        // only for the case where nobody has authored one yet.
        if (Authored)
        {
            root.SetActive(true);
            messageText.text = message;

            string yesA = string.IsNullOrEmpty(yesLabel) ? LocalizationManager.Tr("Yes") : yesLabel;
            string noA  = string.IsNullOrEmpty(noLabel)  ? LocalizationManager.Tr("No")  : noLabel;
            if (yesButtonLabel != null) yesButtonLabel.text = yesA;
            if (noButtonLabel != null) noButtonLabel.text = noA;

            // RemoveAllListeners, because this object survives scene loads and
            // the same buttons are reused for every question ever asked.
            yesButton.onClick.RemoveAllListeners();
            yesButton.onClick.AddListener(OnYes);
            noButton.onClick.RemoveAllListeners();
            noButton.onClick.AddListener(OnNo);
            return;
        }

        // ---- rebuild root ----
        if (root != null) Destroy(root);
        root = new GameObject("Root");
        root.transform.SetParent(transform, false);

        // dim background (also swallows clicks behind the dialog)
        var dim = new GameObject("Dim", typeof(Image));
        dim.transform.SetParent(root.transform, false);
        var dimRT = dim.GetComponent<RectTransform>();
        dimRT.anchorMin = Vector2.zero; dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero; dimRT.offsetMax = Vector2.zero;
        dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        // panel
        var panel = new GameObject("Panel", typeof(Image));
        panel.transform.SetParent(root.transform, false);
        var pRT = panel.GetComponent<RectTransform>();
        pRT.anchorMin = new Vector2(0.5f, 0.5f);
        pRT.anchorMax = new Vector2(0.5f, 0.5f);
        pRT.pivot = new Vector2(0.5f, 0.5f);
        pRT.sizeDelta = new Vector2(720f, 300f);
        panel.GetComponent<Image>().color = new Color(0.09f, 0.09f, 0.11f, 0.98f);

        // thin border
        var border = new GameObject("Border", typeof(Image));
        border.transform.SetParent(panel.transform, false);
        var bRT = border.GetComponent<RectTransform>();
        bRT.anchorMin = Vector2.zero; bRT.anchorMax = Vector2.one;
        bRT.offsetMin = new Vector2(-2f, -2f); bRT.offsetMax = new Vector2(2f, 2f);
        border.GetComponent<Image>().color = new Color(0.75f, 0.66f, 0.42f, 0.85f);
        border.transform.SetAsFirstSibling();

        // message
        var msgGO = new GameObject("Message", typeof(TextMeshProUGUI));
        msgGO.transform.SetParent(panel.transform, false);
        var msg = msgGO.GetComponent<TextMeshProUGUI>();
        msg.text = message;
        msg.fontSize = 32f;
        msg.color = new Color(0.94f, 0.9f, 0.78f, 1f);
        msg.alignment = TextAlignmentOptions.Center;
        msg.enableWordWrapping = true;
        var mRT = msg.GetComponent<RectTransform>();
        mRT.anchorMin = new Vector2(0.05f, 0.4f);
        mRT.anchorMax = new Vector2(0.95f, 0.95f);
        mRT.offsetMin = Vector2.zero; mRT.offsetMax = Vector2.zero;

        // Yes / No buttons
        string yesTxt = string.IsNullOrEmpty(yesLabel) ? LocalizationManager.Tr("Yes") : yesLabel;
        string noTxt  = string.IsNullOrEmpty(noLabel)  ? LocalizationManager.Tr("No")  : noLabel;
        MakeButton(panel.transform, yesTxt, new Vector2(0.1f, 0.08f), new Vector2(0.48f, 0.30f), OnYes);
        MakeButton(panel.transform, noTxt,  new Vector2(0.52f, 0.08f), new Vector2(0.9f, 0.30f), OnNo);
    }

    private void MakeButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, Action onClick)
    {
        var btnGO = new GameObject(label, typeof(Image), typeof(Button));
        btnGO.transform.SetParent(parent, false);
        var rt = btnGO.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        btnGO.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.2f, 1f);

        var btn = btnGO.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = new Color(0.16f, 0.16f, 0.2f, 1f);
        colors.highlightedColor = new Color(0.28f, 0.24f, 0.16f, 1f);
        colors.pressedColor     = new Color(0.38f, 0.32f, 0.20f, 1f);
        colors.selectedColor    = new Color(0.28f, 0.24f, 0.16f, 1f);
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick?.Invoke());

        var lblGO = new GameObject("Label", typeof(TextMeshProUGUI));
        lblGO.transform.SetParent(btnGO.transform, false);
        var lbl = lblGO.GetComponent<TextMeshProUGUI>();
        lbl.text = label;
        lbl.fontSize = 30f;
        lbl.color = new Color(0.94f, 0.9f, 0.78f, 1f);
        lbl.alignment = TextAlignmentOptions.Center;
        var lRT = lbl.GetComponent<RectTransform>();
        lRT.anchorMin = Vector2.zero; lRT.anchorMax = Vector2.one;
        lRT.offsetMin = Vector2.zero; lRT.offsetMax = Vector2.zero;
    }

    private void OnYes()
    {
        var cb = pendingYes;
        Close();
        try { cb?.Invoke(); } catch (Exception e) { Debug.LogError($"[ConfirmDialog] onYes threw: {e}"); }
    }

    private void OnNo()
    {
        var cb = pendingNo;
        Close();
        try { cb?.Invoke(); } catch (Exception e) { Debug.LogError($"[ConfirmDialog] onNo threw: {e}"); }
    }

    private void Close()
    {
        s_open = false;
        pendingYes = null;
        pendingNo = null;

        // An AUTHORED dialog is hidden, never destroyed. Destroying it would
        // work exactly once and then silently stop opening for the rest of the
        // session, because there is nothing left to fill in. Only the
        // code-built tree is thrown away, which is what it was made for.
        if (Authored) { root.SetActive(false); return; }

        if (root != null) { Destroy(root); root = null; }
    }

    // Esc = No — matches the main-menu confirm dialog's mouse behaviour.
    private void Update()
    {
        if (!s_open) return;
        if (Input.GetKeyDown(KeyCode.Escape)) OnNo();
    }
}
