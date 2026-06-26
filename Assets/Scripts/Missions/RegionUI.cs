using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;

[RequireComponent(typeof(Image))]
public class RegionUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Region Data")]
    public RegionData myRegionData;

    [Header("UI References")]
    public GameObject lockIcon;
    public Image borderImage;
    [Tooltip("��� ��� ����������� ������ ��� ��� ��� �������")]
    public Image stormLayer;

    [Header("Map Label (Ink on Paper)")]
    public TextMeshProUGUI mapLabelText;

    [Header("Level Indicator")]
    public Image levelIconImage;
    [Tooltip("���� ��������� 5 �������� ������� (Lv1, Lv2, Lv3, Lv4, Lv5)")]
    public Sprite[] levelSprites;

    [Header("AAA Polish")]
    public Image lightningFlashImage;
    public Sprite forestStorm;
    public Sprite desertStorm;
    public Sprite winterStorm;

    private Image regionImage;
    private MapPanelUI mainMapUI;

    [Header("Colors - Fill States")]
    public Color lockedColor = new Color(0.1f, 0.12f, 0.18f, 0.8f);
    public Color availableColor = new Color(1f, 0.9f, 0.6f, 0.15f);
    public Color conqueredColor = new Color(0.8f, 0.7f, 1f, 0.25f);

    [Header("Colors - Hover")]
    public Color hoverAvailableColor = new Color(0.6f, 0.4f, 1f, 0.4f);
    public Color hoverLockedColor = new Color(0.8f, 0.2f, 0.2f, 0.4f);
    public Color hoverConqueredColor = new Color(1f, 0.8f, 0.2f, 0.4f);

    [Header("Storm Visuals")]
    public Color darkFogColor = new Color(0.08f, 0.1f, 0.15f, 0.95f);
    public Color lightFogColor = new Color(0.15f, 0.18f, 0.25f, 0.95f);

    private float breathSpeed;
    private float breathOffset;
    private Color targetImageColor;
    private bool isHovered = false;

    void Awake()
    {
        regionImage = GetComponent<Image>();
        regionImage.alphaHitTestMinimumThreshold = 0.1f;
        mainMapUI = FindFirstObjectByType<MapPanelUI>(FindObjectsInactive.Include);

        breathSpeed = Random.Range(0.4f, 0.8f);
        breathOffset = Random.Range(0f, 10f);
    }

    void OnEnable()
    {
        MapTableInteract.OnMapFullyOpened += TriggerRevealAnimation;
        MapProgressionManager.OnMapStateChanged += RefreshUIState;

        UpdateRegionVisuals(false);
        RefreshUIState();
    }

    void OnDisable()
    {
        MapTableInteract.OnMapFullyOpened -= TriggerRevealAnimation;
        MapProgressionManager.OnMapStateChanged -= RefreshUIState;
    }

    void Update()
    {
        regionImage.color = Color.Lerp(regionImage.color, targetImageColor, Time.deltaTime * 10f);

        if (myRegionData != null && myRegionData.currentState == RegionState.Locked && stormLayer != null)
        {
            float stormPulse = (Mathf.Sin(Time.time * breathSpeed + breathOffset) + 1f) / 2f;

            if (myRegionData.regionBiome == RegionBiome.Forest)
            {
                stormLayer.color = Color.Lerp(darkFogColor, lightFogColor, stormPulse);
            }
            else
            {
                Color baseColor = stormLayer.color;
                stormLayer.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Lerp(0.7f, 0.95f, stormPulse));
            }
        }

        bool isSelected = mainMapUI != null && mainMapUI.GetCurrentRegion() == myRegionData && mainMapUI.IsPanelOpen();
        if (isSelected)
        {
            float pulse = (Mathf.Sin(Time.time * 4f) + 1.2f) * 0.5f;
            transform.localScale = Vector3.one * (1f + pulse * 0.05f);
        }
        else
        {
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, Time.deltaTime * 10f);
        }
    }

    private void RefreshUIState()
    {
        RefreshLevelIcon();
        UpdateMapLabel();
    }

    private void UpdateMapLabel()
    {
        if (mapLabelText == null || myRegionData == null) return;

        if (myRegionData.currentState == RegionState.Locked)
        {
            mapLabelText.text = "???";
            mapLabelText.alpha = 0.3f;
        }
        else
        {
            mapLabelText.text = myRegionData.regionName.ToUpper();
            mapLabelText.alpha = 0.8f;
        }
    }

    private void UpdateRegionVisuals(bool hover)
    {
        isHovered = hover;
        RegionState state = myRegionData != null ? myRegionData.currentState : RegionState.Locked;

        if (lockIcon != null) lockIcon.SetActive(state == RegionState.Locked);

        if (state == RegionState.Locked)
        {
            targetImageColor = hover ? hoverLockedColor : lockedColor;

            if (stormLayer != null && !myRegionData.isNewlyUnlocked)
            {
                stormLayer.gameObject.SetActive(true);

                if (myRegionData != null)
                {
                    switch (myRegionData.regionBiome)
                    {
                        case RegionBiome.Forest:
                            if (forestStorm != null) stormLayer.sprite = forestStorm;
                            stormLayer.color = darkFogColor;
                            break;
                        case RegionBiome.Desert:
                            if (desertStorm != null) stormLayer.sprite = desertStorm;
                            stormLayer.color = new Color(0.8f, 0.6f, 0.3f, 0.9f);
                            break;
                        case RegionBiome.Winter:
                            if (winterStorm != null) stormLayer.sprite = winterStorm;
                            stormLayer.color = new Color(0.7f, 0.85f, 1f, 0.9f);
                            break;
                    }
                }
            }
        }
        else if (state == RegionState.Available)
        {
            targetImageColor = hover ? hoverAvailableColor : availableColor;
            if (stormLayer != null && !myRegionData.isNewlyUnlocked) stormLayer.gameObject.SetActive(false);
        }
        else if (state == RegionState.Conquered)
        {
            targetImageColor = hover ? hoverConqueredColor : conqueredColor;
            if (stormLayer != null) stormLayer.gameObject.SetActive(false);
        }
    }

    public void RefreshLevelIcon()
    {
        if (myRegionData == null || levelIconImage == null) return;

        if (myRegionData.currentState != RegionState.Conquered)
        {
            levelIconImage.gameObject.SetActive(false);
            return;
        }

        levelIconImage.gameObject.SetActive(true);
        int currentLevel = PlayerPrefs.GetInt("RegionLevel_" + myRegionData.regionID, 1);

        if (levelSprites != null && levelSprites.Length >= currentLevel && currentLevel > 0)
        {
            levelIconImage.sprite = levelSprites[currentLevel - 1];
        }
    }

    public void DoLightningFlash()
    {
        if (lightningFlashImage != null && myRegionData.currentState == RegionState.Locked)
        {
            StartCoroutine(LightningFlashRoutine());
        }
    }

    private IEnumerator LightningFlashRoutine()
    {
        float duration = 0.2f;

        for (int i = 0; i < 2; i++)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Lerp(0.6f, 0f, elapsed / duration);
                lightningFlashImage.color = new Color(1, 1, 1, alpha);
                yield return null;
            }
            yield return new WaitForSeconds(0.05f);
        }

        lightningFlashImage.color = new Color(1, 1, 1, 0f);
    }

    private void TriggerRevealAnimation()
    {
        if (myRegionData != null && myRegionData.currentState == RegionState.Available && myRegionData.isNewlyUnlocked)
        {
            if (stormLayer != null) StartCoroutine(DissolveStormRoutine());
            myRegionData.isNewlyUnlocked = false;
        }
    }

    private IEnumerator DissolveStormRoutine()
    {
        stormLayer.gameObject.SetActive(true);
        float duration = 2.5f;
        float elapsed = 0f;

        Vector3 startScale = stormLayer.transform.localScale;
        Vector3 targetScale = startScale * 1.4f;

        Color startColor = stormLayer.color;
        Color targetTransparent = new Color(startColor.r, startColor.g, startColor.b, 0f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float smoothT = 1f - Mathf.Pow(1f - t, 3f);

            stormLayer.transform.localScale = Vector3.Lerp(startScale, targetScale, smoothT);
            stormLayer.color = Color.Lerp(startColor, targetTransparent, smoothT);

            yield return null;
        }

        stormLayer.gameObject.SetActive(false);
        stormLayer.transform.localScale = startScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
        UpdateRegionVisuals(true);
    }

    public void OnPointerExit(PointerEventData eventData) { UpdateRegionVisuals(false); }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (mainMapUI != null && myRegionData != null)
        {
            // Cinematic dolly to the clicked region before opening the
            // detail panel — the smooth zoom is what reads as AAA. The
            // viewer's Update() lerps localScale + anchoredPosition over
            // its own smoothing window, so we just set the target and
            // let it glide.
            MapInteractiveViewer viewer = FindFirstObjectByType<MapInteractiveViewer>();
            if (viewer != null) viewer.FocusOnNode(GetComponent<RectTransform>(), false);

            mainMapUI.OpenPanel(myRegionData, transform.position);
        }
    }
}