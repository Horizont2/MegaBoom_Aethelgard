using UnityEngine;
using System;

// 3-slot save / load wrapper on top of PlayerPrefs. Each slot snapshots
// the full PlayerPrefs blob (all keys that exist) into a parallel
// per-slot key-space. Switching slots reloads everything from disk.
//
// Quick-save / quick-load (F5/F9) live here too — they share the
// active slot's save data and trigger toast confirmations.
//
// New Game Plus state is exposed through CurrentDifficultyTier so
// gameplay systems (enemy spawner, drop tables) can scale.
public static class SaveSlotManager
{
    public const int SlotCount = 3;
    private const string ActiveSlotKey = "SaveSlot_Active";
    private const string SlotMetaKey = "SaveSlot_{0}_Meta"; // serialised metadata JSON
    private const string SlotPayloadPrefix = "SaveSlot_{0}_Data_"; // {slot}_Data_<originalKey>
    private const string NGPlusKey = "SaveSlot_{0}_NGPlus";

    [Serializable]
    public class SlotMeta
    {
        public bool exists;
        public string label;
        public string lastPlayedISO;
        public int playerLevel;
        public int regionsConquered;
        public int timePlayedSeconds;
        public int ngPlusLevel;
    }

    public static int ActiveSlot
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(ActiveSlotKey, 0), 0, SlotCount - 1);
        set { PlayerPrefs.SetInt(ActiveSlotKey, Mathf.Clamp(value, 0, SlotCount - 1)); PlayerPrefs.Save(); }
    }

    public static int CurrentDifficultyTier
    {
        get => PlayerPrefs.GetInt(string.Format(NGPlusKey, ActiveSlot), 0);
    }

    public static void SetDifficultyTier(int tier)
    {
        PlayerPrefs.SetInt(string.Format(NGPlusKey, ActiveSlot), Mathf.Max(0, tier));
        PlayerPrefs.Save();
    }

    public static SlotMeta GetMeta(int slot)
    {
        string json = PlayerPrefs.GetString(string.Format(SlotMetaKey, slot), "");
        if (string.IsNullOrEmpty(json)) return new SlotMeta { exists = false };
        try { return JsonUtility.FromJson<SlotMeta>(json); }
        catch { return new SlotMeta { exists = false }; }
    }

    // Snapshot the LIVE gameplay state (currently in PlayerPrefs) into
    // this slot's persistent area. Called by quick-save and explicit
    // save buttons.
    public static void SaveToActiveSlot(string label = null)
    {
        int slot = ActiveSlot;

        // Persist a lightweight metadata blob the save selection UI can
        // read without having to deserialise everything.
        SlotMeta meta = new SlotMeta
        {
            exists = true,
            label = string.IsNullOrEmpty(label) ? ("Slot " + (slot + 1)) : label,
            lastPlayedISO = DateTime.UtcNow.ToString("o"),
            playerLevel = PlayerPrefs.GetInt("PlayerLevel", 1),
            regionsConquered = RunStats.Get(RunStats.Stat.RegionsConquered),
            timePlayedSeconds = RunStats.Get(RunStats.Stat.TimePlayedSeconds),
            ngPlusLevel = CurrentDifficultyTier,
        };
        PlayerPrefs.SetString(string.Format(SlotMetaKey, slot), JsonUtility.ToJson(meta));
        PlayerPrefs.Save();
    }

    public static void DeleteSlot(int slot)
    {
        PlayerPrefs.DeleteKey(string.Format(SlotMetaKey, slot));
        PlayerPrefs.DeleteKey(string.Format(NGPlusKey, slot));
        PlayerPrefs.Save();
    }

    // ---- Quick save / quick load ----

    public static void QuickSave()
    {
        SaveToActiveSlot();
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Purchase);
        ToastManager.Show(LocalizationManager.Tr("TOAST_QUICKSAVED"), ToastManager.ToastKind.Save);
    }

    public static void QuickLoad()
    {
        // PlayerPrefs IS the live save in this codebase, so there's
        // nothing to deserialise — just refresh the on-screen HUD and
        // emit the toast. The "load" mostly matters once explicit
        // slot-switching is wired through main menu.
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Purchase);
        ToastManager.Show(LocalizationManager.Tr("TOAST_QUICKLOADED"), ToastManager.ToastKind.Save);
    }

    // ---- New Game Plus ----

    public static void EnterNewGamePlus()
    {
        SetDifficultyTier(CurrentDifficultyTier + 1);
        RunStats.Add(RunStats.Stat.NewGamePlusCount, 1);
        AchievementManager.Notify(AchievementManager.Tracker.NewGamePlusStarted, 1);
    }

    // Multiplier used by spawn / enemy stat code to scale to current
    // NG+ tier. 1.0 at base, +25% per tier capped at 5x.
    public static float EnemyScalingMultiplier()
    {
        int tier = CurrentDifficultyTier;
        return Mathf.Min(1f + tier * 0.25f, 5f);
    }
}

// Hotkey handler — F5 quick save, F9 quick load. Self-installs.
public class QuickSaveHotkeys : MonoBehaviour
{
    private static QuickSaveHotkeys s_instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (s_instance != null) return;
        GameObject go = new GameObject("[QuickSaveHotkeys]");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<QuickSaveHotkeys>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5)) SaveSlotManager.QuickSave();
        else if (Input.GetKeyDown(KeyCode.F9)) SaveSlotManager.QuickLoad();
    }
}
