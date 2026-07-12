using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Pop-up shown when a mercenary campaign completes its return trip.
// Subscribed to MercenaryCampaignManager.OnCampaignReturned so it can raise
// itself without polling. Fields are SerializeField references for the
// designer to reskin freely.
public class BattleResultPanel : MonoBehaviour
{
    [Header("Root")]
    public GameObject rootObject;
    public CanvasGroup canvasGroup;
    public Button closeButton;

    [Header("Content")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI regionNameText;
    public TextMeshProUGUI outcomeText;
    public TextMeshProUGUI casualtiesText;
    public TextMeshProUGUI rewardText;

    [Header("Optional VFX")]
    public ParticleSystem winVFX;
    public ParticleSystem lossVFX;

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (rootObject != null) rootObject.SetActive(false);
    }

    private void OnEnable()
    {
        MercenaryCampaignManager.OnCampaignReturned += HandleCampaignReturned;
    }

    private void OnDisable()
    {
        MercenaryCampaignManager.OnCampaignReturned -= HandleCampaignReturned;
    }

    private void HandleCampaignReturned(MercenaryCampaign c)
    {
        if (c == null) return;
        if (rootObject != null) rootObject.SetActive(true);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        RegionData region = null;
        if (MercenaryCampaignManager.Instance != null)
        {
            for (int i = 0; i < MercenaryCampaignManager.Instance.regionCatalogue.Count; i++)
            {
                var r = MercenaryCampaignManager.Instance.regionCatalogue[i];
                if (r != null && r.regionID == c.regionID) { region = r; break; }
            }
        }

        if (titleText != null) titleText.text = c.won
            ? LocalizationManager.Tr("MERC_VICTORY")
            : LocalizationManager.Tr("MERC_DEFEAT");
        if (regionNameText != null && region != null) regionNameText.text = region.regionName.ToUpper();

        if (outcomeText != null)
        {
            outcomeText.text = c.won
                ? LocalizationManager.Tr("MERC_VICTORY_TEXT")
                : LocalizationManager.Tr("MERC_DEFEAT_TEXT");
        }

        int casualties = c.lostUnitUIDs != null ? c.lostUnitUIDs.Count : 0;
        int total = c.armyUIDs != null ? c.armyUIDs.Count : 0;
        if (casualtiesText != null) casualtiesText.text = LocalizationManager.Tr("MERC_LOSSES_LINE", casualties, total);

        if (rewardText != null)
            rewardText.text = c.won && c.diamondsAwarded > 0 ? $"◆ +{c.diamondsAwarded}" : "—";

        if (c.won && winVFX != null) winVFX.Play();
        if (!c.won && lossVFX != null) lossVFX.Play();

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayUI(c.won ? AudioID.UI_Click : AudioID.UI_Error);
    }

    public void Close()
    {
        if (rootObject != null) rootObject.SetActive(false);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }
}
