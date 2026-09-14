using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

[RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
public class MissionPaperUI : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descText;
    public TextMeshProUGUI rewardText;
    public Button acceptButton;

    private MissionData myMissionData;

    [Header("Animation Settings")]
    public float flyDuration = 0.6f;
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        acceptButton.onClick.AddListener(AcceptMission);
    }

    public void SetupPaper(MissionData baseData, float multiplier)
    {
        myMissionData = baseData;

        int finalTarget = Mathf.RoundToInt(baseData.targetAmount * multiplier);
        int finalWood = Mathf.RoundToInt(baseData.woodReward * multiplier);
        int finalStone = Mathf.RoundToInt(baseData.stoneReward * multiplier);
        int finalFood = Mathf.RoundToInt(baseData.foodReward * multiplier);
        int finalDiamond = Mathf.RoundToInt(baseData.diamondReward * multiplier);

        titleText.text = LocalizationManager.Tr(baseData.missionName);

        // Primary line: WHAT the player must do, in a call-to-action colour.
        // Secondary line: the flavor description (smaller, muted).
        string objective = MissionData.BuildObjective(baseData.missionType, finalTarget);
        string flavor = LocalizationManager.Tr(baseData.missionDescription);
        bool hasFlavor = !string.IsNullOrEmpty(flavor)
                         && flavor != "Mission description..."
                         && flavor != baseData.missionName;

        string primary = $"<size=110%><color=#8B0000><b>{objective}</b></color></size>";
        if (hasFlavor)
            descText.text = $"{primary}\n\n<size=90%><color=#5C4033>{flavor}</color></size>";
        else
            descText.text = primary;

        BuildRewardRow(finalWood, finalStone, finalFood, finalDiamond);
    }

    // ==== THE REWARD IS FOUR NUMBERS, NOT A SENTENCE ====
    //
    // This read "REWARDS: 40 Wood  25 Stone  10 Food" — a label, then the name
    // of each resource spelled out beside its amount, in four different text
    // colours. On a small paper card next to an objective line and a line of
    // flavour that is a wall of words, and the part the player actually scans
    // for — how much of what — is the part buried deepest in it.
    //
    // The icons already exist and are already the language the rest of the game
    // uses for these three resources: the region victory screen and the chest
    // rewards both draw them from the same sheet. Reusing them here means the
    // card says the same thing in a quarter of the space, and says it in a form
    // the player has already learned everywhere else.
    private Transform _rewardRow;

    private void BuildRewardRow(int wood, int stone, int food, int diamond)
    {
        if (rewardText == null) return;

        var set = ReliquarySet.Load();
        // No icon set resolved: keep the words rather than showing nothing.
        if (set == null || (set.woodIcon == null && set.stoneIcon == null && set.foodIcon == null))
        {
            rewardText.gameObject.SetActive(true);
            string rewText = $"<b>{LocalizationManager.Tr("MISSION_REWARDS_LABEL")}</b> ";
            if (wood > 0)    rewText += $"<color=#5C4033>{LocalizationManager.Tr("MISSION_RES_WOOD", wood)}</color>  ";
            if (stone > 0)   rewText += $"<color=#4A4A4A>{LocalizationManager.Tr("MISSION_RES_STONE", stone)}</color>  ";
            if (food > 0)    rewText += $"<color=#B85E00>{LocalizationManager.Tr("MISSION_RES_FOOD", food)}</color>  ";
            if (diamond > 0) rewText += $"<color=#005500>{LocalizationManager.Tr("MISSION_RES_GEMS", diamond)}</color>";
            rewardText.text = rewText;
            return;
        }

        // The row takes the label's place exactly, so it lands wherever the card
        // was laid out to put the rewards — no prefab has to be re-authored, and
        // a card with a different layout still works.
        if (_rewardRow == null)
        {
            var go = new GameObject("RewardRow", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(rewardText.transform.parent, false);
            rt.SetSiblingIndex(rewardText.transform.GetSiblingIndex());

            var src = rewardText.rectTransform;
            rt.anchorMin = src.anchorMin; rt.anchorMax = src.anchorMax;
            rt.pivot = src.pivot;
            rt.anchoredPosition = src.anchoredPosition;
            rt.sizeDelta = src.sizeDelta;

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            _rewardRow = rt;
        }

        for (int i = _rewardRow.childCount - 1; i >= 0; i--) Destroy(_rewardRow.GetChild(i).gameObject);
        rewardText.gameObject.SetActive(false);

        float h = Mathf.Max(22f, rewardText.fontSize * 1.35f);
        AddReward(set.woodIcon, wood, h);
        AddReward(set.stoneIcon, stone, h);
        AddReward(set.foodIcon, food, h);
        // No gem icon exists in the set, so diamonds keep a word — better an
        // honest label than a wood log standing in for a diamond.
        if (diamond > 0) AddReward(null, diamond, h, LocalizationManager.Tr("MISSION_RES_GEMS", diamond));
    }

    private void AddReward(Sprite icon, int amount, float height, string overrideLabel = null)
    {
        if (amount <= 0 && overrideLabel == null) return;

        var cell = new GameObject(icon != null ? "Reward" : "RewardText", typeof(RectTransform));
        cell.transform.SetParent(_rewardRow, false);
        var row = cell.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 4f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        row.childControlWidth = true;
        row.childControlHeight = true;
        cell.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (icon != null)
        {
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(cell.transform, false);
            var img = iconGo.AddComponent<Image>();
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var le = iconGo.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.preferredWidth = height;
        }

        var textGo = new GameObject("Amount", typeof(RectTransform));
        textGo.transform.SetParent(cell.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = overrideLabel ?? amount.ToString();
        tmp.font = rewardText.font;
        tmp.fontSize = rewardText.fontSize;
        tmp.color = rewardText.color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        textGo.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void AcceptMission()
    {
        acceptButton.interactable = false;

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_QuestAccept);

        transform.SetParent(transform.root);
        transform.SetAsLastSibling();

        SaveMissionForWorld();
        StartCoroutine(FlyToCornerRoutine());
    }

    private void SaveMissionForWorld()
    {
        if (MissionManager.Instance != null)
        {
            MissionManager.Instance.AddNewMission(myMissionData, myMissionData.targetAmount);
        }
    }

    private IEnumerator FlyToCornerRoutine()
    {
        Vector3 startPos = rectTransform.localPosition;

        // ������ ��������� ������ ������ �������� �������: ������ ����� � ������ 
        // (��������� ����, �� �������� ������ To-Do ����)
        // -600 �� �� X ��������� ������� ���� ����!
        Vector3 targetPos = startPos + new Vector3(-600f, 300f, 0f);
        Vector3 startScale = rectTransform.localScale;

        float timer = 0f;
        while (timer < flyDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / flyDuration;

            // ���������� ��� ������ ��������� (�������� ����� ����������, ���� ���� ������)
            float easeInBack = progress * progress * (2.70158f * progress - 1.70158f);

            // ������������
            rectTransform.localPosition = Vector3.LerpUnclamped(startPos, targetPos, easeInBack);
            rectTransform.localScale = Vector3.Lerp(startScale, Vector3.zero, progress);

            // ����� ��������� �� ��� ������� ��� �����
            rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(0, -25f, progress));

            canvasGroup.alpha = Mathf.Lerp(1f, 0f, progress);

            yield return null;
        }
        Destroy(gameObject);
    }
}