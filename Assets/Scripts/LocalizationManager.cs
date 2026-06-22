using UnityEngine;
using System.Collections.Generic;

// Lightweight localisation pipeline. Hardcoded English + Ukrainian
// strings live in a static dictionary so new languages can be added
// without an asset pipeline; the public API is a plain Tr(key)
// lookup that gracefully falls through to the key itself when an
// entry is missing.
//
// Usage in code:
//     toastText.text = LocalizationManager.Tr("MISSION_COMPLETE");
//     dialogueLabel.text = LocalizationManager.Tr("DIALOGUE_INTRO", "Aethelgard");
//
// The selected language persists via PlayerPrefs (Settings_Language)
// and a change broadcasts OnLanguageChanged so UI can re-pull strings.
public static class LocalizationManager
{
    public enum Lang { English, Ukrainian }

    public static event System.Action OnLanguageChanged;

    private static Lang s_lang = Lang.English;
    private static bool s_loaded;

    private static readonly Dictionary<string, string> s_en = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> s_uk = new Dictionary<string, string>();

    public static Lang CurrentLanguage
    {
        get { EnsureLoaded(); return s_lang; }
        set
        {
            EnsureLoaded();
            if (s_lang == value) return;
            s_lang = value;
            PlayerPrefs.SetInt("Settings_Language", (int)value);
            PlayerPrefs.Save();
            OnLanguageChanged?.Invoke();
        }
    }

    public static string Tr(string key)
    {
        EnsureLoaded();
        Dictionary<string, string> active = s_lang == Lang.Ukrainian ? s_uk : s_en;
        if (active.TryGetValue(key, out string v)) return v;
        // English fallback if Ukrainian entry missing.
        if (s_lang == Lang.Ukrainian && s_en.TryGetValue(key, out v)) return v;
        return key; // last resort — surface the key so missing strings are obvious
    }

    public static string Tr(string key, params object[] args)
    {
        string template = Tr(key);
        return args == null || args.Length == 0 ? template : string.Format(template, args);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        s_loaded = false;
    }

    private static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;
        s_lang = (Lang)PlayerPrefs.GetInt("Settings_Language", 0);
        Seed();
    }

    private static void Seed()
    {
        // === Core UI ===
        Add("UI_SAVE_AND_CLOSE",       "SAVE & CLOSE",                "ЗБЕРЕГТИ І ЗАКРИТИ");
        Add("UI_CLOSE",                "CLOSE",                       "ЗАКРИТИ");
        Add("UI_CONFIRM",              "CONFIRM",                     "ПІДТВЕРДИТИ");
        Add("UI_CANCEL",               "CANCEL",                      "СКАСУВАТИ");
        Add("UI_RESUME",               "RESUME",                      "ПРОДОВЖИТИ");
        Add("UI_QUIT_TO_CAMP",         "QUIT TO CAMP",                "ВИЙТИ В ТАБІР");

        // === Settings ===
        Add("SETTINGS_TITLE",          "SETTINGS",                    "НАЛАШТУВАННЯ");
        Add("SETTINGS_TAB_AUDIO",      "AUDIO",                       "ЗВУК");
        Add("SETTINGS_TAB_GRAPHICS",   "GRAPHICS",                    "ГРАФІКА");
        Add("SETTINGS_TAB_GAMEPLAY",   "GAMEPLAY",                    "ГРА");
        Add("SETTINGS_TAB_CONTROLS",   "CONTROLS",                    "КЕРУВАННЯ");
        Add("SETTINGS_TAB_LANG",       "LANGUAGE",                    "МОВА");
        Add("SETTINGS_MASTER_VOLUME",  "MASTER VOLUME",               "ЗАГАЛЬНА ГУЧНІСТЬ");
        Add("SETTINGS_MUSIC_VOLUME",   "MUSIC",                       "МУЗИКА");
        Add("SETTINGS_SFX_VOLUME",     "SOUND EFFECTS",               "ЕФЕКТИ");
        Add("SETTINGS_SENSITIVITY",    "MOUSE SENSITIVITY",           "ЧУТЛИВІСТЬ МИШІ");
        Add("SETTINGS_SUBTITLES",      "SUBTITLES",                   "СУБТИТРИ");
        Add("SETTINGS_SUBTITLE_SIZE",  "SUBTITLE SIZE",               "РОЗМІР СУБТИТРІВ");
        Add("SETTINGS_SCREEN_SHAKE",   "SCREEN SHAKE",                "ТРЯСКА ЕКРАНУ");
        Add("SETTINGS_DMG_POPUPS",     "DAMAGE NUMBERS",              "ЦИФРИ УРОНУ");
        Add("SETTINGS_LIMIT_FPS",      "LIMIT FPS TO 60",             "ОБМЕЖИТИ FPS ДО 60");
        Add("SETTINGS_SHOW_FPS",       "SHOW FPS COUNTER",            "ПОКАЗАТИ ЛІЧИЛЬНИК FPS");
        Add("SETTINGS_COLORBLIND",     "COLORBLIND MODE",             "РЕЖИМ ДАЛЬТОНІКА");
        Add("SETTINGS_QUALITY",        "GRAPHICS QUALITY",            "ЯКІСТЬ ГРАФІКИ");
        Add("SETTINGS_QUALITY_LOW",    "Low",                         "Низька");
        Add("SETTINGS_QUALITY_MED",    "Medium",                      "Середня");
        Add("SETTINGS_QUALITY_HIGH",   "High",                        "Висока");
        Add("SETTINGS_QUALITY_ULTRA",  "Ultra",                       "Ультра");

        // === Notifications ===
        Add("TOAST_MISSION_DONE",      "Mission Complete: {0}",       "Завдання виконано: {0}");
        Add("TOAST_REGION_CLEARED",    "Region Conquered: {0}",       "Регіон захоплено: {0}");
        Add("TOAST_LEVEL_UP",          "Level {0}",                   "Рівень {0}");
        Add("TOAST_ACHIEVEMENT",       "Achievement Unlocked: {0}",   "Досягнення: {0}");
        Add("TOAST_QUICKSAVED",        "Game Saved",                  "Гру збережено");
        Add("TOAST_QUICKLOADED",       "Save Restored",               "Збереження відновлено");
        Add("TOAST_LORE_FOUND",        "New Lore Entry: {0}",         "Новий запис у літописі: {0}");

        // === Lore codex entries ===
        SeedLore();

        // === Achievements (names only — descriptions in AchievementManager) ===
        Add("ACH_FIRST_BLOOD",         "First Blood",                 "Перша Кров");
        Add("ACH_FIRST_REGION",        "Conqueror",                   "Завойовник");
        Add("ACH_FIVE_REGIONS",        "Reclaimer",                   "Відновитель");
        Add("ACH_ALL_REGIONS",         "King of Aethelgard",          "Король Етельгарду");
        Add("ACH_LEVEL_10",            "Veteran",                     "Ветеран");
        Add("ACH_LEVEL_25",            "Hero of the Realm",           "Герой Королівства");
        Add("ACH_BOSS_SLAIN",          "Bonebreaker",                 "Костолам");
        Add("ACH_SCROLLS_5",           "Loremaster",                  "Хранитель Знань");
        Add("ACH_SCROLLS_ALL",         "Chronicler of Aethelgard",    "Літописець Етельгарду");
        Add("ACH_PERFECT_DODGE_10",    "Wind-Touched",                "Тінь Вітру");
        Add("ACH_DIAMOND_HOARDER",     "Hoarder's Gaze",              "Скарбничий");
        Add("ACH_NG_PLUS",             "Eternal Return",              "Вічне Повернення");
    }

    private static void SeedLore()
    {
        // Lore entry: NAME + BODY. Each has a unique key.
        AddLore("LORE_AETHELGARD_FALL",
            "The Fall of Aethelgard",
            "Падіння Етельгарду",
            "When the Pale King first marched south, our chronicles ended mid-sentence. " +
            "What remains is bone and ash — and the watchfires we light to remember that the kingdom was, " +
            "once, a place where children could sleep without dreaming of teeth.",
            "Коли Блідий Король вперше рушив на південь, наші літописи обірвалися на півслові. " +
            "Залишилися лише кістки та попіл — і сторожові вогні, які ми запалюємо, щоб пам'ятати: " +
            "королівство колись було місцем, де діти могли спати, не бачачи у снах ікла.");

        AddLore("LORE_AETHER_SHARDS",
            "The Aether Shards",
            "Аетерові Уламки",
            "They are not crystals. They are the cooled grief of stars, fallen during the long siege. " +
            "Carry too many and you will hear the song they sang at the moment of their making — " +
            "and it is the song of something that should not have been born.",
            "Це не кристали. Це застиглий смуток зірок, які впали під час довгої облоги. " +
            "Носи їх забагато — і почуєш пісню, яку вони співали в момент свого створення. " +
            "А це пісня того, що не мало б народитися.");

        AddLore("LORE_BONE_TIDE",
            "Of the Bone Tide",
            "Про Кістяний Приплив",
            "The dead do not rise. They are RAISED. Some lord still stands in some keep beyond the mist " +
            "and orders them up like a man calling cattle to a slaughterhouse. " +
            "Find the keep. Find the lord. End the order.",
            "Мертві не повстають. Їх ПІДНІМАЮТЬ. Якийсь володар досі стоїть у якійсь твердині за туманом " +
            "і піднімає їх, як людина гонить худобу на бійню. " +
            "Знайди твердиню. Знайди володаря. Зупини наказ.");

        AddLore("LORE_TOTEMS",
            "On the Purifying Totems",
            "Про Очисні Тотеми",
            "The first Watcher carved them from the hearthstones of villages that had already fallen. " +
            "Each totem holds a single name. When you strike it true, the name is freed, " +
            "and one soul that was bound to the Pale King's army walks at last into the long sleep.",
            "Перший Сторож вирізав їх із вогнищ сіл, які вже впали. " +
            "Кожен тотем тримає одне ім'я. Коли б'єш точно — ім'я звільняється, " +
            "і одна душа, прив'язана до армії Блідого Короля, нарешті входить у довгий сон.");

        AddLore("LORE_THE_FORGE",
            "The Forge at the Camp",
            "Кузня в Таборі",
            "The forge-mother does not speak. She lost her tongue at the Sundering of Old Vael and " +
            "would not take a new one when offered. She works the bellows in three-beat rhythm: " +
            "in. out. silence. The silence is for the names of her sons.",
            "Кузнечиха не говорить. Вона втратила язик під час Розриву Старого Велю " +
            "і відмовилася від нового, коли їй пропонували. Вона роздуває міхи в три удари: " +
            "вдих. видих. тиша. Тиша — за іменами її синів.");

        AddLore("LORE_NIGHT_BUFF",
            "Why the Dead are Stronger at Night",
            "Чому Мертві Сильніші Вночі",
            "Sunlight is a small justice that comes back every morning. The dead remember justice; " +
            "they are ashamed of it. They wait until the world is fair to no one, " +
            "and then they take what they always wanted: another chance to feed.",
            "Сонячне світло — мала справедливість, що повертається щоранку. Мертві пам'ятають справедливість; " +
            "вона їм соромна. Вони чекають, доки світ стане несправедливим до всіх, " +
            "а тоді беруть те, чого завжди прагнули: ще один шанс наїстися.");

        AddLore("LORE_THE_PALE_KING",
            "Fragment: The Pale King",
            "Уривок: Блідий Король",
            "...and they say he was once a knight of our own banner, struck down at the gates of " +
            "Aethelgard and woken by something that crawled out of the moat. " +
            "If true, then somewhere under that crown is a man who still flinches at his own name.",
            "...і кажуть, він колись був лицарем нашого прапора, повалений біля воріт " +
            "Етельгарду і пробуджений тим, що виповзло з рову. " +
            "Якщо це правда, то десь під тією короною є людина, яка все ще здригається на власне ім'я.");

        AddLore("LORE_STRANGER",
            "The Stranger at the Cart",
            "Незнайомець біля Воза",
            "He says his cart broke. He says the wood will fix it. He says many things. " +
            "But when the second wave came he did not run, and his eyes did not move from yours. " +
            "Whoever sent him here is keeping a ledger.",
            "Він каже, що його віз зламався. Каже, що дерево все полагодить. Він багато чого каже. " +
            "Але коли прийшла друга хвиля — він не побіг, і його очі не відірвалися від твоїх. " +
            "Хто б його сюди не послав — той веде облік.");
    }

    private static void Add(string key, string en, string uk)
    {
        s_en[key] = en;
        s_uk[key] = uk;
    }

    private static void AddLore(string key, string titleEn, string titleUk, string bodyEn, string bodyUk)
    {
        s_en[key + "_TITLE"] = titleEn;
        s_uk[key + "_TITLE"] = titleUk;
        s_en[key + "_BODY"] = bodyEn;
        s_uk[key + "_BODY"] = bodyUk;
    }
}
