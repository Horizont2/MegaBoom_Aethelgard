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
        string desc = LocalizationManager.Tr(baseData.missionDescription);
        string targetLine = LocalizationManager.Tr("MISSION_TARGET_LABEL", finalTarget);
        descText.text = $"{desc}\n\n<color=#8B0000><b>{targetLine}</b></color>";

        string rewText = $"<b>{LocalizationManager.Tr("MISSION_REWARDS_LABEL")}</b> ";
        if (finalWood > 0)    rewText += $"<color=#5C4033>{LocalizationManager.Tr("MISSION_RES_WOOD", finalWood)}</color>  ";
        if (finalStone > 0)   rewText += $"<color=#4A4A4A>{LocalizationManager.Tr("MISSION_RES_STONE", finalStone)}</color>  ";
        if (finalFood > 0)    rewText += $"<color=#B85E00>{LocalizationManager.Tr("MISSION_RES_FOOD", finalFood)}</color>  ";
        if (finalDiamond > 0) rewText += $"<color=#005500>{LocalizationManager.Tr("MISSION_RES_GEMS", finalDiamond)}</color>";

        rewardText.text = rewText;
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