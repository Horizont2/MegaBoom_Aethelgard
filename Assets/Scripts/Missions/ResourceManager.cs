using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance;

    [Header("STASH (Склад у Таборі)")]
    public int stashWood = 0;
    public int stashStone = 0;
    public int stashFood = 0;
    public int diamonds = 0;

    [Header("Base Capacities (Склад)")]
    public int baseMaxWood = 200;
    public int baseMaxStone = 100;
    public int baseMaxFood = 50;
    public int extraCapacity = 0;

    [Header("RUN INVENTORY (Зібране в Подорожі)")]
    public int runWood = 0;
    public int runStone = 0;
    public int runFood = 0;

    [Header("Backpack Capacities (Рюкзак)")]
    public int baseRunMaxWood = 100;
    public int baseRunMaxStone = 50;
    public int baseRunMaxFood = 30;
    public int extraRunCapacity = 0;

    [Header("UI Texts")]
    public TextMeshProUGUI inventoryTitleText;
    public TextMeshProUGUI woodText;
    public TextMeshProUGUI stoneText;
    public TextMeshProUGUI foodText;
    public TextMeshProUGUI diamondsText;

    [Header("UI Fills (Зображення замість Слайдерів)")]
    public Image woodFill;
    public Image stoneFill;
    public Image foodFill;
    public float fillLerpSpeed = 5f;

    [Header("UI Popups")]
    public ResourcePopup woodPopup;
    public ResourcePopup stonePopup;
    public ResourcePopup foodPopup;

    private bool isCamp => SceneManager.GetActiveScene().name == "CampScene";

    // Словник для відстеження активних анімацій тексту, щоб вони не накладались одна на одну
    private Dictionary<TextMeshProUGUI, Coroutine> activeTextTweens = new Dictionary<TextMeshProUGUI, Coroutine>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        LoadStash();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (isCamp) ClearRunInventory();
        UpdateUI();
    }

    private void Update()
    {
        int targetWood = isCamp ? stashWood : runWood;
        int targetStone = isCamp ? stashStone : runStone;
        int targetFood = isCamp ? stashFood : runFood;

        if (woodFill != null)
            woodFill.fillAmount = Mathf.Lerp(woodFill.fillAmount, (float)targetWood / GetCurrentMax("Wood"), Time.deltaTime * fillLerpSpeed);

        if (stoneFill != null)
            stoneFill.fillAmount = Mathf.Lerp(stoneFill.fillAmount, (float)targetStone / GetCurrentMax("Stone"), Time.deltaTime * fillLerpSpeed);

        if (foodFill != null)
            foodFill.fillAmount = Mathf.Lerp(foodFill.fillAmount, (float)targetFood / GetCurrentMax("Food"), Time.deltaTime * fillLerpSpeed);
    }

    private int GetCurrentMax(string type)
    {
        return isCamp ? GetMax(type) : GetRunMax(type);
    }

    public void SetExtraCapacity(int bonusAmount)
    {
        extraCapacity = bonusAmount;
        UpdateUI();
    }

    public int GetMax(string type)
    {
        if (type == "Wood") return baseMaxWood + extraCapacity;
        if (type == "Stone") return baseMaxStone + extraCapacity;
        if (type == "Food") return baseMaxFood + extraCapacity;
        return 999999;
    }

    public void SetExtraRunCapacity(int bonusAmount)
    {
        extraRunCapacity = bonusAmount;
        UpdateUI();
    }

    public int GetRunMax(string type)
    {
        if (type == "Wood") return baseRunMaxWood + extraRunCapacity;
        if (type == "Stone") return baseRunMaxStone + extraRunCapacity;
        if (type == "Food") return baseRunMaxFood + extraRunCapacity;
        return 999;
    }

    public void AddRunResources(int addWood, int addStone, int addFood)
    {
        if (isCamp) return;

        int oldWood = runWood;
        int oldStone = runStone;
        int oldFood = runFood;

        runWood = Mathf.Min(runWood + addWood, GetRunMax("Wood"));
        runStone = Mathf.Min(runStone + addStone, GetRunMax("Stone"));
        runFood = Mathf.Min(runFood + addFood, GetRunMax("Food"));

        int actualAddedWood = runWood - oldWood;
        int actualAddedStone = runStone - oldStone;
        int actualAddedFood = runFood - oldFood;

        // Запускаємо анімації тексту та попапи тільки якщо ресурс дійсно додався
        if (actualAddedWood > 0)
        {
            if (woodPopup != null) woodPopup.ShowChange(actualAddedWood);
            BounceText(woodText);
        }
        if (actualAddedStone > 0)
        {
            if (stonePopup != null) stonePopup.ShowChange(actualAddedStone);
            BounceText(stoneText);
        }
        if (actualAddedFood > 0)
        {
            if (foodPopup != null) foodPopup.ShowChange(actualAddedFood);
            BounceText(foodText);
        }

        UpdateUI();
    }

    public void AddStashResources(int addWood, int addStone, int addFood)
    {
        int oldWood = stashWood; int oldStone = stashStone; int oldFood = stashFood;

        stashWood = Mathf.Min(stashWood + addWood, GetMax("Wood"));
        stashStone = Mathf.Min(stashStone + addStone, GetMax("Stone"));
        stashFood = Mathf.Min(stashFood + addFood, GetMax("Food"));

        int actualAddedWood = stashWood - oldWood;
        int actualAddedStone = stashStone - oldStone;
        int actualAddedFood = stashFood - oldFood;

        if (actualAddedWood > 0)
        {
            if (woodPopup != null) woodPopup.ShowChange(actualAddedWood);
            BounceText(woodText);
        }
        if (actualAddedStone > 0)
        {
            if (stonePopup != null) stonePopup.ShowChange(actualAddedStone);
            BounceText(stoneText);
        }
        if (actualAddedFood > 0)
        {
            if (foodPopup != null) foodPopup.ShowChange(actualAddedFood);
            BounceText(foodText);
        }

        SaveStash();
        UpdateUI();
    }

    public bool CanAffordStash(int costWood, int costStone, int costFood)
    {
        return (stashWood >= costWood && stashStone >= costStone && stashFood >= costFood);
    }

    public void SpendStashResources(int costWood, int costStone, int costFood)
    {
        stashWood -= costWood;
        stashStone -= costStone;
        stashFood -= costFood;

        if (woodPopup != null && costWood > 0) woodPopup.ShowChange(-costWood);
        if (stonePopup != null && costStone > 0) stonePopup.ShowChange(-costStone);
        if (foodPopup != null && costFood > 0) foodPopup.ShowChange(-costFood);

        SaveStash();
        UpdateUI();
    }

    public void EvacuateRunToStash()
    {
        int maxW = GetMax("Wood");
        int maxS = GetMax("Stone");
        int maxF = GetMax("Food");

        int woodToStore = Mathf.Min(maxW - stashWood, runWood);
        int stoneToStore = Mathf.Min(maxS - stashStone, runStone);
        int foodToStore = Mathf.Min(maxF - stashFood, runFood);

        stashWood += woodToStore;
        stashStone += stoneToStore;
        stashFood += foodToStore;

        int excessWood = runWood - woodToStore;
        int excessStone = runStone - stoneToStore;
        int excessFood = runFood - foodToStore;

        int bonusDiamonds = (excessWood / 5) + (excessStone / 3) + (excessFood / 2);
        diamonds += bonusDiamonds;

        ClearRunInventory();
        SaveStash();
    }

    public void ClearRunInventory()
    {
        runWood = 0; runStone = 0; runFood = 0;
        UpdateUI();
    }

    public void UpdateUI()
    {
        if (inventoryTitleText != null)
        {
            inventoryTitleText.text = isCamp ? "CAMP STASH" : "BACKPACK";
        }

        if (isCamp)
        {
            int maxStashWood = GetMax("Wood");
            int maxStashStone = GetMax("Stone");
            int maxStashFood = GetMax("Food");

            if (woodText) woodText.text = $"( {stashWood} / {maxStashWood} )";
            if (stoneText) stoneText.text = $"( {stashStone} / {maxStashStone} )";
            if (foodText) foodText.text = $"( {stashFood} / {maxStashFood} )";
        }
        else
        {
            int maxRunWood = GetRunMax("Wood");
            int maxRunStone = GetRunMax("Stone");
            int maxRunFood = GetRunMax("Food");

            string wColor = runWood >= maxRunWood ? "<color=#8B2E2E>" : "";
            string wEnd = runWood >= maxRunWood ? "</color>" : "";

            string sColor = runStone >= maxRunStone ? "<color=#8B2E2E>" : "";
            string sEnd = runStone >= maxRunStone ? "</color>" : "";

            string fColor = runFood >= maxRunFood ? "<color=#8B2E2E>" : "";
            string fEnd = runFood >= maxRunFood ? "</color>" : "";

            if (woodText) woodText.text = $"( {wColor}{runWood}{wEnd} / {maxRunWood} )";
            if (stoneText) stoneText.text = $"( {sColor}{runStone}{sEnd} / {maxRunStone} )";
            if (foodText) foodText.text = $"( {fColor}{runFood}{fEnd} / {maxRunFood} )";
        }

        if (diamondsText) diamondsText.text = $"Diamonds: {diamonds}";
    }

    // --- СИСТЕМА АНІМАЦІЇ UI (JUICE) ---

    private void BounceText(TextMeshProUGUI textElement)
    {
        if (textElement == null) return;

        // Перериваємо попередню анімацію, якщо ресурс збирається дуже швидко (спам)
        if (activeTextTweens.ContainsKey(textElement) && activeTextTweens[textElement] != null)
        {
            StopCoroutine(activeTextTweens[textElement]);
        }

        activeTextTweens[textElement] = StartCoroutine(TextBounceRoutine(textElement));
    }

    private IEnumerator TextBounceRoutine(TextMeshProUGUI textElement)
    {
        Vector3 originalScale = Vector3.one;
        Vector3 punchScale = new Vector3(1.35f, 1.35f, 1.35f); // Наскільки сильно збільшується
        float upDuration = 0.08f; // Швидкість збільшення (дуже швидко)
        float downDuration = 0.2f; // Швидкість повернення назад (трохи повільніше)
        float elapsed = 0f;

        // Збільшення
        while (elapsed < upDuration)
        {
            elapsed += Time.deltaTime;
            textElement.rectTransform.localScale = Vector3.Lerp(originalScale, punchScale, elapsed / upDuration);
            yield return null;
        }

        // Повернення
        elapsed = 0f;
        while (elapsed < downDuration)
        {
            elapsed += Time.deltaTime;
            textElement.rectTransform.localScale = Vector3.Lerp(punchScale, originalScale, elapsed / downDuration);
            yield return null;
        }

        textElement.rectTransform.localScale = originalScale;
    }

    // ------------------------------------

    public void SaveStash()
    {
        PlayerPrefs.SetInt("Stash_Wood", stashWood);
        PlayerPrefs.SetInt("Stash_Stone", stashStone);
        PlayerPrefs.SetInt("Stash_Food", stashFood);
        PlayerPrefs.SetInt("PlayerDiamonds", diamonds);
        PlayerPrefs.Save();
    }

    private void LoadStash()
    {
        stashWood = PlayerPrefs.GetInt("Stash_Wood", 50);
        stashStone = PlayerPrefs.GetInt("Stash_Stone", 20);
        stashFood = PlayerPrefs.GetInt("Stash_Food", 10);
        diamonds = PlayerPrefs.GetInt("PlayerDiamonds", 0);
    }
}