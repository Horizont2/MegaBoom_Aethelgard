using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Collections;

public enum ResourceRewardType { Wood, Stone, Food, Diamond }

public class MapResourceNode : MonoBehaviour, IPointerClickHandler
{
    [Header("Resource Settings")]
    public ResourceRewardType resourceType;
    public float maxAccumulationHours = 8f;

    [Header("Debug (Для тестування)")]
    public bool debugForceMaxTime = false;

    [Header("Visuals & Juice")]
    public ParticleSystem collectVFX;
    public CanvasGroup canvasGroup;
    public RectTransform nodeRect;

    private RegionData myRegion;
    private bool isCollected = false;

    public void Setup(RegionData region)
    {
        myRegion = region;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (isCollected || myRegion == null) return;
        CollectResource();
    }

    public int GetCurrentAccumulated()
    {
        if (myRegion == null) return 0;

        string prefsKey = $"LastCollect_{myRegion.regionID}_{resourceType}";
        if (!PlayerPrefs.HasKey(prefsKey)) return 0;

        long temp = Convert.ToInt64(PlayerPrefs.GetString(prefsKey));
        DateTime lastCollectTime = DateTime.FromBinary(temp);
        TimeSpan timePassed = DateTime.Now - lastCollectTime;

        float hoursPassed = Mathf.Min((float)timePassed.TotalHours, maxAccumulationHours);
        if (debugForceMaxTime) hoursPassed = maxAccumulationHours;

        int currentLevel = PlayerPrefs.GetInt("RegionLevel_" + myRegion.regionID, 1);
        if (myRegion.upgradeLevels == null || myRegion.upgradeLevels.Length < currentLevel) return 0;

        RegionLevelData levelData = myRegion.upgradeLevels[currentLevel - 1];
        float ratePerHour = 0;

        switch (resourceType)
        {
            case ResourceRewardType.Wood: ratePerHour = levelData.passiveWood; break;
            case ResourceRewardType.Stone: ratePerHour = levelData.passiveStone; break;
            case ResourceRewardType.Food: ratePerHour = levelData.passiveFood; break;
            case ResourceRewardType.Diamond: ratePerHour = levelData.passiveDiamonds; break;
        }

        return Mathf.FloorToInt(hoursPassed * ratePerHour);
    }

    private void CollectResource()
    {
        if (ResourceManager.Instance == null) return;

        // ПЕРЕВІРКА: ЧИ ПОВНИЙ СКЛАД?
        bool isFull = false;
        switch (resourceType)
        {
            case ResourceRewardType.Wood: isFull = ResourceManager.Instance.stashWood >= ResourceManager.Instance.GetMax("Wood"); break;
            case ResourceRewardType.Stone: isFull = ResourceManager.Instance.stashStone >= ResourceManager.Instance.GetMax("Stone"); break;
            case ResourceRewardType.Food: isFull = ResourceManager.Instance.stashFood >= ResourceManager.Instance.GetMax("Food"); break;
        }

        if (isFull)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Error); // Звук помилки
            GameLog.Info($"<color=red>Склад для {resourceType} повний!</color> Витратьте ресурси.");
            // Тут можна додати UI підказку на екран (Pop-up) "STORAGE FULL"
            return;
        }

        int amountToGive = GetCurrentAccumulated();

        if (amountToGive <= 0)
        {
            Destroy(gameObject);
            return;
        }

        // Блокуємо повторний клік і скидаємо таймер
        isCollected = true;
        string prefsKey = $"LastCollect_{myRegion.regionID}_{resourceType}";
        PlayerPrefs.SetString(prefsKey, DateTime.Now.ToBinary().ToString());
        PlayerPrefs.Save();

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
        if (collectVFX != null) collectVFX.Play();

        // ДОДАЄМО НА СКЛАД
        switch (resourceType)
        {
            case ResourceRewardType.Wood: ResourceManager.Instance.AddStashResources(amountToGive, 0, 0); break;
            case ResourceRewardType.Stone: ResourceManager.Instance.AddStashResources(0, amountToGive, 0); break;
            case ResourceRewardType.Food: ResourceManager.Instance.AddStashResources(0, 0, amountToGive); break;
            case ResourceRewardType.Diamond:
                ResourceManager.Instance.diamonds += amountToGive;
                ResourceManager.Instance.UpdateUI();
                break;
        }

        StartCoroutine(AnimateCollection());
    }

    private IEnumerator AnimateCollection()
    {
        Vector2 startPos = nodeRect.anchoredPosition;
        Vector2 targetPos = startPos + new Vector2(0, 100f);
        float duration = 0.5f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float smoothT = 1f - Mathf.Pow(1f - t, 3f);

            nodeRect.anchoredPosition = Vector2.Lerp(startPos, targetPos, smoothT);
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, t);
            yield return null;
        }

        Destroy(gameObject);
    }
}