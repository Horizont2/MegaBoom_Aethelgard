using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Walks the player through their first gear upgrade, one control at a time.
//
// ==== WHY THE SHOP NEEDS THIS AND THE REST OF THE CAMP DOES NOT ====
//
// Everywhere else, the guide plate names a thing and a trail on the ground
// leads to it. That works because the target is a building the player can see.
// The shop is different: the trail delivers them to a screen with six armour
// categories, a scrolling list, a stats panel and two buttons that both say
// something about buying, and the guide plate — which lives in the world, not
// on this canvas — has nothing left to point at. Players reach the shop and
// bounce off it, which is why the "spend in the shop" step sat uncompleted.
//
// ==== IT WATCHES, IT DOES NOT DRIVE ====
//
// Every step waits for a STATE to become true, never for its own button to be
// clicked. A player who ignores the arrow, opens the helmet list by another
// route, or upgrades a different piece first is credited exactly the same. That
// matters more than it sounds: a tutorial that only accepts the one path it
// drew is a tutorial that gets stuck, and a stuck tutorial in a shop the player
// cannot leave is far worse than no tutorial at all.
[DisallowMultipleComponent]
public class ShopTutorialDirector : MonoBehaviour
{
    // Set by Elias when he funds the upgrade; cleared when it is done.
    public const string PP_ACTIVE = "HelmetQuest_Active";
    public const string PP_DONE = "HelmetQuest_Done";

    [Tooltip("Seconds to wait for the shop UI to finish opening before pointing at anything. Its panels tween in and its layout groups settle a frame or two late, so a hole cut immediately lands over empty space.")]
    public float settleTime = 0.8f;

    private ShopManager _shop;

    public static bool IsQuestActive =>
        PlayerPrefs.GetInt(PP_ACTIVE, 0) == 1 && PlayerPrefs.GetInt(PP_DONE, 0) == 0;

    // CALLED BY ShopManager.Start, and that is the whole point.
    //
    // This was a RuntimeInitializeOnLoadMethod(AfterSceneLoad), which is the
    // exact trap the exploration director fell into and which I documented
    // there before walking straight back into it here: that attribute fires
    // ONCE PER PLAY SESSION, in whichever scene starts first. The shop is a
    // scene the player loads later, so the hook ran in the menu, found no
    // ShopManager, returned, and was never called again. The walkthrough
    // therefore never existed — the trail delivered the player to the shop and
    // then nothing happened, which is exactly what was reported.
    //
    // A call from the shop's own Start cannot have that problem: it runs when
    // and only when a shop exists, every time one is opened.
    public static void InstallIfQuestActive()
    {
        if (!IsQuestActive) return;
        if (FindFirstObjectByType<ShopTutorialDirector>() != null) return;
        if (FindFirstObjectByType<ShopManager>() == null) return;
        new GameObject("[ShopTutorial]").AddComponent<ShopTutorialDirector>();
        Debug.Log("[ShopTutorial] Guided helmet upgrade is armed — walkthrough starting.");
    }

    private void Start() => StartCoroutine(Run());

    private IEnumerator Run()
    {
        _shop = FindFirstObjectByType<ShopManager>();
        if (_shop == null) { Destroy(gameObject); yield break; }

        float t = 0f;
        while (t < settleTime) { t += Time.unscaledDeltaTime; yield return null; }

        // STEP 1 — the helmet category.
        yield return Step(
            () => RectOf(_shop.btnCategoryHelmets),
            "STEP_HELMET_CATEGORY_TITLE", "STEP_HELMET_CATEGORY_BODY",
            () => _shop.IsInsideCategory);

        // STEP 2 — a piece in the list. The first tile is the target, but any
        // selection satisfies it: the point is that they learn the list is
        // clickable, not that they pick the one the arrow chose.
        yield return Step(
            FirstItemRect,
            "STEP_HELMET_PICK_TITLE", "STEP_HELMET_PICK_BODY",
            () => _shop.HasSelection);

        // STEP 3 — spend. Completion is the diamonds actually leaving, checked
        // through the quest flag the shop sets, so a click that failed for lack
        // of funds does not advance the step and quietly lie to the player.
        //
        // The UPGRADE button, not the buy button. ShopManager sets PP_DONE from
        // inside the upgrade handler, so the step could only ever be completed
        // by upgrading — while the arrow pointed at buy, which on a helmet the
        // player already owns reads "EQUIPPED" and does nothing. The words said
        // temper it and the arrow pointed at a button that cannot temper
        // anything.
        yield return Step(
            () => RectOf(_shop.upgradeButton),
            "STEP_HELMET_BUY_TITLE", "STEP_HELMET_BUY_BODY",
            () => PlayerPrefs.GetInt(PP_DONE, 0) == 1);

        // DONE — point at the way home, and let them leave whenever.
        TutorialSpotlight.Retarget(RectOf(_shop.exitButton),
            LocalizationManager.Tr("STEP_HELMET_DONE_TITLE"),
            LocalizationManager.Tr("STEP_HELMET_DONE_BODY"));

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_QuestComplete);

        // The last spotlight is a suggestion, not a cage: it holds briefly and
        // lets go, because trapping the player behind a dim layer they have
        // already satisfied is how a tutorial outstays its welcome.
        float hold = 0f;
        while (hold < 6f && PlayerPrefs.GetInt(PP_ACTIVE, 0) == 1)
        {
            hold += Time.unscaledDeltaTime;
            yield return null;
        }
        TutorialSpotlight.Hide();
        Destroy(gameObject);
    }

    // One step: point at a control, wait for the world to reach a state.
    private IEnumerator Step(System.Func<RectTransform> target, string titleKey, string bodyKey,
                             System.Func<bool> completed)
    {
        if (completed())
        {
            // Already true when we arrived. Skipping in silence is right — a
            // step that congratulates you for something you did before it asked
            // reads as the game not paying attention.
            yield break;
        }

        TutorialSpotlight.Retarget(target(), LocalizationManager.Tr(titleKey), LocalizationManager.Tr(bodyKey));

        // Re-resolved every frame: the list is built after the category opens,
        // so the rect for step 2 does not exist when step 2 begins.
        float guard = 0f;
        while (!completed())
        {
            guard += Time.unscaledDeltaTime;
            // A step that can never complete must not hold the screen forever.
            // The shop is a scene the player cannot walk out of, so a wedged
            // tutorial here is a soft lock.
            if (guard > 180f)
            {
                Debug.LogWarning($"[ShopTutorial] Step '{titleKey}' never completed after three minutes — " +
                                 "releasing the player rather than holding the shop hostage.");
                TutorialSpotlight.Hide();
                Destroy(gameObject);
                yield break;
            }
            var rt = target();
            if (rt != null) TutorialSpotlight.Retarget(rt, LocalizationManager.Tr(titleKey), LocalizationManager.Tr(bodyKey));
            yield return null;
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Click);
    }

    private static RectTransform RectOf(Button b) => b != null ? b.transform as RectTransform : null;

    private RectTransform FirstItemRect()
    {
        if (_shop == null || _shop.itemListContent == null) return null;
        foreach (Transform child in _shop.itemListContent)
            if (child != null && child.gameObject.activeInHierarchy) return child as RectTransform;
        return null;
    }
}
