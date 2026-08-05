using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance;

    [Header("STASH (����� � �����)")]
    public int stashWood = 0;
    public int stashStone = 0;
    public int stashFood = 0;
    public int diamonds = 0;

    [Header("Base Capacities (�����)")]
    public int baseMaxWood = 200;
    public int baseMaxStone = 100;
    public int baseMaxFood = 50;
    public int extraCapacity = 0;

    [Header("RUN INVENTORY (ǳ����� � �������)")]
    public int runWood = 0;
    public int runStone = 0;
    public int runFood = 0;

    [Header("Backpack Capacities (������)")]
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

    [Header("UI Fills (���������� ������ ��������)")]
    public Image woodFill;
    public Image stoneFill;
    public Image foodFill;
    public float fillLerpSpeed = 5f;

    [Header("UI Popups")]
    public ResourcePopup woodPopup;
    public ResourcePopup stonePopup;
    public ResourcePopup foodPopup;

    private bool isCamp => SceneManager.GetActiveScene().name == "CampScene";

    // ������� ��� ���������� �������� �������� ������, ��� ���� �� ����������� ���� �� ����
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
        // Kill any in-flight resource popup on scene change so the
        // floating +X / -X text from the previous scene doesn't ride
        // along to the new HUD.
        if (woodPopup  != null) woodPopup.ForceHide();
        if (stonePopup != null) stonePopup.ForceHide();
        if (foodPopup  != null) foodPopup.ForceHide();
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

        if (diamondTweenTimer < 1f && diamondsText != null)
        {
            diamondTweenTimer = Mathf.Min(1f, diamondTweenTimer + Time.unscaledDeltaTime / DIAMOND_TWEEN_DURATION);
            // Ease-out cubic so the counter races and settles.
            float k = 1f - Mathf.Pow(1f - diamondTweenTimer, 3f);
            int shown = Mathf.RoundToInt(Mathf.Lerp(diamondTweenFrom, diamondTweenTo, k));
            diamondsText.text = LocalizationManager.Tr("Diamonds: {0}", shown);
            // Small punch on the text when a change fires — modest to avoid
            // motion sickness on repeated pickups.
            float pulseK = diamondTweenTimer < 0.25f ? diamondTweenTimer / 0.25f : 1f - (diamondTweenTimer - 0.25f) / 0.75f;
            float scale = 1f + pulseK * 0.10f;
            diamondsText.rectTransform.localScale = new Vector3(scale, scale, 1f);
            if (diamondTweenTimer >= 1f)
                diamondsText.rectTransform.localScale = Vector3.one;
        }
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

        // Route every resource delta through GlobalHUD's polished
        // left-side pickup toast. The old per-resource popup objects
        // (woodPopup / stonePopup / foodPopup) are still bounced for the
        // counter "pop" effect but no longer drive the small +N text —
        // that path now lives entirely in ShowPickupPopup so only ONE
        // toast appears per change, on every scene.
        if (actualAddedWood > 0)
        {
            ShowResourceToast(actualAddedWood, "Wood", new Color(0.85f, 0.6f, 0.35f));
            BounceText(woodText);
        }
        if (actualAddedStone > 0)
        {
            ShowResourceToast(actualAddedStone, "Stone", new Color(0.8f, 0.8f, 0.85f));
            BounceText(stoneText);
        }
        if (actualAddedFood > 0)
        {
            ShowResourceToast(actualAddedFood, "Food", new Color(0.7f, 0.95f, 0.5f));
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
            ShowResourceToast(actualAddedWood, "Wood", new Color(0.85f, 0.6f, 0.35f));
            BounceText(woodText);
        }
        if (actualAddedStone > 0)
        {
            ShowResourceToast(actualAddedStone, "Stone", new Color(0.8f, 0.8f, 0.85f));
            BounceText(stoneText);
        }
        if (actualAddedFood > 0)
        {
            ShowResourceToast(actualAddedFood, "Food", new Color(0.7f, 0.95f, 0.5f));
            BounceText(foodText);
        }

        SaveStash();
        UpdateUI();
    }

    public bool CanAffordStash(int costWood, int costStone, int costFood)
    {
        return (stashWood >= costWood && stashStone >= costStone && stashFood >= costFood);
    }

    // Mercenary system's currency helpers. Diamonds are the "gold" pool for
    // hiring/upgrading mercs and paying campaign rewards — kept in the
    // existing diamonds field so we don't add a second stashed currency.
    public bool CanAffordDiamonds(int cost)
    {
        return diamonds >= cost;
    }

    public void SpendDiamonds(int cost)
    {
        if (cost <= 0) return;
        diamonds -= cost;
        if (diamonds < 0) diamonds = 0;
        ShowResourceToast(-cost, "Diamonds", new Color(0.7f, 0.85f, 1f));
        SaveStash();
        UpdateUI();
    }

    public void AddDiamonds(int amount)
    {
        if (amount <= 0) return;
        diamonds += amount;
        if (diamonds >= 2000) AchievementSystem.Unlock("DEEP_POCKETS");
        ShowResourceToast(amount, "Diamonds", new Color(0.7f, 0.85f, 1f));
        SaveStash();
        UpdateUI();
    }

    public void SpendStashResources(int costWood, int costStone, int costFood)
    {
        stashWood -= costWood;
        stashStone -= costStone;
        stashFood -= costFood;

        if (costWood > 0)  ShowResourceToast(-costWood,  "Wood",  new Color(0.85f, 0.6f, 0.35f));
        if (costStone > 0) ShowResourceToast(-costStone, "Stone", new Color(0.8f, 0.8f, 0.85f));
        if (costFood > 0)  ShowResourceToast(-costFood,  "Food",  new Color(0.7f, 0.95f, 0.5f));

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
            // Route through Tr — the walker only handles labels whose
            // text stays put at scene load, but this method rewrites
            // the title every stash update. Without Tr the UK/RU/etc.
            // player would see the label flash back to English on any
            // resource change.
            inventoryTitleText.text = LocalizationManager.Tr(isCamp ? "CAMP STASH" : "BACKPACK");
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

        // Diamond text is handled by the tween in Update — start a new
        // tween from the currently-displayed number to the new total so
        // gains and spends are legible rather than jumping.
        StartDiamondTween(diamonds);
    }

    // -------- Diamond count animation --------
    private float diamondTweenTimer = 1f;   // 1 == idle
    private float diamondTweenFrom = 0f;
    private float diamondTweenTo = 0f;
    private const float DIAMOND_TWEEN_DURATION = 0.55f;
    private bool diamondDisplayInitialised = false;

    private void StartDiamondTween(int newValue)
    {
        if (diamondsText == null) return;

        if (!diamondDisplayInitialised)
        {
            // First frame — no animation, just show the loaded value.
            diamondTweenFrom = diamondTweenTo = newValue;
            diamondTweenTimer = 1f;
            diamondDisplayInitialised = true;
            diamondsText.text = LocalizationManager.Tr("Diamonds: {0}", newValue);
            return;
        }

        // Pick up from wherever the display currently is so mid-tween
        // updates don't snap.
        int currentDisplayed = Mathf.RoundToInt(Mathf.Lerp(diamondTweenFrom, diamondTweenTo, diamondTweenTimer));
        if (currentDisplayed == newValue) return;
        diamondTweenFrom = currentDisplayed;
        diamondTweenTo = newValue;
        diamondTweenTimer = 0f;
    }

    // Unified pickup toast — replaces the old per-resource popup
    // objects (which only existed on the camp HUD). Routes through
    // GlobalHUD so the polished left-side stack shows on every scene
    // (camp, region capture, gameplay).
    private void ShowResourceToast(int amount, string label, Color color)
    {
        if (GlobalHUD.Instance == null || amount == 0) return;
        string sign = amount > 0 ? "+" : "";
        GlobalHUD.Instance.ShowPickupPopup($"{sign}{amount} {label}", color);
    }

    // --- ������� �Ͳ��ֲ� UI (JUICE) ---

    private void BounceText(TextMeshProUGUI textElement)
    {
        if (textElement == null) return;

        // ���������� ��������� ��������, ���� ������ ��������� ���� ������ (����)
        if (activeTextTweens.ContainsKey(textElement) && activeTextTweens[textElement] != null)
        {
            StopCoroutine(activeTextTweens[textElement]);
        }

        activeTextTweens[textElement] = StartCoroutine(TextBounceRoutine(textElement));
    }

    private IEnumerator TextBounceRoutine(TextMeshProUGUI textElement)
    {
        Vector3 originalScale = Vector3.one;
        Vector3 punchScale = new Vector3(1.35f, 1.35f, 1.35f); // �������� ������ ����������
        float upDuration = 0.08f; // �������� ��������� (���� ������)
        float downDuration = 0.2f; // �������� ���������� ����� (����� ���������)
        float elapsed = 0f;

        // ���������
        while (elapsed < upDuration)
        {
            elapsed += Time.deltaTime;
            textElement.rectTransform.localScale = Vector3.Lerp(originalScale, punchScale, elapsed / upDuration);
            yield return null;
        }

        // ����������
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