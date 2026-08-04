using UnityEngine;
using System.Collections.Generic;

// Клас-маркер. Якщо повісити його на текст, AutoLocalize не буде його чіпати
public class LocalizedText : MonoBehaviour { }

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
    // Order MUST match the LANGUAGE dropdown in SettingsPanelAAABuilder:
    //   English / Українська / Русский / Español / Deutsch / Français / Polski
    public enum Lang { English, Ukrainian, Russian, Spanish, German, French, Polish }

    public static event System.Action OnLanguageChanged;

    private static Lang s_lang = Lang.English;
    private static bool s_loaded;

    private static readonly Dictionary<string, string> s_en = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> s_uk = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> s_ru = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> s_es = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> s_de = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> s_fr = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> s_pl = new Dictionary<string, string>();

    // int for dropdown parity. 0=EN, 1=UK, 2=RU, 3=ES, 4=DE, 5=FR, 6=PL.
    // Highest shipped language INDEX (0 = English, 1 = Ukrainian,
    // 2 = Russian, …). Bump this once RU/ES/DE/FR/PL coverage is
    // complete — until then the setter clamps here so half-translated
    // languages can't be selected via any dropdown / hotkey / save.
    public const int MAX_SHIPPED_LANGUAGE = 1;

    public static int CurrentLanguage
    {
        get { EnsureLoaded(); return (int)s_lang; }
        set
        {
            EnsureLoaded();
            Lang newLang = (Lang)Mathf.Clamp(value, 0, MAX_SHIPPED_LANGUAGE);
            if (s_lang == newLang) return;
            s_lang = newLang;
            PlayerPrefs.SetInt("Settings_Language", (int)s_lang);
            PlayerPrefs.Save();
            OnLanguageChanged?.Invoke();
        }
    }

    public static string Tr(string key)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(key)) return key;
        Dictionary<string, string> active = ActiveDictionary();
        if (active.TryGetValue(key, out string v)) return v;
        // Always fall through to English if the active locale is missing
        // the entry — keeps unlocalised strings readable instead of
        // surfacing the raw key in the UI.
        if (s_lang != Lang.English && s_en.TryGetValue(key, out v)) return v;
        // Compositional fallback for armor names like
        //   "Abyssal Chestplate", "Knight Boots (Elite)",
        //   "Barbarian's Officer Axe"
        // — decompose into set / piece / variant and translate each part
        // separately. Saves registering 108 armor combinations one-by-one.
        // Only runs when the key genuinely wasn't a direct hit AND the
        // active locale actually differs from English (no work in English).
        if (s_lang != Lang.English)
        {
            string composed = TryComposeTranslation(key, active);
            if (composed != null) return composed;
        }
        return key; // last resort — key acts as the literal string
    }

    // Split "X Y (Z)" into pieces, translate whichever ones we have, and
    // rebuild. Returns null if NOTHING could be translated — the caller
    // then falls through to the English literal.
    private static string TryComposeTranslation(string raw, Dictionary<string, string> active)
    {
        // Peel a trailing " (Variant)" first — the variant tag is a common
        // suffix on armor names.
        string variant = null;
        string body = raw;
        int parenIdx = body.LastIndexOf(" (");
        if (parenIdx > 0 && body.EndsWith(")"))
        {
            variant = body.Substring(parenIdx + 1); // includes the ()
            body = body.Substring(0, parenIdx);
        }

        // Body is "Word Word Word …" — translate every whitespace-
        // separated token individually. If ANY token has no translation
        // in the active dictionary and no fallback in English, bail out
        // for this token (keep original word).
        string[] words = body.Split(' ');
        bool anyTranslated = false;
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            if (string.IsNullOrEmpty(w)) continue;
            if (active.TryGetValue(w, out string t))
            {
                words[i] = t;
                anyTranslated = true;
            }
        }

        string variantOut = null;
        if (variant != null)
        {
            if (active.TryGetValue(variant, out string vt)) { variantOut = vt; anyTranslated = true; }
            else variantOut = variant;
        }

        if (!anyTranslated) return null; // nothing to give — let caller fall back
        string joined = string.Join(" ", words);
        return variantOut != null ? joined + " " + variantOut : joined;
    }

    private static Dictionary<string, string> ActiveDictionary()
    {
        switch (s_lang)
        {
            case Lang.Ukrainian: return s_uk;
            case Lang.Russian:   return s_ru;
            case Lang.Spanish:   return s_es;
            case Lang.German:    return s_de;
            case Lang.French:    return s_fr;
            case Lang.Polish:    return s_pl;
            default:             return s_en;
        }
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
        Add("UI_SAVE_AND_CLOSE", "SAVE & CLOSE", "ЗБЕРЕГТИ І ЗАКРИТИ");
        Add("UI_CLOSE", "CLOSE", "ЗАКРИТИ");
        Add("UI_CONFIRM", "CONFIRM", "ПІДТВЕРДИТИ");
        Add("UI_CANCEL", "CANCEL", "СКАСУВАТИ");
        Add("UI_RESUME", "RESUME", "ПРОДОВЖИТИ");
        Add("UI_QUIT_TO_CAMP", "QUIT TO CAMP", "ВИЙТИ В ТАБІР");

        // === Settings ===
        Add("SETTINGS_TITLE", "SETTINGS", "НАЛАШТУВАННЯ");
        Add("SETTINGS_TAB_AUDIO", "AUDIO", "ЗВУК");
        Add("SETTINGS_TAB_GRAPHICS", "GRAPHICS", "ГРАФІКА");
        Add("SETTINGS_TAB_GAMEPLAY", "GAMEPLAY", "ГРА");
        Add("SETTINGS_TAB_CONTROLS", "CONTROLS", "КЕРУВАННЯ");
        Add("SETTINGS_TAB_LANG", "LANGUAGE", "МОВА");
        Add("SETTINGS_MASTER_VOLUME", "MASTER VOLUME", "ЗАГАЛЬНА ГУЧНІСТЬ");
        Add("SETTINGS_MUSIC_VOLUME", "MUSIC", "МУЗИКА");
        Add("SETTINGS_SFX_VOLUME", "SOUND EFFECTS", "ЕФЕКТИ");
        Add("SETTINGS_SENSITIVITY", "MOUSE SENSITIVITY", "ЧУТЛИВІСТЬ МИШІ");
        Add("SETTINGS_SUBTITLES", "SUBTITLES", "СУБТИТРИ");
        Add("SETTINGS_SUBTITLE_SIZE", "SUBTITLE SIZE", "РОЗМІР СУБТИТРІВ");
        Add("SETTINGS_SCREEN_SHAKE", "SCREEN SHAKE", "ТРЯСКА ЕКРАНУ");
        Add("SETTINGS_DMG_POPUPS", "DAMAGE NUMBERS", "ЦИФРИ УРОНУ");
        Add("SETTINGS_LIMIT_FPS", "LIMIT FPS TO 60", "ОБМЕЖИТИ FPS ДО 60");
        Add("SETTINGS_SHOW_FPS", "SHOW FPS COUNTER", "ПОКАЗАТИ ЛІЧИЛЬНИК FPS");
        Add("SETTINGS_COLORBLIND", "COLORBLIND MODE", "РЕЖИМ ДАЛЬТОНІКА");
        Add("SETTINGS_QUALITY", "GRAPHICS QUALITY", "ЯКІСТЬ ГРАФІКИ");
        Add("SETTINGS_QUALITY_LOW", "Low", "Низька");
        Add("SETTINGS_QUALITY_MED", "Medium", "Середня");
        Add("SETTINGS_QUALITY_HIGH", "High", "Висока");
        Add("SETTINGS_QUALITY_ULTRA", "Ultra", "Ультра");

        // === Notifications ===
        Add("TOAST_MISSION_DONE", "Mission Complete: {0}", "Завдання виконано: {0}");
        Add("TOAST_REGION_CLEARED", "Region Conquered: {0}", "Регіон захоплено: {0}");
        Add("TOAST_LEVEL_UP", "Level {0}", "Рівень {0}");
        Add("TOAST_ACHIEVEMENT", "Achievement Unlocked: {0}", "Досягнення: {0}");
        Add("TOAST_QUICKSAVED", "Game Saved", "Гру збережено");
        Add("TOAST_QUICKLOADED", "Save Restored", "Збереження відновлено");
        Add("TOAST_LORE_FOUND", "New Lore Entry: {0}", "Новий запис у літописі: {0}");

        // === Text-as-key entries for AutoLocalize ===
        // AutoLocalize uses the visible English text as the
        // localization key. Keep these in sync with what's shown in
        // scenes — missing entries fall through to the original.
        Add("PAUSED", "PAUSED", "ПАУЗА");
        Add("CONTINUE", "CONTINUE", "ПРОДОВЖИТИ");
        Add6("RESUME",            "RESUME",          "ПРОДОВЖИТИ",      "ПРОДОЛЖИТЬ",      "REANUDAR",         "FORTSETZEN",        "REPRENDRE");
        Add6("BACK TO MENU",      "BACK TO MENU",    "НА ГОЛОВНУ",      "В ГЛАВНОЕ МЕНЮ",  "VOLVER AL MENÚ",   "ZUM HAUPTMENÜ",     "MENU PRINCIPAL");
        Add6("SETTINGS",          "SETTINGS",        "НАЛАШТУВАННЯ",    "НАСТРОЙКИ",       "AJUSTES",          "EINSTELLUNGEN",     "PARAMÈTRES");
        Add6("BACK",              "BACK",            "НАЗАД",           "НАЗАД",           "ATRÁS",            "ZURÜCK",            "RETOUR");
        Add6("CLOSE",             "CLOSE",           "ЗАКРИТИ",         "ЗАКРЫТЬ",         "CERRAR",           "SCHLIESSEN",        "FERMER");
        Add6("APPLY",             "APPLY",           "ПРИЙНЯТИ",        "ПРИМЕНИТЬ",       "APLICAR",          "ÜBERNEHMEN",        "APPLIQUER");
        Add6("APPLY & CLOSE",     "APPLY & CLOSE",   "ПРИЙНЯТИ І ЗАКРИТИ","ПРИМЕНИТЬ И ЗАКРЫТЬ","APLICAR Y CERRAR","ÜBERNEHMEN U. SCHLIESSEN","APPLIQUER ET FERMER");
        Add6("RESET DEFAULTS",    "RESET DEFAULTS",  "СКИНУТИ ДО ЗАМОВЧУВАНЬ","СБРОСИТЬ ПО УМОЛЧАНИЮ","RESTABLECER","ZURÜCKSETZEN", "RÉINITIALISER");
        Add6("DISCARD",           "DISCARD",         "ВІДХИЛИТИ",       "ОТМЕНИТЬ",        "DESCARTAR",        "VERWERFEN",         "ANNULER");
        Add6("NEW GAME",          "NEW GAME",        "НОВА ГРА",        "НОВАЯ ИГРА",      "NUEVA PARTIDA",    "NEUES SPIEL",       "NOUVELLE PARTIE");
        Add6("LOAD GAME",         "LOAD GAME",       "ЗАВАНТАЖИТИ",     "ЗАГРУЗИТЬ",       "CARGAR PARTIDA",   "SPIEL LADEN",       "CHARGER");
        Add6("LOAD",              "LOAD",            "ЗАВАНТАЖИТИ",     "ЗАГРУЗИТЬ",       "CARGAR",           "LADEN",             "CHARGER");
        Add6("QUIT",              "QUIT",            "ВИЙТИ",           "ВЫХОД",           "SALIR",            "BEENDEN",           "QUITTER");
        Add6("QUIT TO DESKTOP",   "QUIT TO DESKTOP", "ВИЙТИ В ОС",      "ВЫЙТИ НА РАБОЧИЙ СТОЛ","SALIR AL ESCRITORIO","ZUM DESKTOP BEENDEN","QUITTER VERS BUREAU");
        Add6("EXIT",              "EXIT",            "ВИЙТИ",           "ВЫХОД",           "SALIR",            "BEENDEN",           "QUITTER");
        Add6("PLAY",              "PLAY",            "ГРАТИ",           "ИГРАТЬ",          "JUGAR",            "SPIELEN",           "JOUER");
        Add6("START",             "START",           "СТАРТ",           "СТАРТ",           "INICIAR",          "START",             "DÉMARRER");
        Add6("PAUSED",            "PAUSED",          "ПАУЗА",           "ПАУЗА",           "EN PAUSA",         "PAUSIERT",          "EN PAUSE");
        Add6("CONTINUE",          "CONTINUE",        "ПРОДОВЖИТИ",      "ПРОДОЛЖИТЬ",      "CONTINUAR",        "FORTSETZEN",        "CONTINUER");

        // Sidebar categories used by AAA settings panel — all 7 locales.
        Add7("GENERAL",       "GENERAL",       "ЗАГАЛЬНІ",     "ОБЩИЕ",       "GENERAL",     "ALLGEMEIN",   "GÉNÉRAL",        "OGÓLNE");
        Add7("GAMEPLAY",      "GAMEPLAY",      "ГРА",          "ИГРОВОЙ ПРОЦЕСС","JUEGO",    "SPIELABLAUF", "GAMEPLAY",       "ROZGRYWKA");
        Add7("AUDIO",         "AUDIO",         "ЗВУК",         "ЗВУК",        "AUDIO",       "AUDIO",       "AUDIO",          "DŹWIĘK");
        Add7("VIDEO",         "VIDEO",         "ВІДЕО",        "ВИДЕО",       "VIDEO",       "VIDEO",       "VIDÉO",          "WIDEO");
        Add7("GRAPHICS",      "GRAPHICS",      "ГРАФІКА",      "ГРАФИКА",     "GRÁFICOS",    "GRAFIK",      "GRAPHISMES",     "GRAFIKA");
        Add7("CONTROLS",      "CONTROLS",      "КЕРУВАННЯ",    "УПРАВЛЕНИЕ",  "CONTROLES",   "STEUERUNG",   "COMMANDES",      "STEROWANIE");
        Add7("ACCESSIBILITY", "ACCESSIBILITY", "ДОСТУПНІСТЬ",  "ДОСТУПНОСТЬ", "ACCESIBILIDAD","BARRIEREFREIHEIT","ACCESSIBILITÉ","DOSTĘPNOŚĆ");
        Add7("LANGUAGE",      "LANGUAGE",      "МОВА",         "ЯЗЫК",        "IDIOMA",      "SPRACHE",     "LANGUE",         "JĘZYK");

        // Section headers
        Add6("HUD",              "HUD",             "HUD",          "HUD",           "HUD",            "HUD",            "HUD");
        Add6("SAVE",             "SAVE",            "ЗБЕРЕЖЕННЯ",   "СОХРАНЕНИЕ",    "GUARDADO",       "SPEICHERN",      "SAUVEGARDE");
        Add6("MIX",              "MIX",             "МІКС",         "МИКС",          "MEZCLA",         "MIX",            "MIX");
        Add6("DISPLAY",          "DISPLAY",         "ДИСПЛЕЙ",      "ДИСПЛЕЙ",       "PANTALLA",       "ANZEIGE",        "AFFICHAGE");
        Add6("CAMERA",           "CAMERA",          "КАМЕРА",       "КАМЕРА",        "CÁMARA",         "KAMERA",         "CAMÉRA");
        Add6("QUALITY PRESET",   "QUALITY PRESET",  "ПРЕСЕТ ЯКОСТІ","ПРЕСЕТ КАЧЕСТВА","CALIDAD",        "QUALITÄTSPROFIL","PROFIL DE QUALITÉ");
        Add6("TIERS",            "TIERS",           "РІВНІ",        "УРОВНИ",        "NIVELES",        "STUFEN",         "NIVEAUX");
        Add6("POST-FX",          "POST-FX",         "ПОСТ-ЕФЕКТИ",  "ПОСТ-ЭФФЕКТЫ",  "POST-FX",        "POST-FX",        "POST-FX");
        Add6("MOUSE & KEYBOARD", "MOUSE & KEYBOARD","МИША ТА КЛАВІАТУРА","МЫШЬ И КЛАВИАТУРА","RATÓN Y TECLADO","MAUS UND TASTATUR","SOURIS ET CLAVIER");
        Add6("MOUSE & CAMERA",   "MOUSE & CAMERA",  "МИША ТА КАМЕРА","МЫШЬ И КАМЕРА", "RATÓN Y CÁMARA", "MAUS UND KAMERA","SOURIS ET CAMÉRA");
        Add6("GAMEPAD",          "GAMEPAD",         "ГЕЙМПАД",      "ГЕЙМПАД",       "MANDO",          "GAMEPAD",        "MANETTE");
        Add6("BINDINGS",         "BINDINGS",        "ПРИВ'ЯЗКИ",    "ПРИВЯЗКИ",      "ASIGNACIONES",   "BELEGUNG",       "TOUCHES");
        Add6("FEEDBACK",         "FEEDBACK",        "ВІДГУК",       "ОТКЛИК",        "RETROALIM.",     "FEEDBACK",       "RETOUR");
        Add6("DIFFICULTY",       "DIFFICULTY",      "СКЛАДНІСТЬ",   "СЛОЖНОСТЬ",     "DIFICULTAD",     "SCHWIERIGKEIT",  "DIFFICULTÉ");
        Add6("TUTORIAL",         "TUTORIAL",        "НАВЧАННЯ",     "ОБУЧЕНИЕ",      "TUTORIAL",       "TUTORIAL",       "TUTORIEL");
        Add6("BEHAVIOUR",        "BEHAVIOUR",       "ПОВЕДІНКА",    "ПОВЕДЕНИЕ",     "COMPORTAMIENTO", "VERHALTEN",      "COMPORTEMENT");
        Add6("VISUAL AIDS",      "VISUAL AIDS",     "ВІЗУАЛЬНА ДОПОМОГА","ВИЗУАЛЬНАЯ ПОМОЩЬ","AYUDA VISUAL","SEHHILFEN",  "AIDES VISUELLES");
        Add6("UI",               "UI",              "ІНТЕРФЕЙС",    "ИНТЕРФЕЙС",     "INTERFAZ",       "BENUTZEROBERFLÄCHE","INTERFACE");
        Add6("TEXT",             "TEXT",            "ТЕКСТ",        "ТЕКСТ",         "TEXTO",          "TEXT",           "TEXTE");
        Add6("SUBTITLES",        "SUBTITLES",       "СУБТИТРИ",     "СУБТИТРЫ",      "SUBTÍTULOS",     "UNTERTITEL",     "SOUS-TITRES");
        Add6("PREVIEW",          "PREVIEW",         "ПРЕВ'Ю",       "ПРЕДПРОСМОТР",  "VISTA PREVIA",   "VORSCHAU",       "APERÇU");
        Add6("DESCRIPTION",      "DESCRIPTION",     "ОПИС",         "ОПИСАНИЕ",      "DESCRIPCIÓN",    "BESCHREIBUNG",   "DESCRIPTION");
        // Toggles & rows in settings panel
        Add6("Show FPS",     "Show FPS",     "Показати FPS",     "Показывать FPS",  "Mostrar FPS",   "FPS anzeigen",     "Afficher FPS");
        Add6("Limit FPS",    "Limit FPS",    "Обмежити FPS",     "Ограничить FPS",  "Limitar FPS",   "FPS begrenzen",    "Limiter FPS");
        Add6("Auto-Save",    "Auto-Save",    "Авто-збереження",  "Автосохранение",  "Auto-guardar",  "Autospeichern",    "Sauv. auto");
        Add6("Damage Popups","Damage Popups","Цифри урону",      "Цифры урона",     "Cifras de daño","Schadenszahlen",   "Chiffres dégâts");
        Add6("Screen Shake", "Screen Shake", "Тряска екрану",    "Тряска экрана",   "Vibrar pantalla","Bildschirmrütteln","Tremblement écran");
        Add6("Hit-Stop FX",  "Hit-Stop FX",  "Заморозка на ударі","Заморозка при ударе","Pausa de golpe","Trefferstopp",  "Pause d'impact");
        Add6("Low HP Vignette","Low HP Vignette","Червона рамка при низькому HP","Виньетка при низком HP","Viñeta de HP bajo","Vignette bei niedrigem HP","Vignette PV faible");
        Add6("Tutorial Hints","Tutorial Hints","Підказки",       "Подсказки",       "Sugerencias",   "Tutorial-Tipps",   "Astuces du tuto");
        Add6("Master",       "Master",       "Загальна",         "Общая",           "Maestro",       "Hauptlautstärke",  "Volume principal");
        Add6("Music",        "Music",        "Музика",           "Музыка",          "Música",        "Musik",            "Musique");
        Add6("Sound FX",     "Sound FX",     "Ефекти",           "Звуковые эффекты","Efectos",       "Soundeffekte",     "Effets sonores");
        Add6("Voice",        "Voice",        "Голос",            "Голос",           "Voz",           "Stimme",           "Voix");
        Add6("Ambient",      "Ambient",      "Атмосфера",        "Окружение",       "Ambiente",      "Ambiente",         "Ambiance");
        Add6("Mute When Unfocused","Mute When Unfocused","Глушити коли вікно неактивне","Глушить при сворачивании","Silenciar al perder foco","Stumm bei Fokusverlust","Couper si fenêtre inactive");
        Add6("Resolution",   "Resolution",   "Роздільна здатність","Разрешение",    "Resolución",    "Auflösung",        "Résolution");
        Add6("Window Mode",  "Window Mode",  "Режим вікна",      "Режим окна",      "Modo de ventana","Fenstermodus",    "Mode fenêtre");
        Add6("Refresh Rate", "Refresh Rate", "Частота оновлення","Частота обновления","Tasa de refresco","Bildwiederholrate","Taux rafraîchiss.");
        Add6("Monitor",      "Monitor",      "Монітор",          "Монитор",         "Monitor",       "Monitor",          "Moniteur");
        Add6("FPS Cap",      "FPS Cap",      "Ліміт FPS",        "Лимит FPS",       "Límite FPS",    "FPS-Limit",        "Limite FPS");
        Add6("V-Sync",       "V-Sync",       "Вертикальна синхр.","Верт. синхрон.", "Sincr. vert.",  "VSync",            "Synchro vert.");
        Add6("Field of View","Field of View","Поле зору",        "Поле обзора",     "Campo de visión","Sichtfeld",       "Champ de vision");
        Add6("Brightness",   "Brightness",   "Яскравість",       "Яркость",         "Brillo",        "Helligkeit",       "Luminosité");
        Add6("Gamma",        "Gamma",        "Гамма",            "Гамма",           "Gamma",         "Gamma",            "Gamma");
        Add("Preset", "Preset", "Пресет");
        Add("Render Scale (%)", "Render Scale (%)", "Рендер-масштаб (%)");
        Add("Anti-Aliasing", "Anti-Aliasing", "Згладжування");
        Add("Texture Quality", "Texture Quality", "Якість текстур");
        Add("Shadow Quality", "Shadow Quality", "Якість тіней");
        Add("Shadow Distance", "Shadow Distance", "Дистанція тіней");
        Add("Post Processing", "Post Processing", "Постобробка");
        Add("Dynamic Shadows", "Dynamic Shadows", "Динамічні тіні");
        Add("Motion Blur", "Motion Blur", "Розмиття руху");
        Add("Depth of Field", "Depth of Field", "Глибина різкості");
        Add("Bloom", "Bloom", "Свічення");
        Add("Ambient Occlusion", "Ambient Occlusion", "Затемнення");
        Add("Volumetric Lighting", "Volumetric Lighting", "Об'ємне світло");
        Add("Mouse Sensitivity", "Mouse Sensitivity", "Чутливість миші");
        Add("Invert Y Axis", "Invert Y Axis", "Інверсія Y");
        Add("Controller Vibration", "Controller Vibration", "Вібрація геймпада");
        Add("Aim Assist", "Aim Assist", "Прицілювання");
        Add("Subtitle Size", "Subtitle Size", "Розмір субтитрів");
        Add("Subtitle Background", "Subtitle Background", "Фон субтитрів");
        Add("Colorblind Mode", "Colorblind Mode", "Режим дальтоніка");
        Add("High Contrast UI", "High Contrast UI", "Високий контраст");
        Add("Reduce Motion", "Reduce Motion", "Менше анімацій");
        Add("Photosensitivity Safe Mode", "Photosensitivity Safe Mode", "Безпечний режим (фотосенсит.)");
        Add("UI Scale", "UI Scale", "Масштаб UI");
        Add("Game Language", "Game Language", "Мова гри");
        Add("Voice Language", "Voice Language", "Мова озвучення");
        Add("Hold to Sprint", "Hold to Sprint", "Утримувати спринт");
        Add("Custom key bindings coming soon.", "Custom key bindings coming soon.", "Власні клавіші — скоро.");
        Add("PREVIEW", "PREVIEW", "ПРЕВ'Ю");
        Add("DESCRIPTION", "DESCRIPTION", "ОПИС");
        Add("Mouse over any option to read what it does.",
                                       "Mouse over any option to read what it does.",
                                       "Наведи на будь-яку опцію, щоб прочитати опис.");
        // HUD prompts + section headers in gameplay
        Add("ENGINEERING MASTERY", "ENGINEERING MASTERY", "ІНЖЕНЕРНА МАЙСТЕРНІСТЬ");
        Add("WOOD", "WOOD", "ДЕРЕВО");
        Add("STONE", "STONE", "КАМІНЬ");
        Add("FOOD", "FOOD", "ЇЖА");
        Add("DIAMONDS", "DIAMONDS", "АЛМАЗИ");
        Add("BACKPACK", "BACKPACK", "РЮКЗАК");
        Add("HP", "HP", "HP");
        Add("STAMINA", "STAMINA", "ВИТРИВАЛІСТЬ");
        Add("LVL", "LVL", "РІВ");
        Add("PURIFY TOTEM", "PURIFY TOTEM", "ОЧИСТИТИ ТОТЕМ");
        Add("[E] Open Map", "[E] Open Map", "[E] Відкрити мапу");
        Add("SLAY THE OVERLORD!", "SLAY THE OVERLORD!", "ЗНИЩ ВЕЛИТЕНЯ!");
        Add("SURVIVE THE SWARM!", "SURVIVE THE SWARM!", "ВИЖИВИ ПІД НАТИСКОМ!");

        SeedFullLocale();

        // === Lore codex entries ===
        SeedLore();

        // === Achievements (names only — descriptions in AchievementManager) ===
        Add("ACH_FIRST_BLOOD", "First Blood", "Перша Кров");
        Add("ACH_FIRST_REGION", "Conqueror", "Завойовник");
        Add("ACH_FIVE_REGIONS", "Reclaimer", "Відновитель");
        Add("ACH_ALL_REGIONS", "King of Aethelgard", "Король Етельгарду");
        Add("ACH_LEVEL_10", "Veteran", "Ветеран");
        Add("ACH_LEVEL_25", "Hero of the Realm", "Герой Королівства");
        Add("ACH_BOSS_SLAIN", "Bonebreaker", "Костолам");
        Add("ACH_SCROLLS_5", "Loremaster", "Хранитель Знань");
        Add("ACH_SCROLLS_ALL", "Chronicler of Aethelgard", "Літописець Етельгарду");
        Add("ACH_PERFECT_DODGE_10", "Wind-Touched", "Тінь Вітру");
        Add("ACH_DIAMOND_HOARDER", "Hoarder's Gaze", "Скарбничий");
        Add("ACH_NG_PLUS", "Eternal Return", "Вічне Повернення");
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

        AddLore("LORE_FORGE_MOTHER",
            "The Forge-Mother's Pact",
            "Пакт Кузнечихи",
            "Three sons. Two she watched march out the gates of Old Vael. The third she sent " +
            "willing into the fire of her own forge, because the realm needed a sword that " +
            "would not break, and a mother's grief is the only quench-water that holds.",
            "Троє синів. Двох вона провела за ворота Старого Велю. Третього вона послала " +
            "власноруч у вогонь своєї кузні — бо королівству потрібен був меч, що не зламається, " +
            "а материнський смуток — єдиний гарт, що тримає.");

        AddLore("LORE_WATCHFIRE",
            "On the Watchfires",
            "Про Сторожові Вогні",
            "We light them not for warmth, nor to see by. We light them so that the dead know " +
            "where the line is. So that the dead remember: this far, no further. So that on " +
            "the longest night, the dead see the fire and remember they were once afraid of it.",
            "Ми запалюємо їх не для тепла, не для світла. Ми запалюємо їх, щоб мертві знали, " +
            "де проходить межа. Щоб мертві пам'ятали: до цієї межі — і не далі. Щоб найдовшої " +
            "ночі мертві бачили вогонь і пригадували, що колись його боялися.");

        AddLore("LORE_DEAD_RIVER",
            "Of the Dead River",
            "Про Мертву Ріку",
            "Below the high country there is a river that no fish swims and no horse will " +
            "drink from. Some say it is the boundary the Pale King set. Others say it is " +
            "where the kingdom's prayers go when they are not answered, and they sink, and " +
            "they wait for someone to listen.",
            "За високим краєм є ріка, в якій не плаває риба і з якої не питиме жоден кінь. " +
            "Кажуть, це межа, яку поставив Блідий Король. Інші кажуть — туди йдуть молитви " +
            "королівства, коли на них не відповідають, тонуть і чекають, поки хтось почує.");

        AddLore("LORE_BLOOD_OATH",
            "The Blood Oath at the Gates",
            "Кривава Присяга Біля Воріт",
            "Twelve hundred swore it. Eleven hundred and ninety-eight broke it within the year. " +
            "Two kept it. We do not know which two — only that the snow that fell on the gates " +
            "that winter did not melt in any spring since.",
            "Тисяча двісті присягнули. Тисяча сто дев'яносто вісім зламали присягу впродовж року. " +
            "Двоє стримали. Ми не знаємо, які двоє — лише те, що сніг, який тоді ліг на ворота, " +
            "не танув жодної з весен відтоді.");

        // === Mission flavor lines (shown as random tip text in loading screens) ===
        Add("TIP_LORE_1",
            "The aether shards sing when carried in odd numbers. Always carry even.",
            "Аетерові уламки співають, коли їх непарне число. Носи завжди парну кількість.");
        Add("TIP_LORE_2",
            "A grenade thrown into a Bone Tide will kill thirty things. A grenade thrown at the Pale King will kill one man, and that may be enough.",
            "Граната, кинута в Кістяний Приплив, вб'є тридцять. Граната, кинута в Блідого Короля, вб'є одного — і цього може вистачити.");
        Add("TIP_LORE_3",
            "The skeletons grow stronger at night because the dead remember being afraid of the dawn.",
            "Скелети сильнішають вночі, бо мертві пам'ятають, як боялися світанку.");
        Add("TIP_LORE_4",
            "A perfect dodge is not a step away from the strike. It is a step into the rhythm of the striker.",
            "Ідеальне ухилення — це не крок убік від удару. Це крок у ритм того, хто б'є.");
        Add("TIP_LORE_5",
            "The forge-mother does not take silver. She takes the names of those you have lost.",
            "Кузнечиха не бере срібла. Вона бере імена тих, кого ти втратив.");
        Add("TIP_LORE_6",
            "The totems are not weapons. They are receipts. Each one is a debt the Pale King owes us, paid in his servants' freedom.",
            "Тотеми — не зброя. Тотеми — розписки. Кожна — борг, який Блідий Король сплачує нам свободою своїх слуг.");
        Add("TIP_LORE_7",
            "If you hear a child laugh in the forest, do not look toward the sound. Look for the cart.",
            "Якщо почуєш у лісі дитячий сміх — не повертай голови на звук. Шукай очима віз.");
        Add("TIP_LORE_8",
            "Hold the line until dawn. There is always another dawn.",
            "Тримай межу до світанку. Світанок завжди приходить ще раз.");

        // === Region-specific lore plaques (shown on entering a region for the first time) ===
        Add("REGION_INTRO_FOREST",
            "Forest of Vael — once a hunting ground for kings, now feeding the dead.",
            "Велівський Ліс — колись мисливські угіддя королів, тепер — годівля мерців.");
        Add("REGION_INTRO_HIGHLANDS",
            "The Highlands of Aethelgard — where the Blood Oath was sworn, and broken.",
            "Високогір'я Етельгарду — де присяга була дана й зламана.");
        Add("REGION_INTRO_BONEFIELDS",
            "The Bonefields — where the Pale King's first wave fell. They never stopped getting back up.",
            "Кістяні Поля — де впала перша хвиля Блідого Короля. Вони так і не перестали підніматися.");
        Add("REGION_INTRO_FROSTGATE",
            "Frostgate — the last waystation before the dead lands. Light a fire here. You will need it.",
            "Льодова Брама — остання застава перед мертвими землями. Запали тут вогонь. Він тобі знадобиться.");
        Add("REGION_INTRO_DEEP",
            "The Deep Approach — the road to the Pale King's keep. The dead walk both directions.",
            "Глибинний Підступ — дорога до твердині Блідого Короля. Мертві ходять в обидва боки.");

        // === Stranger NPC dialogue (more lines to flesh out the camp) ===
        Add("DLG_STRANGER_1",
            "Stranger: You smell like the gates of Aethelgard. I knew them once.",
            "Незнайомець: Від тебе тхне воротами Етельгарду. Я знав їх колись.");
        Add("DLG_STRANGER_2",
            "Stranger: The aether shards aren't crystals, friend. They were stars. They fell when we ran out of prayers.",
            "Незнайомець: Уламки — не кристали, друже. Це були зірки. Вони впали, коли в нас закінчилися молитви.");
        Add("DLG_STRANGER_3",
            "Stranger: I'm just a cart-mender. Don't look at me like that. The cart really is broken.",
            "Незнайомець: Я просто латаю воза. Не дивися на мене так. Віз справді зламаний.");
        Add("DLG_STRANGER_4",
            "Stranger: There was a banner once. Black on gold. Burnt now. If you find a piece, bring it.",
            "Незнайомець: Колись був прапор. Чорне на золотому. Тепер згорів. Якщо знайдеш клапоть — принеси.");
        Add("DLG_STRANGER_5",
            "Stranger: Every man you kill out there used to be one of us. You're not wrong to do it. Just be the one who remembers.",
            "Незнайомець: Кожен, кого ти там вбиваєш, колись був нашим. Ти не помиляєшся. Просто будь тим, хто пам'ятає.");

        // === Forge-mother dialogue (silent — pantomime in text) ===
        Add("DLG_FORGE_MOTHER_1",
            "[The forge-mother nods at the anvil. The hammer is waiting for you.]",
            "[Кузнечиха киває на ковадло. Молот чекає тебе.]");
        Add("DLG_FORGE_MOTHER_2",
            "[She presses three fingers to her mouth, then to your shoulder. Her sons. Now you.]",
            "[Вона притискає три пальці до вуст, потім до твого плеча. Її сини. Тепер — ти.]");

        // === Region cleared lines (rotating flavour for the post-region screen) ===
        Add("REGION_CLEARED_1",
            "The watchfires of the realm burn a little brighter tonight.",
            "Сторожові вогні королівства цієї ночі горять трохи яскравіше.");
        Add("REGION_CLEARED_2",
            "One more name freed from the Pale King's ledger.",
            "Ще одне ім'я звільнене з реєстру Блідого Короля.");
        Add("REGION_CLEARED_3",
            "Aethelgard remembers what was taken. Aethelgard remembers what was returned.",
            "Етельгард пам'ятає, що було забрано. Етельгард пам'ятає, що було повернуто.");
    }

    private static void Add(string key, string en, string uk)
    {
        s_en[key] = en;
        s_uk[key] = uk;
    }

    // Shorthand for entries where the English string IS the lookup key.
    // Handy for wrapping call-site literals with Tr() without inventing
    // a separate MERC_* / PROMPT_* identifier.
    private static void AddSelf(string en, string uk)
    {
        s_en[en] = en;
        s_uk[en] = uk;
    }

    // Bulk pass that touches every visible string we ship in the AAA
    // settings panel — row labels, description copy, dropdown options,
    // section headers, and the right-rail PREVIEW / DESCRIPTION text.
    // Splits out of Seed so the table is easier to maintain. Every
    // entry here uses Add7 (all 7 locales) so language flips actually
    // re-render the panel.
    private static void SeedFullLocale()
    {
        // -- Common dropdown values --
        Add7("Low",     "Low",     "Низька",  "Низкое",  "Bajo",    "Niedrig", "Bas",     "Niskie");
        Add7("Medium",  "Medium",  "Середня", "Среднее", "Medio",   "Mittel",  "Moyen",   "Średnie");
        Add7("High",    "High",    "Висока",  "Высокое", "Alto",    "Hoch",    "Élevé",   "Wysokie");
        Add7("Ultra",   "Ultra",   "Ультра",  "Ультра",  "Ultra",   "Ultra",   "Ultra",   "Ultra");
        Add7("Custom",  "Custom",  "Власна",  "Кастом",  "Personal.","Eigene", "Perso.",  "Własne");
        Add7("Off",     "Off",     "Вимк.",   "Выкл.",   "Apagado", "Aus",     "Désactivé","Wył.");
        Add7("On",      "On",      "Увімк.",  "Вкл.",    "Encendido","An",     "Activé",  "Wł.");
        Add7("Easy",    "Easy",    "Легко",   "Легко",   "Fácil",   "Leicht",  "Facile",  "Łatwo");
        Add7("Normal",  "Normal",  "Норма",   "Нормально","Normal",  "Normal", "Normal",  "Normalnie");
        Add7("Hard",    "Hard",    "Складно", "Сложно",  "Difícil", "Schwer",  "Difficile","Trudno");
        Add7("Hardcore","Hardcore","Хардкор", "Хардкор", "Hardcore","Hardcore","Hardcore","Hardcore");
        Add7("Small",   "Small",   "Малий",   "Малый",   "Pequeño", "Klein",   "Petit",   "Mały");
        Add7("Large",   "Large",   "Великий", "Большой", "Grande",  "Groß",    "Grand",   "Duży");
        Add7("Fullscreen","Fullscreen","Повноекр.","Полноэкр.","Pant. completa","Vollbild","Plein écran","Pełny ekran");
        Add7("Borderless","Borderless","Без рамки","Без рамки","Sin bordes","Randlos","Sans bord","Bezramkowy");
        Add7("Windowed","Windowed","У вікні","Оконный","Ventana","Fenster","Fenêtré","Okno");
        Add7("FXAA",    "FXAA",    "FXAA",    "FXAA",    "FXAA",    "FXAA",    "FXAA",    "FXAA");
        Add7("SMAA",    "SMAA",    "SMAA",    "SMAA",    "SMAA",    "SMAA",    "SMAA",    "SMAA");
        Add7("TAA",     "TAA",     "TAA",     "TAA",     "TAA",     "TAA",     "TAA",     "TAA");
        Add7("Hard",    "Hard",    "Жорсткі", "Жесткие", "Duras",   "Hart",    "Dures",   "Twarde");
        Add7("Soft Low","Soft Low","М'які слабкі","Мягкие низкие","Suaves bajo","Weich niedrig","Doux faible","Miękkie niskie");
        Add7("Soft High","Soft High","М'які високі","Мягкие высокие","Suaves alto","Weich hoch","Doux élevé","Miękkie wysokie");
        Add7("Unlimited","Unlimited","Без ліміту","Без ограничения","Sin límite","Unbegrenzt","Illimité","Bez limitu");
        Add7("Primary",  "Primary",  "Основний","Основной","Principal","Primär","Principal","Główny");

        // -- More row labels --
        Add7("Texture Quality","Texture Quality","Якість текстур","Качество текстур","Calidad de texturas","Texturqualität","Qualité des textures","Jakość tekstur");
        Add7("Shadow Quality", "Shadow Quality", "Якість тіней",   "Качество теней",  "Calidad de sombras", "Schattenqualität",     "Qualité des ombres", "Jakość cieni");
        Add7("Shadow Distance","Shadow Distance","Дистанція тіней","Дальность теней","Distancia de sombras","Schattendistanz",   "Distance des ombres","Odległość cieni");
        Add7("Post Processing","Post Processing","Постобробка",    "Пост-обработка",  "Postprocesado",       "Nachbearbeitung",      "Post-traitement",    "Post-processing");
        Add7("Dynamic Shadows","Dynamic Shadows","Динамічні тіні", "Динамические тени","Sombras dinámicas",  "Dynamische Schatten",  "Ombres dynamiques",  "Dynamiczne cienie");
        Add7("Motion Blur",    "Motion Blur",    "Розмиття руху",  "Размытие в движении","Desenfoque mov.", "Bewegungsunschärfe",  "Flou cinétique",     "Rozmycie ruchu");
        Add7("Depth of Field", "Depth of Field", "Глибина різкості","Глубина резкости","Profundidad de campo","Tiefenschärfe",     "Profondeur de champ","Głębia ostrości");
        Add7("Bloom",          "Bloom",          "Свічення",       "Свечение",        "Bloom",               "Bloom",                "Bloom",              "Bloom");
        Add7("Ambient Occlusion","Ambient Occlusion","Затемнення", "Окружающее затемн.","Oclusión ambiental","Umgebungsverdeckung","Occlusion ambiante", "Ambient Occlusion");
        Add7("Volumetric Lighting","Volumetric Lighting","Об'ємне світло","Объёмное освещение","Iluminación volumétrica","Volumetrisches Licht","Éclairage volumétrique","Oświetlenie wolumetryczne");
        Add7("Mouse Sensitivity","Mouse Sensitivity","Чутливість миші","Чувствительность мыши","Sensibilidad ratón","Mausempfindlichkeit","Sensibilité souris","Czułość myszy");
        Add7("Invert Y Axis",  "Invert Y Axis",  "Інверсія Y",     "Инверсия Y",      "Invertir eje Y",      "Y-Achse invertieren",   "Inverser axe Y",     "Odwróć oś Y");
        Add7("Controller Vibration","Controller Vibration","Вібрація геймпада","Вибрация геймпада","Vibración del mando","Controller-Vibration","Vibration manette","Wibracje pada");
        Add7("Aim Assist",     "Aim Assist",     "Прицілювання",   "Помощь прицела",  "Asistencia de mira",  "Zielhilfe",            "Aide à la visée",    "Asystent celow.");
        Add7("Subtitle Size",  "Subtitle Size",  "Розмір субтитрів","Размер субтитров","Tamaño subtítulos",  "Untertitelgröße",      "Taille sous-titres", "Rozmiar napisów");
        Add7("Subtitle Background","Subtitle Background","Фон субтитрів","Фон субтитров","Fondo de subtítulos","Untertitelhintergrund","Fond des sous-titres","Tło napisów");
        Add7("Colorblind Mode","Colorblind Mode","Режим дальтоніка","Режим дальтоника","Modo daltónico",     "Farbenblindmodus",     "Mode daltonien",     "Tryb daltonisty");
        Add7("High Contrast UI","High Contrast UI","Високий контраст","Высокий контраст","Alto contraste",  "Hoher Kontrast",       "Contraste élevé",    "Wysoki kontrast");
        Add7("Reduce Motion",  "Reduce Motion",  "Менше анімацій", "Меньше анимаций", "Reducir movimiento",  "Bewegung reduzieren",  "Réduire les mouvts.","Mniej ruchu");
        Add7("Photosensitivity Safe Mode","Photosensitivity Safe Mode","Безпечний режим (фотосенсит.)","Безопасный режим (фоточувств.)","Modo seguro fotosen.","Sicherer Modus (lichtempf.)","Mode photosensible","Tryb fotoczuły");
        Add7("UI Scale",       "UI Scale",       "Масштаб UI",     "Масштаб UI",      "Escala de IU",        "UI-Skalierung",        "Échelle IU",         "Skala UI");
        Add7("Game Language",  "Game Language",  "Мова гри",       "Язык игры",       "Idioma del juego",    "Spielsprache",         "Langue du jeu",      "Język gry");
        Add7("Voice Language", "Voice Language", "Мова озвучення", "Язык озвучки",    "Idioma de voz",       "Sprachausgabe",        "Langue voix",        "Język głosu");
        Add7("Hold to Sprint", "Hold to Sprint", "Утримувати спринт","Удерживать спринт","Mantener para correr","Halten zum Sprinten","Maintenir pour courir","Trzymaj by biec");
        Add7("Preset",         "Preset",         "Пресет",         "Пресет",          "Preset",              "Voreinstellung",       "Préréglage",         "Predefiniowane");
        Add7("Render Scale (%)","Render Scale (%)","Рендер-масштаб (%)","Масштаб рендера (%)","Escala render (%)","Rendering-Skala (%)","Échelle rendu (%)","Skala renderu (%)");
        Add7("Anti-Aliasing",  "Anti-Aliasing",  "Згладжування",   "Сглаживание",     "Suavizado",           "Kantenglättung",       "Anti-aliasing",      "Wygładzanie");
        Add7("Difficulty",     "Difficulty",     "Складність",     "Сложность",       "Dificultad",          "Schwierigkeit",        "Difficulté",         "Trudność");

        // -- Description text shown in right rail on hover --
        Add7("Toggle the on-screen frames-per-second counter.",
            "Toggle the on-screen frames-per-second counter.",
            "Увімкнути/вимкнути екранний лічильник кадрів.",
            "Включить/выключить счётчик кадров на экране.",
            "Activa el contador de FPS en pantalla.",
            "FPS-Anzeige auf dem Bildschirm umschalten.",
            "Active/désactive l'indicateur de FPS à l'écran.",
            "Pokaż/ukryj licznik klatek na ekranie.");
        Add7("Quick on/off cap. Use the FPS Cap dropdown in Video for exact values.",
            "Quick on/off cap. Use the FPS Cap dropdown in Video for exact values.",
            "Швидкий перемикач ліміту. Точне значення — у Video → FPS Cap.",
            "Быстрое включение лимита. Точное значение — в Video → FPS Cap.",
            "Limitador rápido. Usa FPS Cap en Video para el valor exacto.",
            "Schneller An/Aus-Schalter. Genauer Wert in Video → FPS Cap.",
            "Bascule rapide. Limite précise dans Vidéo → FPS Cap.",
            "Szybki limit. Dokładna wartość w Wideo → FPS Cap.");
        Add7("Periodically save progress without prompting.",
            "Periodically save progress without prompting.",
            "Періодично зберігає прогрес без сповіщень.",
            "Периодически сохраняет прогресс без подтверждения.",
            "Guarda progreso periódicamente sin preguntar.",
            "Speichert den Fortschritt regelmäßig im Hintergrund.",
            "Sauvegarde régulière, sans confirmation.",
            "Okresowo zapisuje postępy bez pytania.");
        Add7("Combat scaling. Easy / Normal / Hard / Hardcore. Hardcore disables checkpoints.",
            "Combat scaling. Easy / Normal / Hard / Hardcore. Hardcore disables checkpoints.",
            "Складність бою. Легко / Норма / Складно / Хардкор. У хардкорі чекпойнти відключені.",
            "Сложность боя. Легко / Норма / Сложно / Хардкор. В хардкоре нет чекпоинтов.",
            "Escala de combate. Hardcore desactiva los puntos de control.",
            "Kampfskalierung. Hardcore deaktiviert Checkpoints.",
            "Échelle de combat. Hardcore désactive les points de contrôle.",
            "Skalowanie walki. Hardcore wyłącza punkty kontrolne.");
        Add7("Show floating damage numbers above enemies you hit.",
            "Show floating damage numbers above enemies you hit.",
            "Показувати літаючі цифри урону над ворогами.",
            "Показывать всплывающие цифры урона над врагами.",
            "Muestra cifras de daño flotantes sobre los enemigos.",
            "Schwebende Schadenszahlen über Gegnern anzeigen.",
            "Affiche les dégâts au-dessus des ennemis touchés.",
            "Pokazuj cyfry obrażeń nad trafionymi wrogami.");
        Add7("Camera shake on impacts and explosions. Disable if it causes discomfort.",
            "Camera shake on impacts and explosions. Disable if it causes discomfort.",
            "Тряска камери на ударах і вибухах. Вимкніть, якщо викликає дискомфорт.",
            "Тряска камеры от ударов и взрывов. Отключите при дискомфорте.",
            "Vibración por impactos y explosiones.",
            "Bildschirmrütteln bei Treffern und Explosionen.",
            "Tremblement de caméra. Désactivable si gênant.",
            "Wstrząsy kamery przy trafieniach i wybuchach.");
        Add7("Brief freeze on heavy hits for impact. Disable for smoother combat.",
            "Brief freeze on heavy hits for impact. Disable for smoother combat.",
            "Коротка заморозка при важких ударах. Вимкніть для плавнішого бою.",
            "Кратковременная заморозка при тяжёлых ударах.",
            "Pausa breve en golpes pesados.",
            "Kurze Freeze-Pause bei harten Treffern.",
            "Pause brève sur coups lourds.",
            "Krótka pauza przy mocnych trafieniach.");
        Add7("Red edge tint when health is critical. Disable to reduce visual noise.",
            "Red edge tint when health is critical. Disable to reduce visual noise.",
            "Червоний контур при критичному HP. Вимкніть, щоб менше відволікало.",
            "Красный край при критическом HP.",
            "Borde rojo cuando el HP es crítico.",
            "Roter Rand bei kritischem HP.",
            "Vignette rouge quand HP critique.",
            "Czerwony brzeg przy krytycznym HP.");
        Add7("Show contextual hint popups when new mechanics appear.",
            "Show contextual hint popups when new mechanics appear.",
            "Показувати підказки при появі нових механік.",
            "Показывать подсказки при новых механиках.",
            "Muestra sugerencias contextuales con mecánicas nuevas.",
            "Zeigt Tutorial-Tipps bei neuen Mechaniken.",
            "Affiche les astuces sur les nouvelles mécaniques.",
            "Pokazuj wskazówki przy nowych mechanikach.");
        Add7("Overall game volume — affects every channel.",
            "Overall game volume — affects every channel.",
            "Загальна гучність гри — впливає на всі канали.",
            "Общая громкость — влияет на все каналы.",
            "Volumen general — afecta a todos los canales.",
            "Gesamtlautstärke — alle Kanäle betroffen.",
            "Volume général — affecte tous les canaux.",
            "Główna głośność — wpływa na wszystkie kanały.");
        Add7("Background music and ambient score.",
            "Background music and ambient score.",
            "Фонова музика та амбіент.",
            "Фоновая музыка и амбиент.",
            "Música de fondo y ambiental.",
            "Hintergrundmusik und Ambient.",
            "Musique de fond et ambiance.",
            "Muzyka tła i ambient.");
        Add7("Combat and world impact sounds.",
            "Combat and world impact sounds.",
            "Звуки бою та світу.",
            "Звуки боя и мира.",
            "Sonidos de combate y mundo.",
            "Kampf- und Weltklänge.",
            "Sons de combat et du monde.",
            "Dźwięki walki i świata.");
        Add7("Dialogue and narration.",
            "Dialogue and narration.",
            "Діалоги та розповідь.",
            "Диалоги и закадровый текст.",
            "Diálogo y narración.",
            "Dialoge und Erzählung.",
            "Dialogues et narration.",
            "Dialogi i narracja.");
        Add7("Menu, button, and HUD sounds.",
            "Menu, button, and HUD sounds.",
            "Звуки меню, кнопок та HUD.",
            "Звуки меню, кнопок и HUD.",
            "Sonidos de menú, botones e HUD.",
            "Menü-, Tasten- und HUD-Sounds.",
            "Sons du menu, des boutons, du HUD.",
            "Dźwięki menu, przycisków, HUD.");
        Add7("World ambience — wind, fire, water.",
            "World ambience — wind, fire, water.",
            "Атмосфера світу — вітер, вогонь, вода.",
            "Окружение — ветер, огонь, вода.",
            "Ambiente — viento, fuego, agua.",
            "Weltatmosphäre — Wind, Feuer, Wasser.",
            "Ambiance — vent, feu, eau.",
            "Otoczenie — wiatr, ogień, woda.");
        Add7("Silence the game when the window loses focus (Alt-Tab).",
            "Silence the game when the window loses focus (Alt-Tab).",
            "Заглушує гру при втраті фокусу вікна (Alt-Tab).",
            "Глушит звук при потере фокуса (Alt-Tab).",
            "Silencia el juego al perder el foco (Alt-Tab).",
            "Stumm, wenn das Fenster den Fokus verliert.",
            "Coupe le son si la fenêtre perd le focus.",
            "Wycisza grę gdy okno traci fokus.");
        Add7("Wider FOV shows more peripheral vision but distorts edges. Default 75.",
            "Wider FOV shows more peripheral vision but distorts edges. Default 75.",
            "Ширше поле зору — більше периферії, але викривляє краї. За замовч. 75.",
            "Шире поле обзора — больше периферии. По умолчанию 75.",
            "Más amplio: más visión periférica, distorsiona bordes. Por defecto 75.",
            "Breiteres FOV: mehr Peripherie, verzerrte Ränder. Standard 75.",
            "FOV plus large : plus de périphérie, bords distordus. Défaut 75.",
            "Szersze FOV: więcej peryferium, zniekształca brzegi. Domyślnie 75.");

        Add7("Mouse over any option to read what it does.",
            "Mouse over any option to read what it does.",
            "Наведи на будь-яку опцію, щоб прочитати опис.",
            "Наведи на любую опцию, чтобы прочитать описание.",
            "Pasa el ratón por una opción para leer su descripción.",
            "Mit der Maus über eine Option fahren, um die Beschreibung zu lesen.",
            "Survolez une option pour lire sa description.",
            "Najedź na opcję by przeczytać opis.");

        // -- Pause menu + main menu basics --
        Add7("PRESS START",   "PRESS START",   "НАТИСНІТЬ START",  "НАЖМИТЕ START",   "PULSA START",      "DRÜCKE START",     "APPUYEZ SUR START","WCIŚNIJ START");
        Add7("Save",          "Save",          "Зберегти",         "Сохранить",       "Guardar",          "Speichern",        "Sauvegarder",      "Zapisz");
        Add7("Load",          "Load",          "Завантажити",      "Загрузить",       "Cargar",           "Laden",            "Charger",          "Wczytaj");
        Add7("Delete",        "Delete",        "Видалити",         "Удалить",         "Eliminar",         "Löschen",          "Supprimer",        "Usuń");
        Add7("Yes",           "Yes",           "Так",              "Да",              "Sí",               "Ja",               "Oui",              "Tak");
        Add7("No",            "No",            "Ні",               "Нет",             "No",               "Nein",             "Non",              "Nie");
        Add7("OK",            "OK",            "Гаразд",           "ОК",              "Aceptar",          "OK",               "OK",               "OK");
        Add7("Cancel",        "Cancel",        "Скасувати",        "Отмена",          "Cancelar",         "Abbrechen",        "Annuler",          "Anuluj");
        Add7("OPTIONS",       "OPTIONS",       "ОПЦІЇ",            "ОПЦИИ",           "OPCIONES",         "OPTIONEN",         "OPTIONS",          "OPCJE");
        Add7("CREDITS",       "CREDITS",       "ТИТРИ",            "АВТОРЫ",          "CRÉDITOS",         "ABSPANN",          "CRÉDITS",          "TWÓRCY");
        Add7("MAP",           "MAP",           "КАРТА",            "КАРТА",           "MAPA",             "KARTE",            "CARTE",            "MAPA");
        Add7("INVENTORY",     "INVENTORY",     "ІНВЕНТАР",         "ИНВЕНТАРЬ",       "INVENTARIO",       "INVENTAR",         "INVENTAIRE",       "EKWIPUNEK");
        Add7("CODEX",         "CODEX",         "КОДЕКС",           "КОДЕКС",          "CÓDEX",            "KODEX",            "CODEX",            "KODEKS");
        Add7("ACHIEVEMENTS",  "ACHIEVEMENTS",  "ДОСЯГНЕННЯ",       "ДОСТИЖЕНИЯ",      "LOGROS",           "ERFOLGE",          "SUCCÈS",           "OSIĄGNIĘCIA");
        Add7("CAMP STASH",    "CAMP STASH",    "ЗАПАСИ ТАБОРУ",    "ЗАПАСЫ ЛАГЕРЯ",   "ALMACÉN",          "LAGERVORRAT",      "RÉSERVE DU CAMP",  "ZAPASY OBOZU");
        Add7("BACKPACK",      "BACKPACK",      "РЮКЗАК",           "РЮКЗАК",          "MOCHILA",          "RUCKSACK",         "SAC À DOS",        "PLECAK");
        Add7("WOOD",          "WOOD",          "ДЕРЕВО",           "ДЕРЕВО",          "MADERA",           "HOLZ",             "BOIS",             "DREWNO");
        Add7("STONE",         "STONE",         "КАМІНЬ",           "КАМЕНЬ",          "PIEDRA",           "STEIN",            "PIERRE",           "KAMIEŃ");
        Add7("FOOD",          "FOOD",          "ЇЖА",              "ЕДА",             "COMIDA",           "NAHRUNG",          "NOURRITURE",       "JEDZENIE");
        Add7("DIAMONDS",      "DIAMONDS",      "АЛМАЗИ",           "АЛМАЗЫ",          "DIAMANTES",        "DIAMANTEN",        "DIAMANTS",         "DIAMENTY");
        Add7("HP",            "HP",            "HP",               "HP",              "PV",               "LP",               "PV",               "HP");
        Add7("STAMINA",       "STAMINA",       "ВИТРИВАЛІСТЬ",     "ВЫНОСЛИВОСТЬ",    "ENERGÍA",          "AUSDAUER",         "ENDURANCE",        "WYTRZYMAŁOŚĆ");
        Add7("LVL",           "LVL",           "РІВ",              "УР",              "NV",               "LVL",              "NV",               "POZIOM");
        Add7("Level {0}",     "Level {0}",     "Рівень {0}",       "Уровень {0}",     "Nivel {0}",        "Stufe {0}",        "Niveau {0}",       "Poziom {0}");
        Add7("PURIFY TOTEM",  "PURIFY TOTEM",  "ОЧИСТИТИ ТОТЕМ",   "ОЧИСТИТЬ ТОТЕМ",  "PURIFICAR TÓTEM",  "TOTEM REINIGEN",   "PURIFIER LE TOTEM","OCZYŚĆ TOTEM");
        Add7("[E] Open Map",  "[E] Open Map",  "[E] Відкрити мапу","[E] Открыть карту","[E] Abrir mapa",  "[E] Karte öffnen", "[E] Ouvrir carte", "[E] Otwórz mapę");
        Add7("[F] EXECUTE",   "[F] EXECUTE",   "[F] СТРАТИТИ",     "[F] КАЗНИТЬ",     "[F] EJECUTAR",     "[F] HINRICHTEN",   "[F] EXÉCUTER",     "[F] EGZEKUCJA");
        Add7("SLAY THE OVERLORD!","SLAY THE OVERLORD!","ЗНИЩ ВЕЛИТЕНЯ!","УБЕЙ ВЛАДЫКУ!","¡MATA AL SEÑOR!","BESIEGE DEN OVERLORD!","TUE LE SEIGNEUR!","ZABIJ WŁADCĘ!");
        Add7("SURVIVE THE SWARM!","SURVIVE THE SWARM!","ВИЖИВИ ПІД НАТИСКОМ!","ВЫЖИВИ В РОЕ!","SOBREVIVE A LA HORDA!","ÜBERLEBE DEN SCHWARM!","SURVIVRE À LA HORDE !","PRZETRWAJ HORDĘ!");
        Add7("ENGINEERING MASTERY","ENGINEERING MASTERY","ІНЖЕНЕРНА МАЙСТЕРНІСТЬ","ИНЖЕНЕРНОЕ МАСТЕРСТВО","MAESTRÍA EN INGENIERÍA","INGENIEURSMEISTERSCHAFT","MAÎTRISE TECHNIQUE","MISTRZOSTWO INŻYNIERII");
        Add7("Reach a new level of camp efficiency. (0/5)",
             "Reach a new level of camp efficiency. (0/5)",
             "Досягни нового рівня ефективності табору. (0/5)",
             "Достигни нового уровня эффективности лагеря. (0/5)",
             "Alcanza un nuevo nivel de eficiencia. (0/5)",
             "Erreiche eine neue Effizienzstufe. (0/5)",
             "Atteignez un nouveau niveau d'efficacité. (0/5)",
             "Osiągnij nowy poziom efektywności. (0/5)");

        // -- Footer + header text --
        Add7("RESET DEFAULTS","RESET DEFAULTS","СКИНУТИ ДО ЗАМОВЧУВАНЬ","СБРОСИТЬ ПО УМОЛЧАНИЮ","RESTABLECER","ZURÜCKSETZEN","RÉINITIALISER","PRZYWRÓĆ DOMYŚLNE");
        Add7("S E T T I N G S","S E T T I N G S","Н А Л А Ш Т У В А Н Н Я","Н А С Т Р О Й К И","A J U S T E S","E I N S T E L L U N G E N","P A R A M È T R E S","U S T A W I E N I A");

        // == In-game UI vocabulary ==

        // Pause menu + main menu chrome
        Add7("Give Up",            "Give Up",            "Здатися",          "Сдаться",         "Rendirse",          "Aufgeben",          "Abandonner",         "Poddaj się");
        Add7("Back to Menu",       "Back to Menu",       "На головну",       "В главное меню",  "Volver al menú",    "Zum Hauptmenü",     "Menu principal",     "Menu główne");
        Add7("Restart Run",        "Restart Run",        "Почати спробу знову","Начать заново",   "Reiniciar intento", "Lauf neu starten",  "Recommencer",        "Zacznij od nowa");
        Add7("Quit to Desktop",    "Quit to Desktop",    "Вийти на робочий стіл","Выйти на рабочий стол","Salir al escritorio","Beenden",       "Quitter",            "Wyjdź do systemu");
        Add7("Really quit to desktop?","Really quit to desktop?","Точно вийти?","Точно выйти?","¿Salir de verdad?","Wirklich beenden?","Vraiment quitter ?","Na pewno wyjść?");
        Add7("Main Menu",          "Main Menu",          "Головне меню",     "Главное меню",    "Menú principal",    "Hauptmenü",         "Menu principal",     "Menu główne");
        Add7("Save Game",          "Save Game",          "Зберегти гру",     "Сохранить игру",  "Guardar partida",   "Spiel speichern",   "Sauvegarder",        "Zapisz grę");
        Add7("Load Game",          "Load Game",          "Завантажити гру",  "Загрузить игру",  "Cargar partida",    "Spiel laden",       "Charger",            "Wczytaj grę");
        Add7("Save Slot",          "Save Slot",          "Слот збереження",  "Слот сохранения", "Ranura",            "Speicherplatz",     "Emplacement",        "Slot zapisu");
        Add7("Empty",              "Empty",              "Порожньо",         "Пусто",           "Vacío",             "Leer",              "Vide",               "Pusto");
        Add7("Overwrite?",         "Overwrite?",         "Перезаписати?",    "Перезаписать?",   "¿Sobrescribir?",    "Überschreiben?",    "Écraser ?",          "Nadpisać?");

        // Building / construction UI
        Add7("Build",              "Build",              "Будувати",         "Строить",         "Construir",         "Bauen",             "Construire",         "Buduj");
        Add7("Building",           "Building",           "Будівля",          "Постройка",       "Edificio",          "Gebäude",           "Bâtiment",           "Budynek");
        Add7("Cost",               "Cost",               "Вартість",         "Стоимость",       "Coste",             "Kosten",            "Coût",               "Koszt");
        Add7("Upgrade",            "Upgrade",            "Покращити",        "Улучшить",        "Mejorar",           "Verbessern",        "Améliorer",          "Ulepsz");
        Add7("Repair",             "Repair",             "Відновити",        "Починить",        "Reparar",           "Reparieren",        "Réparer",            "Napraw");
        Add7("Demolish",           "Demolish",           "Знести",           "Снести",          "Demoler",           "Abreißen",          "Démolir",            "Wyburz");
        Add7("Place",              "Place",              "Розмістити",       "Разместить",      "Colocar",           "Platzieren",        "Placer",             "Umieść");
        Add7("Level",              "Level",              "Рівень",           "Уровень",         "Nivel",             "Stufe",             "Niveau",             "Poziom");
        Add7("Max Level",          "Max Level",          "Макс. рівень",     "Макс. уровень",   "Nivel máximo",      "Maximalstufe",      "Niveau max",         "Maks. poziom");
        Add7("Available",          "Available",          "Доступно",         "Доступно",        "Disponible",        "Verfügbar",         "Disponible",         "Dostępne");
        Add7("Locked",             "Locked",             "Заблоковано",      "Заблокировано",   "Bloqueado",         "Gesperrt",          "Verrouillé",         "Zablokowane");
        Add7("Unlocked",           "Unlocked",           "Розблоковано",     "Разблокировано",  "Desbloqueado",      "Freigeschaltet",    "Déverrouillé",       "Odblokowane");
        Add7("Required",           "Required",           "Потрібно",         "Требуется",       "Requerido",         "Erforderlich",      "Requis",             "Wymagane");
        Add7("Insufficient resources","Insufficient resources","Недостатньо ресурсів","Недостаточно ресурсов","Recursos insuficientes","Nicht genug Ressourcen","Ressources insuffisantes","Brak zasobów");
        Add7("Construction Complete","Construction Complete","Будівлю завершено","Постройка завершена","Construcción completada","Bau abgeschlossen","Construction terminée","Budowa ukończona");

        // Mission UI
        Add7("Missions",           "Missions",           "Місії",            "Миссии",          "Misiones",          "Missionen",         "Missions",           "Misje");
        Add7("Mission",            "Mission",            "Місія",            "Миссия",          "Misión",            "Mission",           "Mission",            "Misja");
        Add7("Active Missions",    "Active Missions",    "Активні місії",    "Активные миссии", "Misiones activas",  "Aktive Missionen",  "Missions actives",   "Aktywne misje");
        Add7("Completed",          "Completed",          "Виконано",         "Выполнено",       "Completado",        "Abgeschlossen",     "Terminé",            "Ukończono");
        Add7("In Progress",        "In Progress",        "Виконується",      "В процессе",      "En progreso",       "Läuft",             "En cours",           "W trakcie");
        Add7("Failed",             "Failed",             "Провалено",        "Провалена",       "Fallida",           "Fehlgeschlagen",    "Échouée",            "Nieudana");
        Add7("Objective",          "Objective",          "Завдання",         "Цель",            "Objetivo",          "Ziel",              "Objectif",           "Cel");
        Add7("Reward",             "Reward",             "Нагорода",         "Награда",         "Recompensa",        "Belohnung",         "Récompense",         "Nagroda");
        Add7("Rewards",            "Rewards",            "Нагороди",         "Награды",         "Recompensas",       "Belohnungen",       "Récompenses",        "Nagrody");
        Add7("Accept",             "Accept",             "Прийняти",         "Принять",         "Aceptar",           "Annehmen",          "Accepter",           "Przyjmij");
        Add7("Decline",            "Decline",            "Відхилити",        "Отклонить",       "Rechazar",          "Ablehnen",          "Refuser",            "Odrzuć");
        Add7("Mission Board",      "Mission Board",      "Дошка місій",      "Доска миссий",    "Tablero de misiones","Missionsbrett",    "Tableau de missions","Tablica misji");
        Add7("Generate Mission",   "Generate Mission",   "Створити місію",   "Сгенерировать миссию","Generar misión","Mission generieren","Générer une mission","Generuj misję");
        Add7("Mission Complete",   "Mission Complete",   "Місія виконана",   "Миссия выполнена","Misión completada", "Mission erfüllt",   "Mission terminée",   "Misja ukończona");
        Add7("Mission Failed",     "Mission Failed",     "Місію провалено",  "Миссия провалена","Misión fallida",    "Mission fehlgeschlagen","Mission échouée","Misja nieudana");

        // Shop UI
        Add7("Shop",               "Shop",               "Магазин",          "Магазин",         "Tienda",            "Laden",             "Boutique",           "Sklep");
        Add7("Buy",                "Buy",                "Купити",           "Купить",          "Comprar",           "Kaufen",            "Acheter",            "Kup");
        Add7("Sell",               "Sell",               "Продати",          "Продать",         "Vender",            "Verkaufen",         "Vendre",             "Sprzedaj");
        Add7("Equip",              "Equip",              "Спорядити",        "Экипировать",     "Equipar",           "Ausrüsten",         "Équiper",            "Załóż");
        Add7("Equipped",           "Equipped",           "Споряджено",       "Экипировано",     "Equipado",          "Ausgerüstet",       "Équipé",             "Założono");
        Add7("Owned",              "Owned",              "У наявності",      "Есть",            "Adquirido",         "Im Besitz",         "Possédé",            "Posiadane");
        Add7("Price",              "Price",              "Ціна",             "Цена",            "Precio",            "Preis",             "Prix",               "Cena");
        Add7("Purchase",           "Purchase",           "Купити",           "Купить",          "Comprar",           "Kaufen",            "Acheter",            "Kup");
        Add7("Weapons",            "Weapons",            "Зброя",            "Оружие",          "Armas",             "Waffen",            "Armes",              "Broń");
        Add7("Armor",              "Armor",              "Броня",            "Броня",           "Armadura",          "Rüstung",           "Armure",             "Pancerz");
        Add7("Consumables",        "Consumables",        "Витратні",         "Расходники",      "Consumibles",       "Verbrauchsgüter",   "Consommables",       "Materiały");
        Add7("Items",              "Items",              "Предмети",         "Предметы",        "Objetos",           "Gegenstände",       "Objets",             "Przedmioty");
        Add7("Stats",              "Stats",              "Характеристики",   "Характеристики",  "Estadísticas",      "Werte",             "Statistiques",       "Statystyki");
        Add7("Damage",             "Damage",             "Урон",             "Урон",            "Daño",              "Schaden",           "Dégâts",             "Obrażenia");
        Add7("Defense",            "Defense",            "Захист",            "Защита",          "Defensa",           "Verteidigung",      "Défense",            "Obrona");
        Add7("Description",        "Description",        "Опис",             "Описание",        "Descripción",       "Beschreibung",      "Description",        "Opis");

        // Resource names + camp HUD
        Add7("Wood",               "Wood",               "Дерево",           "Дерево",          "Madera",            "Holz",              "Bois",               "Drewno");
        Add7("Stone",              "Stone",              "Камінь",           "Камень",          "Piedra",            "Stein",             "Pierre",             "Kamień");
        Add7("Food",               "Food",               "Їжа",              "Еда",             "Comida",            "Nahrung",           "Nourriture",         "Jedzenie");
        Add7("Diamond",            "Diamond",            "Алмаз",            "Алмаз",           "Diamante",          "Diamant",           "Diamant",            "Diament");
        Add7("XP",                 "XP",                 "ДОСВІД",           "ОПЫТ",            "EXP",               "ERFAHRUNG",         "EXP",                "DOŚW.");
        Add7("XP Crystal",         "XP Crystal",         "Кристал досвіду",  "Кристалл опыта",  "Cristal de EXP",    "EP-Kristall",       "Cristal d'EXP",      "Kryształ EXP");
        Add7("Iron",               "Iron",               "Залізо",           "Железо",          "Hierro",            "Eisen",             "Fer",                "Żelazo");
        Add7("Gold",               "Gold",               "Золото",           "Золото",          "Oro",               "Gold",              "Or",                 "Złoto");
        Add7("Day",                "Day",                "День",             "День",            "Día",               "Tag",               "Jour",               "Dzień");
        Add7("Night",              "Night",              "Ніч",              "Ночь",            "Noche",             "Nacht",             "Nuit",               "Noc");
        Add7("Storm",              "Storm",              "Шторм",            "Шторм",           "Tormenta",          "Sturm",             "Tempête",            "Burza");
        Add7("Clear",              "Clear",              "Ясно",             "Ясно",            "Despejado",         "Klar",              "Clair",              "Pogodnie");
        Add7("Region Cleared",     "Region Cleared",     "Регіон зачищено",  "Регион зачищен",  "Región despejada",  "Region erobert",    "Région libérée",     "Region oczyszczony");

        // Map / region descriptions
        Add7("World Map",          "World Map",          "Мапа світу",       "Карта мира",      "Mapa del mundo",    "Weltkarte",         "Carte du monde",     "Mapa świata");
        Add7("Region",             "Region",             "Регіон",           "Регион",          "Región",            "Region",            "Région",             "Region");
        Add7("Regions",            "Regions",            "Регіони",          "Регионы",         "Regiones",          "Regionen",          "Régions",            "Regiony");
        Add7("Conquered",          "Conquered",          "Захоплено",        "Захвачен",        "Conquistado",       "Erobert",           "Conquise",           "Podbity");
        Add7("Available Region",   "Available Region",   "Доступний регіон", "Доступный регион","Región disponible", "Verfügbare Region", "Région disponible",  "Dostępny region");
        Add7("Recommended Power",  "Recommended Power",  "Рекомендована сила","Рекомендуемая сила","Poder recomendado","Empfohlene Macht","Puissance recomm.",  "Zalecana moc");
        Add7("Power Level",        "Power Level",        "Рівень сили",      "Уровень силы",    "Nivel de poder",    "Machtstufe",        "Niveau de puissance","Poziom mocy");
        Add7("Confront",           "Confront",           "Атакувати",        "Атаковать",       "Atacar",            "Angreifen",         "Affronter",          "Zaatakuj");
        Add7("Travel",             "Travel",             "Подорожувати",     "Путешествовать",  "Viajar",            "Reisen",            "Voyager",            "Podróżuj");
        Add7("Enter",              "Enter",              "Увійти",           "Войти",           "Entrar",            "Betreten",          "Entrer",             "Wejdź");

        // Generic combat / status
        Add7("Boss",               "Boss",               "Бос",              "Босс",            "Jefe",              "Boss",              "Boss",               "Boss");
        Add7("Enemy",              "Enemy",              "Ворог",            "Враг",            "Enemigo",           "Gegner",            "Ennemi",             "Wróg");
        Add7("Enemies",            "Enemies",            "Вороги",           "Враги",           "Enemigos",          "Gegner",            "Ennemis",            "Wrogowie");
        Add7("Player",             "Player",             "Гравець",          "Игрок",           "Jugador",           "Spieler",           "Joueur",             "Gracz");
        Add7("Health",             "Health",             "Здоров'я",         "Здоровье",        "Salud",             "Gesundheit",        "Santé",              "Zdrowie");
        Add7("Energy",             "Energy",             "Енергія",          "Энергия",         "Energía",           "Energie",           "Énergie",            "Energia");
        Add7("Stamina",            "Stamina",            "Витривалість",     "Выносливость",    "Energía",           "Ausdauer",          "Endurance",          "Wytrzymałość");
        Add7("Critical Hit",       "Critical Hit",       "Критичний удар",   "Критический удар","Golpe crítico",     "Kritischer Treffer","Coup critique",      "Cios krytyczny");
        Add7("Defeated",           "Defeated",           "Переможено",       "Побеждён",        "Derrotado",         "Besiegt",           "Vaincu",             "Pokonany");
        Add7("Victory",            "Victory",            "Перемога",         "Победа",          "Victoria",          "Sieg",              "Victoire",           "Zwycięstwo");
        Add7("Defeat",             "Defeat",             "Поразка",          "Поражение",       "Derrota",           "Niederlage",        "Défaite",            "Porażka");
        Add7("You Died",           "You Died",           "Ви загинули",      "Вы погибли",      "Has muerto",        "Du bist gestorben", "Vous êtes mort",     "Zginąłeś");
        Add7("Respawn",            "Respawn",            "Відродитися",      "Возродиться",     "Reaparecer",        "Wiederbeleben",     "Réapparaître",       "Odrodzenie");
        Add7("Wait",               "Wait",               "Чекати",           "Ждать",           "Esperar",           "Warten",            "Attendre",           "Czekaj");

        // Achievements + codex
        Add7("Achievement Unlocked","Achievement Unlocked","Досягнення розблоковано","Достижение получено","Logro desbloqueado","Erfolg freigeschaltet","Succès débloqué","Osiągnięcie zdobyte");
        Add7("New Lore Entry",     "New Lore Entry",     "Новий запис у лорі","Новая запись в лоре","Nuevo registro",    "Neuer Eintrag",     "Nouvelle entrée",    "Nowy wpis");
        Add7("Codex Updated",      "Codex Updated",      "Кодекс оновлено",  "Кодекс обновлён", "Códex actualizado", "Kodex aktualisiert","Codex mis à jour",   "Kodeks zaktualiz.");

        // Prompts / hints
        Add7("Press {0}",          "Press {0}",          "Натисни {0}",      "Нажми {0}",       "Pulsa {0}",         "Drücke {0}",        "Appuie {0}",         "Wciśnij {0}");
        Add7("Hold {0}",           "Hold {0}",           "Утримуй {0}",      "Удерживай {0}",   "Mantén {0}",        "Halte {0}",         "Maintenir {0}",      "Trzymaj {0}");
        Add7("Press SPACE to Skip","Press SPACE to Skip","Натисни ПРОБІЛ щоб пропустити","Нажми ПРОБЕЛ чтобы пропустить","Pulsa ESPACIO para saltar","SPACE drücken zum Überspringen","Appuyez sur ESPACE pour passer","Wciśnij SPACJĘ, by pominąć");
        Add7("TIP:",               "TIP:",               "ПОРАДА:",          "СОВЕТ:",          "CONSEJO:",          "TIPP:",             "ASTUCE :",           "WSKAZÓWKA:");
        Add7("Tutorial",           "Tutorial",           "Навчання",         "Обучение",        "Tutorial",          "Tutorial",          "Tutoriel",           "Samouczek");
        Add7("Custom key bindings coming soon.",
             "Custom key bindings coming soon.",
             "Власні клавіші — скоро.",
             "Кастомные клавиши — скоро.",
             "Combinaciones personalizadas próximamente.",
             "Eigene Tastenbelegung folgt bald.",
             "Touches personnalisées bientôt.",
             "Własne klawisze wkrótce.");

        SeedInGameRuntime();
        SeedPolishBackfill();
        SeedDialogues();
        SeedPrompts();
    }

    // Add6 fills EN/UK/RU/ES/DE/FR but skips Polish, so Polish fell
    // through to English for ~65 keys (menu chrome, section headers,
    // toggle rows). This backfill patches s_pl directly so nothing is
    // still English when the player picks Polski.
    private static void SeedPolishBackfill()
    {
        // Menu chrome
        AddPl("RESUME", "WZNÓW");
        AddPl("BACK TO MENU", "DO MENU");
        AddPl("SETTINGS", "USTAWIENIA");
        AddPl("BACK", "WSTECZ");
        AddPl("CLOSE", "ZAMKNIJ");
        AddPl("APPLY", "ZASTOSUJ");
        AddPl("APPLY & CLOSE", "ZASTOSUJ I ZAMKNIJ");
        AddPl("RESET DEFAULTS", "PRZYWRÓĆ DOMYŚLNE");
        AddPl("DISCARD", "ODRZUĆ");
        AddPl("NEW GAME", "NOWA GRA");
        AddPl("LOAD GAME", "WCZYTAJ GRĘ");
        AddPl("LOAD", "WCZYTAJ");
        AddPl("QUIT", "WYJDŹ");
        AddPl("QUIT TO DESKTOP", "WYJDŹ DO PULPITU");
        AddPl("EXIT", "WYJDŹ");
        AddPl("PLAY", "GRAJ");
        AddPl("START", "START");
        AddPl("PAUSED", "PAUZA");
        AddPl("CONTINUE", "KONTYNUUJ");

        // Section headers
        AddPl("HUD", "HUD");
        AddPl("SAVE", "ZAPIS");
        AddPl("MIX", "MIKS");
        AddPl("DISPLAY", "WYŚWIETLANIE");
        AddPl("CAMERA", "KAMERA");
        AddPl("QUALITY PRESET", "PROFIL JAKOŚCI");
        AddPl("TIERS", "POZIOMY");
        AddPl("POST-FX", "POST-FX");
        AddPl("MOUSE & KEYBOARD", "MYSZ I KLAWIATURA");
        AddPl("MOUSE & CAMERA", "MYSZ I KAMERA");
        AddPl("GAMEPAD", "PAD");
        AddPl("BINDINGS", "PRZYPISANIA");
        AddPl("FEEDBACK", "REAKCJA");
        AddPl("DIFFICULTY", "TRUDNOŚĆ");
        AddPl("TUTORIAL", "SAMOUCZEK");
        AddPl("BEHAVIOUR", "ZACHOWANIE");
        AddPl("VISUAL AIDS", "POMOCE WIZUALNE");
        AddPl("UI", "INTERFEJS");
        AddPl("TEXT", "TEKST");
        AddPl("SUBTITLES", "NAPISY");
        AddPl("PREVIEW", "PODGLĄD");
        AddPl("DESCRIPTION", "OPIS");

        // Toggle rows
        AddPl("Show FPS", "Pokaż FPS");
        AddPl("Limit FPS", "Ogranicz FPS");
        AddPl("Auto-Save", "Autozapis");
        AddPl("Damage Popups", "Cyfry obrażeń");
        AddPl("Screen Shake", "Wstrząsy ekranu");
        AddPl("Hit-Stop FX", "Pauza przy trafieniu");
        AddPl("Low HP Vignette", "Winieta niskiego HP");
        AddPl("Tutorial Hints", "Wskazówki samouczka");
        AddPl("Master", "Główna");
        AddPl("Music", "Muzyka");
        AddPl("Sound FX", "Efekty");
        AddPl("Voice", "Głos");
        AddPl("Ambient", "Otoczenie");
        AddPl("Mute When Unfocused", "Wycisz przy utracie fokusu");
        AddPl("Resolution", "Rozdzielczość");
        AddPl("Window Mode", "Tryb okna");
        AddPl("Refresh Rate", "Częst. odświeżania");
        AddPl("Monitor", "Monitor");
        AddPl("FPS Cap", "Limit FPS");
        AddPl("V-Sync", "Synchr. pionowa");
        AddPl("Field of View", "Pole widzenia");
        AddPl("Brightness", "Jasność");
        AddPl("Gamma", "Gamma");

        // Runtime-set button labels from MainMenuManager (mixed case)
        AddPl("Continue", "Kontynuuj");
        AddPl("Start Adventure!", "Rozpocznij przygodę!");
        AddPl("Give Up", "Poddaj się");
        AddPl("Back to Menu", "Do menu");
        AddPl("You sure?\nAll journey progress will be lost", "Na pewno?\nCały postęp wyprawy zostanie utracony");
        AddPl("Are you sure?", "Na pewno?");

        // Same mixed-case pair for the other locales — MainMenuManager
        // sets these at runtime, they aren't inspector-baked.
        Add7("Continue", "Continue", "Продовжити", "Продолжить", "Continuar", "Fortsetzen", "Continuer", "Kontynuuj");
        Add7("Start Adventure!", "Start Adventure!", "Почати пригоду!", "Начать приключение!", "¡Comenzar aventura!", "Abenteuer starten!", "Commencer l'aventure !", "Rozpocznij przygodę!");
        Add7("You sure?\nAll journey progress will be lost",
             "You sure?\nAll journey progress will be lost",
             "Точно?\nВесь прогрес подорожі буде втрачено",
             "Точно?\nВесь прогресс путешествия будет потерян",
             "¿Seguro?\nSe perderá todo el progreso del viaje",
             "Sicher?\nDer gesamte Reisefortschritt geht verloren",
             "Sûr ?\nToute la progression du voyage sera perdue",
             "Na pewno?\nCały postęp wyprawy zostanie utracony");
        Add7("Are you sure?", "Are you sure?", "Ти впевнений?", "Ты уверен?", "¿Estás seguro?", "Bist du sicher?", "Es-tu sûr ?", "Na pewno?");
    }

    private static void AddPl(string key, string pl)
    {
        s_pl[key] = pl;
    }

    // Camp intro + tutorial subtitle lines. Keys stay as the English
    // string so the call sites read naturally (Tr("Stranger: ...")).
    private static void SeedDialogues()
    {
        // Camp intro (CampDirector.IntroCoroutine)
        Add7("Stranger: Welcome to your new Camp. This is your safe haven between dangerous journeys.",
             "Stranger: Welcome to your new Camp. This is your safe haven between dangerous journeys.",
             "Незнайомець: Ласкаво просимо до твого табору. Це твій безпечний прихисток між небезпечними подорожами.",
             "Незнакомец: Добро пожаловать в твой лагерь. Это твоё безопасное убежище между опасными путешествиями.",
             "Extraño: Bienvenido a tu campamento. Es tu refugio seguro entre viajes peligrosos.",
             "Fremder: Willkommen in deinem Lager. Es ist dein sicherer Hafen zwischen gefährlichen Reisen.",
             "Étranger : Bienvenue dans ton camp. C'est ton havre entre les voyages dangereux.",
             "Nieznajomy: Witaj w swoim obozie. To twoje bezpieczne schronienie między niebezpiecznymi wyprawami.");
        Add7("Stranger: Up there is the Camp Stash. All the resources you manage to bring back from the forest are stored here safely.",
             "Stranger: Up there is the Camp Stash. All the resources you manage to bring back from the forest are stored here safely.",
             "Незнайомець: Там нагорі — Запаси Табору. Усі ресурси, які ти приносиш із лісу, зберігаються там у безпеці.",
             "Незнакомец: Там наверху — Запасы Лагеря. Все ресурсы, что ты приносишь из леса, хранятся там в безопасности.",
             "Extraño: Allí arriba está el Almacén del Campamento. Todos los recursos que traes del bosque se guardan allí.",
             "Fremder: Dort oben ist der Lagervorrat. Alle Ressourcen, die du aus dem Wald bringst, sind dort sicher.",
             "Étranger : Là-haut se trouve la Réserve du Camp. Toutes les ressources rapportées de la forêt y sont stockées.",
             "Nieznajomy: Tam na górze są Zapasy Obozu. Wszystkie zasoby, które przyniesiesz z lasu, są tam bezpieczne.");
        Add7("Stranger: You can use those resources to rebuild this place. Walk up to a plot and hold [E]. Restored buildings will generate resources over time!",
             "Stranger: You can use those resources to rebuild this place. Walk up to a plot and hold [E]. Restored buildings will generate resources over time!",
             "Незнайомець: Ти можеш використовувати ці ресурси, щоб відбудовувати це місце. Підійди до ділянки й утримуй [E]. Відновлені будівлі даватимуть ресурси з часом!",
             "Незнакомец: Используй эти ресурсы, чтобы восстанавливать это место. Подойди к участку и удерживай [E]. Восстановленные постройки будут давать ресурсы со временем!",
             "Extraño: Puedes usar esos recursos para reconstruir este lugar. Acércate a una parcela y mantén [E]. ¡Los edificios restaurados generarán recursos con el tiempo!",
             "Fremder: Nutze diese Ressourcen, um diesen Ort wiederaufzubauen. Geh zu einem Grundstück und halte [E]. Wiederhergestellte Gebäude erzeugen mit der Zeit Ressourcen!",
             "Étranger : Utilise ces ressources pour reconstruire cet endroit. Approche-toi d'une parcelle et maintiens [E]. Les bâtiments restaurés génèrent des ressources avec le temps !",
             "Nieznajomy: Możesz użyć tych zasobów, by odbudować to miejsce. Podejdź do działki i przytrzymaj [E]. Odbudowane budynki będą z czasem generować zasoby!");
        Add7("Stranger: Check the Notice Board over there. You can take on special missions to earn resources and valuable Diamonds.",
             "Stranger: Check the Notice Board over there. You can take on special missions to earn resources and valuable Diamonds.",
             "Незнайомець: Глянь на Дошку Оголошень. Там можна брати особливі місії, щоб заробляти ресурси й цінні Алмази.",
             "Незнакомец: Проверь Доску Объявлений. Там можно брать особые миссии, чтобы получать ресурсы и ценные Алмазы.",
             "Extraño: Revisa el Tablón de Anuncios. Puedes aceptar misiones especiales para ganar recursos y valiosos Diamantes.",
             "Fremder: Schau am Anschlagbrett vorbei. Dort kannst du besondere Missionen annehmen für Ressourcen und wertvolle Diamanten.",
             "Étranger : Regarde le Tableau d'Annonces. Tu peux y prendre des missions spéciales pour gagner ressources et précieux Diamants.",
             "Nieznajomy: Sprawdź Tablicę Ogłoszeń. Możesz podjąć specjalne misje po zasoby i cenne Diamenty.");
        Add7("Stranger: At the edge of the camp is the mysterious Shop. Use your Diamonds there to buy permanent meta-upgrades for your future runs.",
             "Stranger: At the edge of the camp is the mysterious Shop. Use your Diamonds there to buy permanent meta-upgrades for your future runs.",
             "Незнайомець: На краю табору стоїть таємничий Магазин. Витрачай там Алмази на постійні мета-покращення для майбутніх забігів.",
             "Незнакомец: На краю лагеря — загадочный Магазин. Трать там Алмазы на постоянные мета-улучшения для будущих забегов.",
             "Extraño: Al borde del campamento hay una Tienda misteriosa. Usa tus Diamantes para comprar mejoras meta permanentes para tus futuras partidas.",
             "Fremder: Am Rand des Lagers steht der geheimnisvolle Laden. Gib deine Diamanten dort für permanente Meta-Upgrades aus.",
             "Étranger : À la lisière du camp se trouve la mystérieuse Boutique. Dépense tes Diamants pour des méta-améliorations permanentes.",
             "Nieznajomy: Na skraju obozu jest tajemniczy Sklep. Wydawaj tam Diamenty na trwałe meta-ulepszenia na przyszłe wyprawy.");
        Add7("[TIP] Try to prioritize upgrading your Storage Vault early on, so you have enough space for all your hard-earned loot!",
             "[TIP] Try to prioritize upgrading your Storage Vault early on, so you have enough space for all your hard-earned loot!",
             "[ПОРАДА] Постарайся спочатку прокачати Сховище — щоб було місце для всієї здобичі!",
             "[СОВЕТ] Постарайся сначала прокачать Хранилище — чтобы было место для всей добычи!",
             "[CONSEJO] Prioriza mejorar la Bóveda de Almacenamiento cuanto antes para tener sitio para todo el botín.",
             "[TIPP] Baue früh das Lagergewölbe aus, damit du Platz für die ganze Beute hast!",
             "[ASTUCE] Améliore ta Réserve tôt pour avoir la place pour tout ton butin !",
             "[WSKAZÓWKA] Ulepsz najpierw Skarbiec, byś miał miejsce na cały łup!");

        // Level_1 tutorial subtitles + hints
        Add7("Stranger: Thank the heavens you're here! My cart is busted and this forest is cursed.",
             "Stranger: Thank the heavens you're here! My cart is busted and this forest is cursed.",
             "Незнайомець: Слава небесам, ти тут! Мій віз зламався, а цей ліс проклятий.",
             "Незнакомец: Слава небесам, ты здесь! Моя телега сломалась, а этот лес проклят.",
             "Extraño: ¡Gracias al cielo estás aquí! Mi carro se ha roto y este bosque está maldito.",
             "Fremder: Dem Himmel sei Dank, du bist hier! Mein Karren ist kaputt und dieser Wald ist verflucht.",
             "Étranger : Dieu merci, tu es là ! Ma charrette est en panne et cette forêt est maudite.",
             "Nieznajomy: Dzięki niebiosom, że jesteś! Mój wóz się zepsuł, a ten las jest przeklęty.");
        Add7("Stranger: I need wood to fix the wheels. Gather 12 pieces, or we're not getting out of here alive!",
             "Stranger: I need wood to fix the wheels. Gather 12 pieces, or we're not getting out of here alive!",
             "Незнайомець: Мені потрібне дерево, щоб полагодити колеса. Збери 12 шматків, інакше живими звідси не виберемось!",
             "Незнакомец: Мне нужно дерево, чтобы починить колёса. Собери 12 кусков, иначе живыми отсюда не выберемся!",
             "Extraño: Necesito madera para arreglar las ruedas. Reúne 12 piezas o no saldremos vivos.",
             "Fremder: Ich brauche Holz für die Räder. Sammle 12 Stück, sonst kommen wir hier nicht lebend raus!",
             "Étranger : Il me faut du bois pour réparer les roues. Récupère 12 morceaux, sinon on ne sortira pas vivants !",
             "Nieznajomy: Potrzebuję drewna do naprawy kół. Zbierz 12 sztuk, inaczej stąd nie wyjdziemy żywi!");
        Add7("[TIP] Walk up to a tree and press Left Mouse Button to attack and gather wood.",
             "[TIP] Walk up to a tree and press Left Mouse Button to attack and gather wood.",
             "[ПОРАДА] Підійди до дерева і натисни ліву кнопку миші, щоб рубати і збирати дерево.",
             "[СОВЕТ] Подойди к дереву и нажми левую кнопку мыши, чтобы рубить и собирать древесину.",
             "[CONSEJO] Acércate a un árbol y pulsa el botón izquierdo del ratón para atacar y recolectar madera.",
             "[TIPP] Geh zu einem Baum und drücke die linke Maustaste, um zu schlagen und Holz zu sammeln.",
             "[ASTUCE] Approche-toi d'un arbre et clique gauche pour l'attaquer et récolter du bois.",
             "[WSKAZÓWKA] Podejdź do drzewa i wciśnij lewy przycisk myszy, by atakować i zbierać drewno.");
        Add7("Stranger: Watch out! They're crawling from the dirt!",
             "Stranger: Watch out! They're crawling from the dirt!",
             "Незнайомець: Обережно! Вони лізуть із землі!",
             "Незнакомец: Осторожно! Они вылезают из земли!",
             "Extraño: ¡Cuidado! ¡Salen del suelo!",
             "Fremder: Vorsicht! Sie kriechen aus der Erde!",
             "Étranger : Attention ! Ils sortent de terre !",
             "Nieznajomy: Uważaj! Wypełzają spod ziemi!");
        Add7("[TIP] Enemies are attacking! Use Left Mouse Button to fight back and watch your health.",
             "[TIP] Enemies are attacking! Use Left Mouse Button to fight back and watch your health.",
             "[ПОРАДА] Вороги атакують! Ліва кнопка миші — атака, стеж за здоров'ям.",
             "[СОВЕТ] Враги атакуют! Левая кнопка мыши — атака, следи за здоровьем.",
             "[CONSEJO] ¡Los enemigos atacan! Usa el botón izquierdo del ratón y vigila tu salud.",
             "[TIPP] Feinde greifen an! Linke Maustaste zum Kämpfen, achte auf deine Gesundheit.",
             "[ASTUCE] Ennemis en approche ! Clic gauche pour attaquer, surveille ta santé.",
             "[WSKAZÓWKA] Wrogowie atakują! Lewy przycisk myszy do walki, uważaj na zdrowie.");
        Add7("Stranger: Good job! Wait... do you hear that?",
             "Stranger: Good job! Wait... do you hear that?",
             "Незнайомець: Молодець! Стривай… ти це чуєш?",
             "Незнакомец: Молодец! Погоди… ты это слышишь?",
             "Extraño: ¡Bien hecho! Espera… ¿oyes eso?",
             "Fremder: Gut gemacht! Warte… hörst du das?",
             "Étranger : Bien joué ! Attends… tu entends ça ?",
             "Nieznajomy: Dobra robota! Czekaj… słyszysz to?");
        Add7("Stranger: IT'S A WHOLE ARMY! THERE'S TOO MANY!",
             "Stranger: IT'S A WHOLE ARMY! THERE'S TOO MANY!",
             "Незнайомець: ЦЕ ЦІЛА АРМІЯ! ЇХ ЗАБАГАТО!",
             "Незнакомец: ЭТО ЦЕЛАЯ АРМИЯ! ИХ СЛИШКОМ МНОГО!",
             "Extraño: ¡ES UN EJÉRCITO ENTERO! ¡SON DEMASIADOS!",
             "Fremder: DAS IST EINE GANZE ARMEE! ES SIND ZU VIELE!",
             "Étranger : C'EST UNE ARMÉE ENTIÈRE ! ILS SONT TROP NOMBREUX !",
             "Nieznajomy: TO CAŁA ARMIA! JEST ICH ZA DUŻO!");
        Add7("Stranger: RUN TO THE HORSE, NOW!!",
             "Stranger: RUN TO THE HORSE, NOW!!",
             "Незнайомець: ДО КОНЯ, ШВИДКО!!",
             "Незнакомец: К КОНЮ, БЫСТРО!!",
             "Extraño: ¡AL CABALLO, YA!!",
             "Fremder: ZUM PFERD, SOFORT!!",
             "Étranger : AU CHEVAL, TOUT DE SUITE !!",
             "Nieznajomy: DO KONIA, JUŻ!!");
        Add7("[TIP] You can't kill them! Hold SHIFT to sprint and reach the Extraction Point!",
             "[TIP] You can't kill them! Hold SHIFT to sprint and reach the Extraction Point!",
             "[ПОРАДА] Їх не вбити! Утримуй SHIFT, щоб бігти, і дістанься точки евакуації!",
             "[СОВЕТ] Их не убить! Удерживай SHIFT для спринта и добеги до точки эвакуации!",
             "[CONSEJO] ¡No puedes matarlos! Mantén SHIFT para esprintar y llega al Punto de Extracción.",
             "[TIPP] Sie sind unbesiegbar! Halte SHIFT zum Sprinten und erreiche den Fluchtpunkt!",
             "[ASTUCE] Impossible de les tuer ! Maintiens SHIFT pour sprinter jusqu'au Point d'Extraction !",
             "[WSKAZÓWKA] Nie da się ich zabić! Trzymaj SHIFT, by biec, i dotrzyj do Punktu Ewakuacji!");
        Add7("YOU HAVE FALLEN...",
             "<color=#8B0000>YOU HAVE FALLEN...</color>",
             "<color=#8B0000>ТИ ЗАГИНУВ...</color>",
             "<color=#8B0000>ТЫ ПАЛ...</color>",
             "<color=#8B0000>HAS CAÍDO...</color>",
             "<color=#8B0000>DU BIST GEFALLEN...</color>",
             "<color=#8B0000>TU ES TOMBÉ...</color>",
             "<color=#8B0000>ZGINĄŁEŚ...</color>");

        // Objective / mission text (Level1_QuestManager.UpdateObjectiveUI)
        Add7("Main Quest",         "Main Quest",         "Головне завдання",  "Главный квест",   "Misión principal",   "Hauptquest",         "Quête principale",     "Główna misja");
        Add7("Investigate the Outpost","Investigate the Outpost","Дослідити застава","Разведать заставу","Investigar el puesto","Vorposten untersuchen","Enquêter au poste","Zbadaj placówkę");
        Add7("Stranger's Request", "Stranger's Request", "Прохання незнайомця","Просьба незнакомца","Petición del extraño","Bitte des Fremden","Requête de l'étranger", "Prośba Nieznajomego");
        Add7("Gather Wood",        "Gather Wood",        "Збирай дерево",     "Собери дерево",   "Recoge madera",      "Sammle Holz",        "Récolter du bois",     "Zbierz drewno");
        Add7("Ambush!",            "Ambush!",            "Засідка!",          "Засада!",         "¡Emboscada!",        "Hinterhalt!",        "Embuscade !",          "Zasadzka!");
        Add7("Survive the Skeletons","Survive the Skeletons","Виживи проти скелетів","Выживи против скелетов","Sobrevive a los esqueletos","Überlebe die Skelette","Survivre aux squelettes","Przetrwaj szkielety");
        Add7("Escape!",            "Escape!",            "Тікай!",            "Беги!",           "¡Escapa!",           "Flieh!",             "Fuir !",               "Uciekaj!");
        Add7("REACH THE HORSE BEFORE THEY KILL YOU!",
             "REACH THE HORSE BEFORE THEY KILL YOU!",
             "ДОБІГАЙ ДО КОНЯ, ДОКИ ЇХ НЕ УБИЛИ!",
             "ДОБЕГИ ДО КОНЯ, ПОКА ТЕБЯ НЕ УБИЛИ!",
             "¡LLEGA AL CABALLO ANTES DE QUE TE MATEN!",
             "ERREICHE DAS PFERD, BEVOR SIE DICH TÖTEN!",
             "ATTEINS LE CHEVAL AVANT QU'ILS NE TE TUENT !",
             "DOTRZYJ DO KONIA, ZANIM CIĘ ZABIJĄ!");
    }

    // Prompt strings shown via GlobalHUD.ShowPrompt across the world.
    private static void SeedPrompts()
    {
        Add7("Press E to Enter Shop",   "Press E to Enter Shop",   "E — увійти в магазин", "E — войти в магазин",  "Pulsa E para entrar en la tienda","E zum Betreten des Ladens","Appuie E pour la boutique","E — wejdź do sklepu");
        Add7("Press E to Evacuate",     "Press E to Evacuate",     "E — евакуюватися",     "E — эвакуироваться",   "Pulsa E para evacuar",           "E zum Evakuieren",         "Appuie E pour évacuer",     "E — ewakuuj się");
        Add7("Press E to Open Board",   "Press E to Open Board",   "E — відкрити дошку",   "E — открыть доску",    "Pulsa E para abrir el tablero",  "E öffnet das Brett",       "Appuie E pour ouvrir le tableau","E — otwórz tablicę");
        Add7("[E] Talk to Elias",       "[E] Talk to Elias",       "[E] Говорити з Еліасом","[E] Говорить с Элиасом","[E] Hablar con Elias",         "[E] Mit Elias sprechen",    "[E] Parler à Elias",        "[E] Rozmowa z Eliasem");
        Add7("[E] Talk to Stranger",   "[E] Talk to Stranger",   "[E] Говорити з Незнайомцем","[E] Говорить с Незнакомцем","[E] Hablar con el Extraño","[E] Mit dem Fremden sprechen","[E] Parler à l'Étranger","[E] Rozmowa z Nieznajomym");
        Add7("[E] Pet Cat",             "[E] Pet Cat",             "[E] Погладити кота",   "[E] Погладить кота",   "[E] Acariciar al gato",          "[E] Katze streicheln",     "[E] Caresser le chat",      "[E] Pogłaskaj kota");
        Add7("[E] Mount Horse & Escape","[E] Mount Horse & Escape","[E] На коня і тікати","[E] На коня и бежать","[E] Montar y escapar",           "[E] Aufsteigen & Fliehen","[E] Monter et fuir",        "[E] Wsiądź i uciekaj");
        Add7("REGION CONQUERED!",       "REGION CONQUERED!",       "РЕГІОН ЗАХОПЛЕНО!",    "РЕГИОН ЗАХВАЧЕН!",     "¡REGIÓN CONQUISTADA!",           "REGION EROBERT!",          "RÉGION CONQUISE !",         "REGION PODBITY!");
        Add7("[F] PURIFY TOTEM",        "[F] PURIFY TOTEM",        "[F] ОЧИСТИТИ ТОТЕМ",   "[F] ОЧИСТИТЬ ТОТЕМ",   "[F] PURIFICAR TÓTEM",            "[F] TOTEM REINIGEN",       "[F] PURIFIER LE TOTEM",     "[F] OCZYŚĆ TOTEM");
        Add7("[F] EXECUTE",             "[F] EXECUTE",             "[F] СТРАТИТИ",         "[F] КАЗНИТЬ",          "[F] EJECUTAR",                   "[F] HINRICHTEN",           "[F] EXÉCUTER",              "[F] EGZEKUCJA");
        Add7("Upgrade Elias's Lodge first!","<color=#FF4444>Upgrade Elias's Lodge first!</color>",
             "<color=#FF4444>Спершу покращ Хатину Еліаса!</color>",
             "<color=#FF4444>Сначала улучши Хижину Элиаса!</color>",
             "<color=#FF4444>¡Primero mejora la Cabaña de Elias!</color>",
             "<color=#FF4444>Rüste zuerst Elias' Hütte auf!</color>",
             "<color=#FF4444>Améliore d'abord la Cabane d'Elias !</color>",
             "<color=#FF4444>Najpierw ulepsz Chatę Eliasa!</color>");

        // === Barracks / Mercenary System ===
        Add("MERC_BARRACKS_TITLE", "BARRACKS", "КАЗАРМА");
        Add("MERC_TAB_HIRE", "HIRE", "НАЙМ");
        Add("MERC_TAB_UPGRADE_UNITS", "UPGRADE UNITS", "ПОЛІПШИТИ ВОЯКІВ");
        Add("MERC_TAB_UPGRADE_BARRACKS", "UPGRADE BARRACKS", "ПОЛІПШИТИ КАЗАРМУ");
        Add("MERC_BTN_HIRE", "HIRE", "НАЙНЯТИ");
        Add("MERC_BTN_UPGRADE", "UPGRADE", "ПОЛІПШИТИ");
        Add("MERC_BTN_MAX", "MAX", "МАКС");
        Add("MERC_OWNED", "OWNED: {0}", "ЄСТЬ: {0}");
        Add("MERC_AVAILABLE", "Available: {0}", "Доступно: {0}");
        Add("MERC_LEVEL_XY", "LEVEL {0} / {1}", "РІВЕНЬ {0} / {1}");
        Add("MERC_MAX_LEVEL", "MAX LEVEL", "МАКС РІВЕНЬ");
        Add("MERC_PERKS_MAXED", "All barracks perks unlocked.", "Усі можливості казарми відкриті.");
        Add("MERC_LEVEL_HEADER", "LEVEL {0}", "РІВЕНЬ {0}");

        // === PreBattle / Army Deployment ===
        Add("MERC_ARMY_DEPLOYMENT", "ARMY DEPLOYMENT", "РОЗГОРТАННЯ АРМІЇ");
        Add("MERC_ENEMY_STRENGTH", "Enemy Strength: {0}", "Сила ворога: {0}");
        Add("MERC_ENEMY_POWER", "Enemy Power: {0}", "Сила ворога: {0}");
        Add("MERC_TRAVEL_TIME", "Travel Time: {0}", "Час у дорозі: {0}");
        Add("MERC_ARMY_SCORE", "Army Score: {0}", "Сила армії: {0}");
        Add("MERC_RISK_LABEL", "Risk: {0}", "Ризик: {0}");
        Add("MERC_EXPECTED_LOSSES", "Expected Losses: {0}-{1}", "Очікувані втрати: {0}-{1}");
        Add("MERC_EXPECTED_LOSSES_NONE", "Expected Losses: —", "Очікувані втрати: —");
        Add("MERC_WIN_PROBABILITY", "Win Probability", "Ймовірність перемоги");
        Add("MERC_BTN_DEPLOY", "DEPLOY ARMY", "ВІДПРАВИТИ АРМІЮ");
        Add("MERC_BTN_MARCH", "MARCH", "У ПОХІД");

        // Risk band strings
        Add("MERC_RISK_OVERWHELMING", "Overwhelming", "Переважна");
        Add("MERC_RISK_FAVOURABLE",   "Favourable",   "Вигідна");
        Add("MERC_RISK_EVEN",         "Even",         "Рівна");
        Add("MERC_RISK_RISKY",        "Risky",        "Ризикована");
        Add("MERC_RISK_SUICIDAL",     "Suicidal",     "Самогубна");

        // === Battle Result ===
        Add("MERC_VICTORY", "VICTORY", "ПЕРЕМОГА");
        Add("MERC_DEFEAT", "DEFEAT", "ПОРАЗКА");
        Add("MERC_VICTORY_TEXT", "Your army routed the defenders.", "Ваша армія розгромила захисників.");
        Add("MERC_DEFEAT_TEXT",  "Your army was broken. The region remains hostile.", "Ваша армія розбита. Регіон лишається ворожим.");
        Add("MERC_LOSSES_LINE",  "Losses: {0} / {1}", "Втрати: {0} / {1}");

        // === Unit flavour (short) ===
        Add("MERC_UNIT_MILITIA", "Militia", "Ополчення");
        Add("MERC_UNIT_RANGER",  "Ranger",  "Слідопит");
        Add("MERC_UNIT_KNIGHT",  "Knight",  "Лицар");

        // === Merc campaign toasts (Show() on start / return) ===
        Add("MERC_TOAST_DEPLOYED", "{0} units marching on {1}", "{0} воїнів вирушили на {1}");
        Add("MERC_TOAST_VICTORY",  "Victory at {0}! +◆{1}",     "Перемога в {0}! +◆{1}");
        Add("MERC_TOAST_DEFEAT",   "Defeated at {0}. {1} fell.", "Поразка в {0}. Загинуло: {1}.");

        Add("MERC_TOAST_ARMY_FULL", "Company is full ({0} units max)", "Загін повний (макс. {0} воїнів)");

        // === Campaign phase labels (CampaignStatusHUD strip) ===
        Add("MERC_PHASE_MARCHING",  "MARCHING",  "МАРШ");
        Add("MERC_PHASE_FIGHTING",  "FIGHTING",  "БИТВА");
        Add("MERC_PHASE_RETURNING", "RETURNING", "ПОВЕРНЕННЯ");

        // Tactic tooltip lines (shown in PreBattlePanel.tacticDescriptionText)
        Add("MERC_TACTIC_DESC_AMBUSH",
            "Ambush — ×0.6 travel, +8% win chance. LOSS is catastrophic (×1.6 casualties).",
            "Засідка — ×0.6 часу, +8% шанс. Поразка — катастрофа (×1.6 втрат).");
        Add("MERC_TACTIC_DESC_ASSAULT",
            "Assault — standard march. Small casualty bonus either way (×0.9 win / ×0.8 loss).",
            "Штурм — стандартний марш. Невеликий бонус до втрат (×0.9 перемога / ×0.8 поразка).");
        Add("MERC_TACTIC_DESC_SIEGE",
            "Siege — ×2 travel, +12% win chance. Engines cut casualties in half (×0.5 / ×0.6).",
            "Облога — ×2 часу, +12% шанс. Машини вдвічі скорочують втрати (×0.5 / ×0.6).");

        // === Elias dialogue lines (CampNPC_Elias.EliasDialogueRoutine) ===
        AddSelf("Elias: The Blight never sleeps. Neither should we.",
            "Еліас: Порча ніколи не спить. І ми не повинні.");
        AddSelf("Elias: Keep your blade sharp. The outlands are unforgiving.",
            "Еліас: Тримай клинок гострим. Пустки не прощають слабкості.");
        AddSelf("Elias: I smell ash on the wind today...",
            "Еліас: Сьогодні на вітрі чую попіл...");
        AddSelf("Elias: If you find any ancient scrolls out there, bring them to me.",
            "Еліас: Якщо знайдеш стародавні сувої — принеси їх мені.");
        AddSelf("Elias: Aethelgard will rise again. I feel it.",
            "Еліас: Ітельгард повстане знову. Я це відчуваю.");
        AddSelf("Elias: Listen closely. This camp won't survive on scraps forever.",
            "Еліас: Слухай уважно. Цей табір не виживе на недоїдках вічно.");
        AddSelf("Elias: The skeletons you fought? They are the cursed remains of Aethelgard's royal guard.",
            "Еліас: Скелети, з якими ти бився? Це прокляті рештки королівської варти Ітельгарду.");
        AddSelf("Elias: Centuries ago, the Ashen Blight ruined this kingdom. We must reclaim the 24 lost provinces.",
            "Еліас: Століття тому Попеляста Порча знищила це королівство. Треба повернути 24 втрачені провінції.");
        AddSelf("Elias: Build me a drafting table here later, and I will chart a safe path to the forests.",
            "Еліас: Побудуй мені креслярський стіл — і я прокладу безпечний шлях до лісів.");
        AddSelf("Elias: The new table is perfect. I've charted the first 8 regions on the map behind me.",
            "Еліас: Новий стіл — досконалий. Я вже наніс перші 8 регіонів на мапу позаду мене.");
        AddSelf("Elias: Interact with the table to plan your assaults. We need those territories back.",
            "Еліас: Взаємодій зі столом, щоб планувати штурми. Ці території треба повернути.");
        AddSelf("Elias: You survived your first conquest. I knew you had the spark.",
            "Еліас: Ти пережив своє перше завоювання. Я знав, що в тобі є іскра.");
        AddSelf("Elias: Did you notice the black ash falling in the woods? That is the physical form of the Blight.",
            "Еліас: Помітив чорний попіл, що падає у лісах? Це фізична форма Порчі.");
        AddSelf("Elias: It corrupts the land and the minds of those who fall in battle. Stay vigilant.",
            "Еліас: Вона розкладає землю й розум тих, хто гине в бою. Будь пильним.");
        AddSelf("Elias: You fight like a demon. It reminds me of the old days...",
            "Еліас: Ти б'єшся як демон. Це нагадує мені старі часи...");
        AddSelf("Elias: I wasn't always a ragged scout. I was the Chief Cartographer of Aethelgard.",
            "Еліас: Я не завжди був обірваним розвідником. Я був Головним Картографом Ітельгарду.");
        AddSelf("Elias: I drew the very borders you now bleed to reclaim. It breaks my heart to see them ruined.",
            "Еліас: Я малював ті самі кордони, за які ти зараз проливаєш кров. Серце розривається дивитися на них зруйнованими.");
        AddSelf("Elias: The alchemical lab is complete. The reagents cleared the faded ink on the parchments.",
            "Еліас: Алхімічну лабораторію завершено. Реагенти проявили вицвіле чорнило на пергаментах.");
        AddSelf("Elias: The Southern Wastes are now open to you. But beware, the heat is the least of your worries there.",
            "Еліас: Південні Пустки тепер відкриті. Але стережися — спека — найменша з проблем там.");
        AddSelf("Elias: We are pushing them back. The Blight recedes where you walk.",
            "Еліас: Ми відтісняємо їх назад. Порча відступає там, де ти йдеш.");
        AddSelf("Elias: But the deeper you go into the Wastes, the older the magic gets. Do not underestimate them.",
            "Еліас: Але чим глибше в Пустках — тим давніша магія. Не недооцінюй їх.");
        AddSelf("Elias: The astrolabe is calibrated. I can finally chart a path through the magical blizzards.",
            "Еліас: Астролябію відкалібровано. Нарешті можу прокласти шлях крізь магічні хуртовини.");
        AddSelf("Elias: The Northern Peaks are unlocked. The entire map of Aethelgard is restored.",
            "Еліас: Північні Вершини відкриті. Уся мапа Ітельгарду відновлена.");
        AddSelf("Elias: You are so close. Only the harshest lands remain.",
            "Еліас: Ти так близько. Залишились лише найсуворіші землі.");
        AddSelf("Elias: The King's personal guard fell in those mountains. They are ruthless. Prepare yourself.",
            "Еліас: Королівська особиста варта загинула в тих горах. Вони безжальні. Готуйся.");

        // === Region cinematic (RegionManager) ===
        Add("UNKNOWN REGION", "UNKNOWN REGION", "НЕВІДОМИЙ РЕГІОН");
        Add("PURIFY THE CORRUPTED TOTEMS", "PURIFY THE CORRUPTED TOTEMS", "ОЧИСТИ ЗАРАЖЕНІ ТОТЕМИ");
        Add("Press <b>SPACE</b> to Skip", "Press <b>SPACE</b> to Skip", "Натисни <b>ПРОБІЛ</b> щоб пропустити");
        Add("REGION CONQUERED", "REGION CONQUERED", "РЕГІОН ЗАВОЙОВАНО");
        Add("THE CURSE HAS BEEN LIFTED", "THE CURSE HAS BEEN LIFTED", "ПРОКЛЯТТЯ ЗНЯТО");
        Add("SLAY THE OVERLORD!", "SLAY THE OVERLORD!", "ЗНИЩ ВОЛОДАРЯ!");
        Add("SURVIVE THE SWARM!", "SURVIVE THE SWARM!", "ВИЖИВИ У РОЙОВИЩІ!");
        Add("SKELETON OVERLORD", "SKELETON OVERLORD", "СКЕЛЕТ-ВОЛОДАР");
        Add("[F] PURIFY TOTEM", "[F] PURIFY TOTEM", "[F] ОЧИСТИТИ ТОТЕМ");

        // === Extraction / codex ===
        Add("Press E to Return to Camp", "Press E to Return to Camp", "Натисни E щоб повернутися в табір");
        Add("[E] Read scroll", "[E] Read scroll", "[E] Прочитати сувій");
        Add("No scrolls recovered yet...", "No scrolls recovered yet...", "Сувоїв ще не знайдено...");
        Add("This scroll has not been recovered.", "This scroll has not been recovered.", "Цей сувій ще не знайдено.");

        // === Region rewards popup ===
        AddSelf("REGION REWARDS", "НАГОРОДИ ЗА РЕГІОН");
        AddSelf("Wood", "Дерево");
        AddSelf("Stone", "Камінь");
        AddSelf("Food", "Їжа");
        AddSelf("Diamonds", "Діаманти");
        AddSelf("Diamond", "Діамант");

        // === Level up / shop level tag / boss ===
        Add("MILESTONE_LEVEL_HP", "MILESTONE LV{0}: +10 Max HP", "РУБІЖ РІВ.{0}: +10 макс. HP");
        Add("SHOP_ITEM_LEVEL_TAG", "(Lv. {0}/{1})", "(Рів. {0}/{1})");
        Add("SHOP_NEED_MORE_DIAMONDS", "Need +{0} diamonds", "Потрібно +{0} діамантів");
        Add("PROMPT_OPEN_CHEST", "[E] Open Chest", "[E] Відкрити скриню");
        AddSelf("The Barracks is open! Walk over and press F to hire mercenaries — they'll conquer regions for you.",
            "Казарми відкриті! Підійди й натисни F, щоб найняти найманців — вони захоплять регіони замість тебе.");

        // === Armor compositional translation ===
        // Instead of registering 108 armor combinations, register the
        // WORDS. Tr's compositional fallback decomposes runtime-authored
        // asset names like "Abyssal Chestplate (Elite)" into these parts.
        // Sets
        AddSelf("Novice",     "Новачок");
        AddSelf("Mercenary",  "Найманець");
        AddSelf("Knight",     "Лицарський");
        AddSelf("Barbarian",  "Варварський");
        AddSelf("Barbarian's","Варварський");
        AddSelf("Abyssal",    "Прірвний");
        AddSelf("Paladin",    "Паладинський");
        AddSelf("Royal",      "Королівський");
        // Pieces
        AddSelf("Helm",       "Шолом");
        AddSelf("Chestplate", "Латний Нагрудник");
        AddSelf("Gauntlets",  "Наручі");
        AddSelf("Belt",       "Пояс");
        AddSelf("Greaves",    "Поножі");
        AddSelf("Boots",      "Чоботи");
        // Variants
        AddSelf("(Sturdy)",   "(Міцний)");
        AddSelf("(Elite)",    "(Елітний)");

        // === Weapon names (only 5) ===
        AddSelf("Rusty Peasant Sword",       "Іржавий Селянський Меч");
        AddSelf("Iron Oathkeeper",           "Залізний Клятводержець");
        AddSelf("Barbarian Axe",             "Варварська Сокира");
        AddSelf("Barbarian's Officer Axe",   "Офіцерська Сокира Варвара");
        AddSelf("Aethelgard's Vengeance",    "Помста Ітельгарду");

        // === Tutorial hint titles + bodies (HintData assets) ===
        AddSelf("ARMOR SLOTS", "СЛОТИ БРОНІ");
        AddSelf("Six slots: Head, Chest, Arms, Belt, Legs, Feet. Mix tiers freely — Power Score sums every equipped piece.",
            "Шість слотів: Голова, Груди, Руки, Пояс, Ноги, Стопи. Змішуй тири вільно — Power Score сумує кожен вдягнений предмет.");
        AddSelf("MELEE", "БЛИЖНІЙ БІЙ");
        AddSelf("Hold <b>LMB</b> to chain swings.", "Тримай <b>ЛКМ</b> для серії ударів.");
        AddSelf("BUILD", "БУДІВНИЦТВО");
        AddSelf("CAMP HUB", "ТАБІР");
        AddSelf("Your safe hub. Walk up to a building slot and press <b>F</b> to inspect or build. Pick missions at the Notice Board.",
            "Твоє безпечне місце. Підійди до слоту будівлі і натисни <b>F</b> для огляду чи будівництва. Місії — на Дошці оголошень.");
        AddSelf("INCOMING ATTACK", "АТАКА!");
        AddSelf("CLEARED REGION", "РЕГІОН ЗАЧИЩЕНО");
        AddSelf("This region is already purified — totems are silent. Small patrols remain for farming, but no boss waves.",
            "Цей регіон вже очищено — тотеми мовчать. Лишились малі патрулі для фарму, але без хвиль босів.");
        AddSelf("DIAMONDS", "ДІАМАНТИ");
        AddSelf("Diamonds are persistent currency. <b>Carry them out alive</b> — they're spent in the Shop on weapons, armor, and meta.",
            "Діаманти — постійна валюта. <b>Винеси їх живим</b> — вони витрачаються в Магазині на зброю, броню та мета.");
        AddSelf("ENCOUNTER CLEARED", "ГРУПУ ЗАЧИЩЕНО");
        AddSelf("Wiping a whole patrol or camp drops a bonus loot cluster. Hunt encounters between totems to stack XP and diamonds.",
            "Зачищення цілого патруля / табору кидає бонусний лут. Полюй на групи між тотемами — стакай XP і діаманти.");
        AddSelf("BLACKSMITH'S FORGE", "КУЗНЯ КОВАЛЯ");
        AddSelf("Each Forge level raises your in-mission <b>weapon damage</b>: +2% / +5% / +8% / +11% / +15%. Stacks on top of weapon stats.",
            "Кожен рівень Кузні підіймає <b>шкоду зброї</b> в місії: +2% / +5% / +8% / +11% / +15%. Множиться на статистику зброї.");
        AddSelf("GRENADE", "ГРАНАТА");
        AddSelf("HUNTER'S CABIN", "ХАТИНА МИСЛИВЦЯ");
        AddSelf("Produces <b>FOOD</b> per minute. Food is the rarest of the basic resources; upgrade the Cabin before high-tier builds.",
            "Виробляє <b>ЇЖУ</b> за хвилину. Їжа — найрідкісніший із базових ресурсів; прокачай Хатину до високих тирів.");
        AddSelf("LEVEL UP", "ПІДВИЩЕННЯ РІВНЯ");
        AddSelf("LUMBERJACK'S HUT", "ХАТА ЛІСОРУБА");
        AddSelf("Produces <b>LOGS</b> per minute, stored in the Vault. Wood is the cheapest resource — but everything costs some.",
            "Виробляє <b>КОЛОДИ</b> за хвилину, зберігаються у Схові. Дерево — найдешевший ресурс, але всюди потрібний.");
        AddSelf("WORLD MAP", "МАПА СВІТУ");
        AddSelf("STACK MULTIPLIER", "МНОЖНИК STACK");
        AddSelf("XP SHARDS", "ОСКОЛКИ ДОСВІДУ");
        AddSelf("STORAGE VAULT", "СХОВ ТАБОРУ");
        AddSelf("WEAPON UPGRADE", "ПОКРАЩЕННЯ ЗБРОЇ");
        AddSelf("ARMOR UPGRADE", "ПОКРАЩЕННЯ БРОНІ");
        AddSelf("CORRUPTED TOTEM", "ЗАРАЖЕНИЙ ТОТЕМ");
        AddSelf("BLACKSMITH'S SHOP", "МАГАЗИН КОВАЛЯ");
        AddSelf("PASSIVE INCOME", "ПАСИВНИЙ ДОХІД");
        AddSelf("Hold <b>E</b> to begin an upgrade. Resources are spent up-front. The build finishes over time — even when you're on a run.",
            "Тримай <b>E</b> для покращення. Ресурси витрачаються одразу. Будівництво завершиться з часом — навіть коли ти в поході.");
        AddSelf("Hold <b>RMB</b> to aim a grenade. Time slows while aiming. Release to throw.",
            "Тримай <b>ПКМ</b> для прицілювання гранати. Час сповільниться. Відпусти щоб кинути.");
        AddSelf("Pick one of three upgrade cards each level. Hover for the effect, click to commit.",
            "Обирай одне з трьох покращень кожного рівня. Наведи для ефекту, клацни щоб обрати.");
        AddSelf("<b>WASD</b> to move, mouse to look. Hold <b>SHIFT</b> to dash and slip past attacks.",
            "<b>WASD</b> — рух, миша — огляд. Тримай <b>SHIFT</b> для ривка й ухилення.");
        AddSelf("<b>Drag</b> to pan, scroll to zoom. Click an available region to see rewards and deploy when ready.",
            "<b>Тягни</b> для переміщення, скрол — масштаб. Клацни доступний регіон для нагород і вирушення.");
        AddSelf("Each armor piece can be levelled 0→5 in the Shop. Higher tier + level = bigger Power Score.",
            "Кожен предмет броні можна прокачати 0→5 у Магазині. Вищий тир + рівень = більший Power Score.");
        AddSelf("Raises your maximum Wood / Stone / Food capacity — otherwise resources overflow and cap at max.",
            "Підвищує макс. запас Дерева / Каменю / Їжі — інакше ресурси переливаються й обрізаються.");
        AddSelf("Spend diamonds in the Shop to level up your equipped weapon — bigger damage per swing.",
            "Витрачай діаманти в Магазині для прокачки зброї — більша шкода за удар.");
        AddSelf("Spend diamonds to unlock and upgrade gear — the higher-tier sets need Storage Vault upgrades to unlock.",
            "Витрачай діаманти на нові сети — вищі тири потребують прокачки Схову.");
        AddSelf("Elite tells are slower and hit harder. Perfect-dodge them with SHIFT to trigger a crit + slow-mo.",
            "Елітні розмахи повільніші й сильніші. Perfect-dodge (SHIFT) — крит + slow-mo.");

        // === Roadside altar (dead-end mini-boss) ===
        Add("PROMPT_ACTIVATE_ALTAR", "[F] Activate Ancient Altar", "[F] Активувати Стародавній Вівтар");
        Add("TOAST_ALTAR_PURIFIED", "Altar purified! +◆{0}", "Вівтар очищено! +◆{0}");

        // === Tutorial hint fallback bodies (TutorialHints wraps through Tr) ===
        AddSelf("TIP", "ПОРАДА");
        AddSelf("Each level lets you pick one of three upgrades. Hover a card to read its effect, click to commit.",
            "Кожен рівень дає вибір одного з трьох покращень. Наведи на картку, щоб прочитати ефект, клацни щоб обрати.");
        AddSelf("Diamonds are persistent currency. Carry them out alive — they're spent in the Shop on weapons, armor, and meta-upgrades.",
            "Діаманти — постійна валюта. Винеси їх живим — вони витрачаються в Магазині на зброю, броню та мета-покращення.");
        AddSelf("Stand on the corrupted totem and press <b>F</b> to purify it. A wave of enemies will spawn — survive to claim the region.",
            "Стань на заражений тотем і натисни <b>F</b> для очищення. З'явиться хвиля ворогів — виживи, щоб захопити регіон.");
        AddSelf("Activating a totem summons a wave. Defeat <b>every</b> enemy to purify it — the next totem unlocks afterward.",
            "Активація тотема викликає хвилю. Знищ <b>усіх</b> ворогів для очищення — після цього відкриється наступний тотем.");
        AddSelf("TIP: red flash on an enemy = incoming attack. DASH (Space) through it to dodge.",
            "ПОРАДА: червоний спалах на ворогові = атака. РИВОК (Space) крізь неї — ухилення.");
        AddSelf("Welcome to camp — your safe hub. Walk up to a building slot and press <b>F</b> to inspect or build. Pick missions at the Notice Board.",
            "Вітаємо в таборі — твоєму безпечному хабі. Підійди до будівлі й натисни <b>F</b> для огляду чи будівництва. Місії — на Дошці оголошень.");
        AddSelf("WASD to move, mouse to look. Hold <b>SHIFT</b> to dash and slip past attacks.",
            "WASD — рух, миша — огляд. Тримай <b>SHIFT</b> для ривка й прослизай повз атаки.");
        AddSelf("Hold <b>G</b> to aim a grenade — releases when you let go. Slows time while aiming.",
            "Тримай <b>G</b> для прицілювання гранати — кидок при відпусканні. Час сповільнюється під час прицілювання.");
        AddSelf("STACK = enemies near you. At 15+ you start dealing multiplied damage. At 30+ you become a typhoon — but you also lose acceleration.",
            "STACK = вороги поруч. Від 15+ шкода множиться. Від 30+ ти тайфун — але втрачаєш прискорення.");
        AddSelf("ELITE windup detected. Dash (<b>SHIFT</b>) right as their flash peaks to trigger Perfect Dodge — guaranteed crit + slow-mo.",
            "ЕЛІТНИЙ замах! Ривок (<b>SHIFT</b>) у пік спалаху = Ідеальне ухилення — гарантований крит + слоу-мо.");
        AddSelf("Hold <b>LMB</b> to chain melee swings. Killing enemies grows the STACK — every 15 stacks adds a damage multiplier.",
            "Тримай <b>ЛКМ</b> для серії ударів. Вбивства ростять STACK — кожні 15 стаків додають множник шкоди.");
        AddSelf("Spend diamonds to unlock and upgrade weapons & armor. Higher tiers boost your Power Score, which gates harder regions.",
            "Витрачай діаманти на зброю та броню. Вищі тири підіймають Power Score, який відкриває складніші регіони.");
        AddSelf("Enemies drop XP shards. Fill the XP bar to level up and pick a new upgrade.",
            "Вороги лишають осколки досвіду. Заповни шкалу XP, щоб підняти рівень і обрати покращення.");
        AddSelf("Drag to pan, scroll to zoom. Click an available region to see its rewards and deploy when ready.",
            "Тягни для переміщення, скрол — масштаб. Клацни доступний регіон, щоб побачити нагороди й вирушити.");
        AddSelf("Cleared encounter — bonus loot dropped at the camp center. Wipe more groups to stack rewards.",
            "Групу зачищено — бонусний лут у центрі табору. Знищуй більше груп, щоб нагороди стакались.");

        // === Tutorial hint bodies for newly-added mechanics ===
        AddSelf("Pick units + a tactic. Ambush is fast + risky, Assault is balanced, Siege is slow + safer. Win chance updates live.",
            "Обирай воїнів + тактику. Засідка — швидка + ризикова, Штурм — збалансована, Облога — повільна + безпечніша. Шанс перемоги оновлюється вживу.");
        AddSelf("Roadside altars summon a mini-boss on activation. Defeat it for a diamond + XP bonus. Optional but tempting.",
            "Придорожні вівтарі викликають міні-боса. Здолай його для бонусних діамантів + XP. Опційно, але спокусливо.");
        AddSelf("A staggered boss can be executed with F. A short cinematic + free kill. Do it whenever the prompt appears.",
            "Оглушеного боса можна добити натиском F. Коротка катсцена + безкоштовне вбивство. Роби це щоразу як з'являється підказка.");
        AddSelf("Your army returned victorious! The region flips to Conquered, its neighbours unlock, and you can send new campaigns.",
            "Твоя армія повернулася з перемогою! Регіон стає Захопленим, сусіди відкриваються, можеш відправляти нові кампанії.");
        AddSelf("Your army was defeated — fallen units are gone for good (permadeath). Hire replacements at the Barracks and try again.",
            "Твою армію переможено — загиблі воїни втрачені назавжди (пермадеус). Найми нових у Казармах і спробуй знову.");
        AddSelf("Region cleared! Its neighbours are now Available. Chain conquests outward — the map opens as you go.",
            "Регіон зачищено! Його сусіди тепер Доступні. Захоплюй ланцюгом назовні — мапа розкривається по мірі просування.");
        AddSelf("The Storage Vault raises your max Wood / Stone / Food capacity. Upgrade it BEFORE big builds so nothing overflows.",
            "Схов Табору підіймає макс. запас Дерева / Каменю / Їжі. Прокачай його ДО великих будов, щоб ресурси не переливались.");
        AddSelf("The Hunter's Cabin produces FOOD per minute — the rarest basic resource. Prioritise it before high-tier builds.",
            "Хатина Мисливця виробляє ЇЖУ за хвилину — найрідкісніший базовий ресурс. Пріоритезуй її перед високими тирами.");
        AddSelf("The Lumberjack's Hut generates LOGS per minute. Cheapest resource but every build needs some.",
            "Хата Лісоруба виробляє КОЛОДИ за хвилину. Найдешевший ресурс, але кожна будова щось потребує.");
        AddSelf("The Forge boosts your in-mission weapon damage by up to +15% at max level. Stacks with weapon tier.",
            "Кузня підіймає шкоду зброї в місії до +15% на макс. рівні. Множиться на тир зброї.");

        // === Achievements ===
        Add("ACHIEVEMENT_UNLOCKED", "Achievement unlocked: {0}", "Досягнення відкрито: {0}");
        AddSelf("ACHIEVEMENT UNLOCKED", "ДОСЯГНЕННЯ ВІДКРИТО");
        AddSelf("First Steps",              "Перші кроки");
        AddSelf("Homestead",                "Домівка");
        AddSelf("Scout's Map",              "Мапа Розвідника");
        AddSelf("First Blood",              "Перша кров");
        AddSelf("Supply Lines",             "Постачання");
        AddSelf("For Hire",                 "На службу");
        AddSelf("March of War",             "Марш війни");
        AddSelf("Veterans",                 "Ветерани");
        AddSelf("Strategist",               "Стратег");
        AddSelf("Halfway",                  "На півдорозі");
        AddSelf("Altar Hunter",             "Мисливець на Вівтарі");
        AddSelf("Executioner",              "Кат");
        AddSelf("The Shopkeeper's Friend",  "Друг Крамаря");
        AddSelf("Untouchable",              "Невразливий");
        AddSelf("Blood in the Air",         "Кров у повітрі");
        AddSelf("City Siege",               "Облога міста");
        AddSelf("The Throne Taken",         "Трон здобуто");
        AddSelf("Kingdom Restored",         "Королівство відроджено");
        AddSelf("Lore Master",              "Знавець Легенд");
        AddSelf("Deep Pockets",             "Глибокі кишені");

        // Achievement descriptions (English == key). One-line each.
        AddSelf("Complete the tutorial.",                            "Пройди навчання.");
        AddSelf("Return to the camp for the first time.",            "Вперше повернись до табору.");
        AddSelf("Upgrade the Scout's Lodge to level 2.",             "Покращ Хижу Розвідника до 2 рівня.");
        AddSelf("Conquer your first region.",                        "Захопи свій перший регіон.");
        AddSelf("Build the Storage Vault.",                          "Збудуй Сховище.");
        AddSelf("Hire your first mercenary.",                        "Найми першого найманця.");
        AddSelf("Send your first army on a campaign.",               "Відправ першу армію в похід.");
        AddSelf("Fill your entire mercenary roster (5 units).",      "Наповни весь склад найманців (5 юнітів).");
        AddSelf("Win an auto-battle with a Siege tactic.",           "Виграй авто-бій з тактикою Облога.");
        AddSelf("Conquer 12 regions.",                               "Захопи 12 регіонів.");
        AddSelf("Purify a roadside altar.",                          "Очисти придорожній вівтар.");
        AddSelf("Perform a Glory Kill on a boss.",                   "Виконай Славне Вбивство над босом.");
        AddSelf("Spend 500 diamonds in the Shop.",                   "Витрать 500 діамантів у Крамниці.");
        AddSelf("Land a Perfect Dodge.",                             "Виконай Ідеальний Ухил.");
        AddSelf("Reach a 15-enemy Stack.",                           "Досягни стеку з 15 ворогів.");
        AddSelf("Conquer the Citadel Outskirts.",                    "Захопи Околиці Цитаделі.");
        AddSelf("Defeat the Overlord in the Throne Room.",           "Перемогти Володаря в Тронній залі.");
        AddSelf("Conquer every region in Aethelgard.",               "Захопи кожен регіон Ітельгарду.");
        AddSelf("Recover 5 lore scrolls.",                           "Відшукай 5 свитків легенд.");
        AddSelf("Hoard 2000 diamonds at once.",                      "Накопич 2000 діамантів одночасно.");

        // === Level-up upgrades (names, flavor descriptions, stat lines) ===
        // Each upgrade is a 3-string set. The English literal is the loc
        // key. Registered via AddSelf so a UK player sees translations,
        // an EN player sees the authored source.
        AddSelf("Vitality Reserves",                                                  "Резерви Витривалості");
        AddSelf("Forged sinew. Each layer means another swing you outlast.",          "Загартовані жили. Кожен шар — ще один удар, який ти переживеш.");
        AddSelf("+10 Max HP",                                                         "+10 макс. HP");
        AddSelf("Vanguard March",                                                     "Хода Авангарду");
        AddSelf("Lighter step, longer stride. The blade always arrives first.",       "Легший крок, довший розмах. Клинок завжди прибуває першим.");
        AddSelf("+0.5 Speed",                                                         "+0.5 швидкості");
        AddSelf("Siege Might",                                                        "Облогова Сила");
        AddSelf("The hammer drinks deeper. Bones break at half the effort.",          "Молот п'є глибше. Кістки ламаються з півзусилля.");
        AddSelf("+5 Damage",                                                          "+5 шкоди");
        AddSelf("Crystal Lure",                                                       "Приманка Кристалів");
        AddSelf("Aether shards leap toward you from farther afield.",                 "Осколки Ефіру летять до тебе з більшої відстані.");
        AddSelf("+0.5 Pickup Range",                                                  "+0.5 радіусу підбирання");
        AddSelf("Whetstone Rhythm",                                                   "Ритм Точила");
        AddSelf("The swing-arc tightens. More strikes per breath.",                   "Дуга удару стискається. Більше ударів на подих.");
        AddSelf("+15 Atk Speed",                                                      "+15 швидкості атаки");
        AddSelf("Aethelgard Plate",                                                   "Броня Ітельгарду");
        AddSelf("Damp the next blow with old steel and older oaths.",                 "Приглуш наступний удар старою сталлю і ще старішими клятвами.");
        AddSelf("+5% Damage Resist",                                                  "+5% опору шкоди");
        AddSelf("Field Medicine",                                                     "Польова Медицина");
        AddSelf("Slow knit, but knit it does. Health returns with every footfall.",   "Повільно, але зростається. Здоров'я повертається з кожним кроком.");
        AddSelf("+0.3 HP/sec",                                                        "+0.3 HP/сек");
        AddSelf("Keen Eye",                                                           "Гостре Око");
        AddSelf("You read where bone is brittle. Strikes find the weak point oftener.","Ти бачиш, де кістка тендітна. Удари частіше знаходять слабке місце.");
        AddSelf("+5% Crit Chance",                                                    "+5% шансу криту");
        AddSelf("Executioner's Edge",                                                 "Лезо Ката");
        AddSelf("When the blade bites true, it bites deeper.",                        "Коли клинок кусає точно — кусає глибше.");
        AddSelf("+25% Crit Damage",                                                   "+25% крит-шкоди");
        AddSelf("Bloodbound Pact",                                                    "Кривавий Пакт");
        AddSelf("Every wound you deliver feeds you back a sip.",                      "Кожна завдана рана повертає тобі ковток.");
        AddSelf("+5% Lifesteal",                                                      "+5% викрадання життя");
        AddSelf("Wind-Touched",                                                       "Торкнутий Вітром");
        AddSelf("The air parts before you. Some blows pass through nothing.",         "Повітря розступається перед тобою. Деякі удари проходять крізь порожнечу.");
        AddSelf("+5% Dodge",                                                          "+5% ухилу");
        AddSelf("Reaver's Reward",                                                    "Нагорода Розбійника");
        AddSelf("Each kill stitches another scar shut.",                              "Кожне вбивство зашиває ще один шрам.");
        AddSelf("+3 HP per Kill",                                                     "+3 HP за вбивство");
        AddSelf("Wardbreaker Sigil",                                                  "Печатка Руйнівника Вартового");
        AddSelf("Those who strike you bleed for the privilege.",                      "Ті, хто б'ють тебе — платять за цю честь кров'ю.");
        AddSelf("+15% Thorns",                                                        "+15% шипів");
        AddSelf("Soulreader",                                                         "Читач Душ");
        AddSelf("You hear the song each fallen soul carries. Learn faster.",          "Ти чуєш пісню, яку несе кожна полегла душа. Вчишся швидше.");
        AddSelf("+15% XP Gain",                                                       "+15% отримання досвіду");
        AddSelf("Hoarder's Gaze",                                                     "Погляд Скнари");
        AddSelf("Aether shards spill heavier where you walk.",                        "Осколки Ефіру сиплються рясніше там, де ти йдеш.");
        AddSelf("+20% Diamond Gain",                                                  "+20% приросту діамантів");

        // === Credits body (prose lines shown between headers) ===
        AddSelf("Horizont Studio",                                          "Студія Horizont");
        AddSelf("Hollow Siege / Aethelgard",                                "Hollow Siege / Ітельгард");
        AddSelf("Game Design, Programming, Level Design",                   "Гейм-дизайн, програмування, дизайн рівнів");
        AddSelf("3D Models & Environment",                                  "3D-моделі та оточення");
        AddSelf("FMOD Studio by Firelight Technologies",                    "FMOD Studio від Firelight Technologies");
        AddSelf("English · Ukrainian",                                      "Англійська · Українська");
        AddSelf("© 2026 Horizont Studio. All rights reserved.",             "© 2026 Студія Horizont. Всі права захищено.");

        // === Compass cardinals (MapAAAEnhancer rose) ===
        Add("COMPASS_N", "N", "Пн");
        Add("COMPASS_S", "S", "Пд");
        Add("COMPASS_E", "E", "Сх");
        Add("COMPASS_W", "W", "Зх");

        // === Main menu extended ===
        Add("MENU_CONFIRM_NEW_GAME",
            "Start a new game?\n\nAll camp progress, conquered regions and mercenaries will be lost. Settings + shop unlocks are kept.",
            "Почати нову гру?\n\nВесь прогрес табору, захоплені регіони й найманці будуть втрачені. Налаштування й розблоковане в магазині залишиться.");
        Add("MENU_CONFIRM_QUIT",
            "Quit the game?",
            "Вийти з гри?");

        // === Credits ===
        Add("CREDITS_HEADER_STUDIO",       "STUDIO",             "СТУДІЯ");
        Add("CREDITS_HEADER_DESIGN",       "DESIGN & CODE",      "ДИЗАЙН І КОД");
        Add("CREDITS_HEADER_ART",          "ART",                "АРТ");
        Add("CREDITS_HEADER_ENGINE",       "ENGINE",             "РУШІЙ");
        Add("CREDITS_HEADER_AUDIO",        "AUDIO",              "АУДІО");
        Add("CREDITS_HEADER_ASSETS",       "ASSETS & PLUGINS",   "АССЕТИ ТА ПЛАГІНИ");
        Add("CREDITS_HEADER_LOCALISATION", "LOCALISATION",       "ЛОКАЛІЗАЦІЯ");
        Add("CREDITS_HEADER_THANKS",       "SPECIAL THANKS",     "ОСОБЛИВА ПОДЯКА");
        Add("CREDITS_HEADER_COPYRIGHT",    "COPYRIGHT",          "АВТОРСЬКІ ПРАВА");
        Add("CREDITS_THANKS_LINE",         "To everyone who tested, played, and believed.",
            "Всім, хто тестував, грав і вірив.");
        Add("CREDITS_END_TAGLINE",         "Aethelgard remembers.",
            "Ітельгард пам'ятає.");

        // === Ending narration ===
        Add("ENDING_LINE_1",
            "The Overlord falls. The last echo of the Blight fades from the throne.",
            "Володар падає. Останнє відлуння Порчі згасає з трону.");
        Add("ENDING_LINE_2",
            "For centuries the mist rose from these halls. Now, only silence.",
            "Століттями туман здіймався з цих залів. Тепер — лиш тиша.");
        Add("ENDING_LINE_3",
            "In the villages you rebuilt, first light breaks through the smoke.",
            "У селищах, які ти відбудував, перше світло пробивається крізь дим.");
        Add("ENDING_LINE_FINAL",
            "You have restored Aethelgard.",
            "Ти відродив Ітельгард.");

        // === Mission element status tag ===
        Add("MISSION_DONE_TAG", "DONE", "ЗРОБЛЕНО");

        // === Region UI misc ===
        Add("REGION_UNKNOWN_LABEL", "???", "???");

        // === Mercenary unit names (ScriptableObject displayName defaults) ===
        AddSelf("Militia", "Ополчення");
        AddSelf("Ranger",  "Слідопит");
        AddSelf("Knight",  "Лицар");
        // Common merc SO flavour lines. If your unit data uses different
        // English text, extend the table with the same AddSelf pattern.
        AddSelf("Cheap conscripts armed with pitchforks.",
                "Дешеві ополченці, озброєні вилами.");
        AddSelf("Skirmishers who peel back enemy lines.",
                "Стрільці, що розривають ворожі лави.");
        AddSelf("Armored elites — few in number, huge in impact.",
                "Броньована еліта — небагато числом, але грізні у бою.");

        // === Combat prompts ===
        AddSelf("[F] EXECUTE", "[F] ДОБИВАННЯ");

        // === Camp onboarding guide (CampGuideDirector.promptKey) ===
        Add("GUIDE_TALK_ELIAS",         "Talk to Elias",                        "Поговори з Еліасом");
        Add("GUIDE_BUILD_LODGE",        "Upgrade the Scout's Lodge",            "Прокачай Хатину Розвідника");
        Add("GUIDE_USE_MAP_TABLE",      "Open the Map Table (press E)",          "Відкрий Мапу (натисни E)");
        Add("GUIDE_CONQUER_FIRST",      "Conquer your first region",            "Захопи свій перший регіон");
        Add("GUIDE_BUILD_BARRACKS",     "Build the Barracks",                   "Побудуй Казарми");
        Add("GUIDE_HIRE_MERCS",         "Hire a mercenary at the Barracks",     "Найми найманця в Казармах");
        Add("GUIDE_SEND_ARMY",          "Send an army to an auto-battle region", "Пошли армію в регіон авто-битви");
        Add("GUIDE_TALK_ELIAS_AGAIN",   "Return to Elias — he has news",         "Повернися до Еліаса — у нього новини");
        Add("GUIDE_STEP_DONE",          "Objective complete!",                   "Ціль виконано!");
        Add("GUIDE_PLATE_TITLE",        "Camp Task",                             "Завдання табору");
        Add("GUIDE_BUILD_STORAGE",      "Build the Storage Vault",               "Побудуй Схов Табору");
        Add("GUIDE_NOTICE_BOARD",       "Check the Notice Board for missions",   "Перевір Дошку Оголошень");
        Add("GUIDE_HIRE_MERC",          "Hire your first mercenary",             "Найми свого першого найманця");
        Add("GUIDE_SEND_ARMY",          "Send an army to an auto-battle region", "Пошли армію в регіон авто-битви");
        Add("GUIDE_VISIT_SHOP",         "Visit the Shop and upgrade your gear",  "Відвідай Магазин і покращ спорядження");
        Add("GUIDE_MIDGAME_REGION",     "Push deeper — conquer the Sunken Outpost", "Просувайся далі — захопи Затоплений Форпост");
        Add("GUIDE_REACH_CITY",         "March on the Citadel Outskirts",         "Виступай на Околиці Цитаделі");
        Add("GUIDE_FINAL_PUSH",         "Storm the Throne Room — end the Blight", "Штурмуй Тронну Залу — покінчи з Порчею");

        // === Graphics auto-detect (wire a button to GraphicsAutoConfig.DetectAndApply) ===
        Add("SETTINGS_AUTODETECT",        "Auto-Detect (Recommended)", "Авто-визначення (рекомендовано)");
        Add("SETTINGS_TIER_APPLIED",      "Graphics set to: {0}",      "Графіку встановлено на: {0}");
        Add("SETTINGS_TIER_LOW",          "Low",                       "Низька");
        Add("SETTINGS_TIER_MEDIUM",       "Medium",                    "Середня");
        Add("SETTINGS_TIER_HIGH",         "High",                      "Висока");
        Add("SETTINGS_TIER_ULTRA",        "Ultra",                     "Ультра");

        // === World interaction prompts (F/E keys near objects) ===
        Add("PROMPT_INSPECT_BUILDING", "[F] Inspect {0}", "[F] Огляд: {0}");
        Add("PROMPT_OPEN_MAP", "[E] Open Map", "[E] Відкрити мапу");
        Add("PROMPT_OPEN_BOARD", "Press E to Open Board", "Натисни E щоб відкрити дошку");
        Add("PROMPT_TALK_STRANGER", "[E] Talk to Stranger", "[E] Поговорити з незнайомцем");
        Add("PROMPT_PET_CAT", "[E] Pet Cat", "[E] Погладити кота");
        Add("PROMPT_EVACUATE", "Press E to Evacuate", "Натисни E щоб евакуюватися");
        Add("PROMPT_MOUNT_HORSE", "[E] Mount Horse & Escape", "[E] Осідлати коня і втекти");
        Add("PROMPT_UPGRADE_ELIAS_FIRST",
            "<color=#FF4444>Upgrade Elias's Lodge first!</color>",
            "<color=#FF4444>Спершу покращ Хатину Еліаса!</color>");

        // === Building upgrade toast ===
        Add("TOAST_BUILDING_UPGRADED", "{0} UPGRADED!", "{0} ПОЛІПШЕНО!");

        // === Camp building CampBuilding.cs hardcoded texts ===
        Add("CB_UNBUILT_LABEL", "(Unbuilt)", "(Ще не збудовано)");
        Add("CB_LEVEL_LABEL", "(Level {0})", "(Рівень {0})");
        Add("CB_MAX_LEVEL", "Max Level Reached", "Досягнуто максимального рівня");
        Add("CB_PRODUCTION_LABEL", "Production", "Виробництво");
        Add("CB_FEATURE_LABEL", "Feature", "Особливість");
        Add("CB_BUILD_TIME", "Build Time: {0}s", "Час: {0} с");
        Add("CB_UPGRADE_TIME", "Upgrade Time: {0}s", "Час поліпшення: {0} с");

        // === MapPanelUI ===
        Add("MAP_CONFIRM_UPGRADE", "<color=#FFD700>CONFIRM</color>", "<color=#FFD700>ПІДТВЕРДИТИ</color>");

        // === LoreCodex ===
        Add("LORE_EMPTY_TITLE", "—", "—");

        // === Tutorial hints ===
        Add("TUTORIAL_TIP_DEFAULT", "TIP", "ПОРАДА");
        // NPC talk prompts
        Add("PROMPT_TALK_ELIAS", "[E] Talk to Elias", "[E] Поговорити з Еліасом");
        Add("PROMPT_ENTER_SHOP", "Press E to Enter Shop", "Натисни E щоб зайти в крамницю");

        // Hint titles + bodies (extend with Add7 for RU/ES/DE/FR/PL)
        Add("ARMOR SLOTS", "ARMOR SLOTS", "СЛОТИ БРОНІ");
        Add("Six slots: Head, Chest, Arms, Belt, Legs, Feet. Mix tiers freely — Power Score sums every equipped piece.",
            "Six slots: Head, Chest, Arms, Belt, Legs, Feet. Mix tiers freely — Power Score sums every equipped piece.",
            "Шість слотів: голова, груди, руки, пояс, ноги, ступні. Змішуй тіри вільно — рейтинг сили сумує кожен вдягнений предмет.");
        Add("MELEE", "MELEE", "БЛИЖНІЙ БІЙ");
        Add("Hold <b>LMB</b> to chain swings.", "Hold <b>LMB</b> to chain swings.", "Утримуй <b>ЛКМ</b> щоб зчіплювати удари.");
        Add("BUILD", "BUILD", "БУДІВНИЦТВО");
        Add("CAMP HUB", "CAMP HUB", "ТАБІР");
        Add("Your safe hub. Walk up to a building slot and press <b>F</b> to inspect or build. Pick missions at the Notice Board.",
            "Your safe hub. Walk up to a building slot and press <b>F</b> to inspect or build. Pick missions at the Notice Board.",
            "Твій безпечний хаб. Підійди до слоту будівлі та натисни <b>F</b> щоб оглянути чи будувати. Місії — на Дошці.");
        Add("INCOMING ATTACK", "INCOMING ATTACK", "АТАКА");
        Add("CLEARED REGION", "CLEARED REGION", "ЗАЧИЩЕНИЙ РЕГІОН");
        Add("This region is already purified — totems are silent. Small patrols remain for farming, but no boss waves.",
            "This region is already purified — totems are silent. Small patrols remain for farming, but no boss waves.",
            "Цей регіон вже очищений — тотеми мовчать. Лишились невеликі патрулі для фарму, босів немає.");
        Add("DIAMONDS", "DIAMONDS", "ДІАМАНТИ");
        Add("Diamonds are persistent currency. <b>Carry them out alive</b> — they're spent in the Shop on weapons, armor, and meta.",
            "Diamonds are persistent currency. <b>Carry them out alive</b> — they're spent in the Shop on weapons, armor, and meta.",
            "Діаманти — стала валюта. <b>Винеси їх живим</b> — витрачаються у Крамниці на зброю, броню і мета.");
        Add("ENCOUNTER CLEARED", "ENCOUNTER CLEARED", "ЗІТКНЕННЯ ЗАЧИЩЕНО");
        Add("Wiping a whole patrol or camp drops a bonus loot cluster. Hunt encounters between totems to stack XP and diamonds.",
            "Wiping a whole patrol or camp drops a bonus loot cluster. Hunt encounters between totems to stack XP and diamonds.",
            "Повне знищення патруля чи табору дає бонусний скарб. Полюй на зіткнення між тотемами щоб накопичувати XP і діаманти.");
        Add("BLACKSMITH'S FORGE", "BLACKSMITH'S FORGE", "КУЗНЯ КОВАЛЯ");
        Add("Each Forge level raises your in-mission <b>weapon damage</b>: +2% / +5% / +8% / +11% / +15%. Stacks on top of weapon stats.",
            "Each Forge level raises your in-mission <b>weapon damage</b>: +2% / +5% / +8% / +11% / +15%. Stacks on top of weapon stats.",
            "Кожен рівень Кузні підвищує <b>урон зброї</b> у місіях: +2% / +5% / +8% / +11% / +15%. Стакається зі статами зброї.");
        Add("GRENADE", "GRENADE", "ГРАНАТА");
        Add("HUNTER'S CABIN", "HUNTER'S CABIN", "ХАТА МИСЛИВЦЯ");
        Add("Produces <b>FOOD</b> per minute. Food is the rarest of the basic resources; upgrade the Cabin before high-tier builds.",
            "Produces <b>FOOD</b> per minute. Food is the rarest of the basic resources; upgrade the Cabin before high-tier builds.",
            "Виробляє <b>ЇЖУ</b> щохвилини. Їжа — найрідкісніший з базових ресурсів; поліпш Хату перед високотірними будівлями.");
        Add("LEVEL UP", "LEVEL UP", "НОВИЙ РІВЕНЬ");
        Add("LUMBERJACK'S HUT", "LUMBERJACK'S HUT", "ХАТА ЛІСОРУБА");
        Add("Produces <b>LOGS</b> per minute, stored in the Vault. Wood is the cheapest resource — but everything costs some.",
            "Produces <b>LOGS</b> per minute, stored in the Vault. Wood is the cheapest resource — but everything costs some.",
            "Виробляє <b>КОЛОДИ</b> щохвилини, зберігаються у Сховищі. Дерево — найдешевший ресурс, але для всього трохи треба.");
        Add("WORLD MAP", "WORLD MAP", "СВІТОВА МАПА");
        Add("MOVEMENT", "MOVEMENT", "РУХ");
        Add("PASSIVE INCOME", "PASSIVE INCOME", "ПАСИВНИЙ ДОХІД");
        Add("Buildings produce resources while you're playing missions or away from the camp. Check the panel for current rate.",
            "Buildings produce resources while you're playing missions or away from the camp. Check the panel for current rate.",
            "Будівлі виробляють ресурси поки ти в місіях або поза табором. Перевір панель для поточної швидкості.");
        Add("PERFECT DODGE", "PERFECT DODGE", "ІДЕАЛЬНИЙ УХИЛ");
        Add("POWER SCORE", "POWER SCORE", "РЕЙТИНГ СИЛИ");
        Add("Power = your weapon + armor + meta. Regions show a Recommended Power. Below it: enemies hit harder. Above it: easier, lower XP.",
            "Power = your weapon + armor + meta. Regions show a Recommended Power. Below it: enemies hit harder. Above it: easier, lower XP.",
            "Сила = зброя + броня + мета. Регіони показують Рекомендовану Силу. Нижче — вороги б'ють сильніше. Вище — легше, менше XP.");
        Add("DEFEND THE TOTEM", "DEFEND THE TOTEM", "ЗАХИСТИ ТОТЕМ");
        Add("Activating a totem summons a wave. Defeat <b>every</b> enemy to purify it. The wave can't be skipped.",
            "Activating a totem summons a wave. Defeat <b>every</b> enemy to purify it. The wave can't be skipped.",
            "Активація тотема викликає хвилю. Переможи <b>кожного</b> ворога щоб очистити тотем. Хвилю не можна пропустити.");
        Add("OBJECTIVE BEACON", "OBJECTIVE BEACON", "МАЯК ЦІЛІ");
        Add("The tall red pillar marks the next corrupted totem — visible across the whole region. Run toward it.",
            "The tall red pillar marks the next corrupted totem — visible across the whole region. Run toward it.",
            "Високий червоний стовп позначає наступний зіпсований тотем — видно з усього регіону. Біжи до нього.");

        // === Region names + lore (24 regions) ===
        Add("Old Lumberyard", "Old Lumberyard", "Стара лісопилка");
        Add("An abandoned camp where woodcutters once thrived. Now, only restless bones remain among the logs.",
            "An abandoned camp where woodcutters once thrived. Now, only restless bones remain among the logs.",
            "Покинутий табір лісорубів. Тепер серед колод лишились самі неспокійні кістки.");
        Add("Whispering Thicket", "Whispering Thicket", "Шепітний ліс");
        Add("The trees here absorb the moonlight, making it dangerously dark. Beware of ambushes.",
            "The trees here absorb the moonlight, making it dangerously dark. Beware of ambushes.",
            "Дерева тут поглинають місячне світло — небезпечно темно. Стережись засідок.");
        Add("Bandit's Crossing", "Bandit's Crossing", "Розбійницьке перепуття");
        Add("A broken bridge heavily guarded by corrupted scavengers. A great source of basic materials.",
            "A broken bridge heavily guarded by corrupted scavengers. A great source of basic materials.",
            "Зламаний міст під охороною зіпсутих падл. Гарне джерело базових матеріалів.");
        Add("Forgotten Shrine", "Forgotten Shrine", "Забутий храм");
        Add("An overgrown statue of a nameless god. The enemies here are slightly more aggressive.",
            "An overgrown statue of a nameless god. The enemies here are slightly more aggressive.",
            "Зарослий статуя безіменного бога. Вороги тут трохи агресивніші.");
        Add("Mossy Foothills", "Mossy Foothills", "Мохові передгір'я");
        Add("The forest begins to thin out, revealing rocky terrain. Stone is easier to find here.",
            "The forest begins to thin out, revealing rocky terrain. Stone is easier to find here.",
            "Ліс рідшає, відкриваючи скелястий рельєф. Тут легше знайти камінь.");
        Add("Ruined Tollkeep", "Ruined Tollkeep", "Зруйнована митниця");
        Add("The border between the forest and the old kingdom. Heavily defended by skeleton guards.",
            "The border between the forest and the old kingdom. Heavily defended by skeleton guards.",
            "Кордон між лісом і старим королівством. Пильно охороняється скелетами.");
        Add("Stonefall Quarry", "Stonefall Quarry", "Каменепадне кар'єр");
        Add("Deep pits where slaves once mined stone. The undead miners still blindly swing their pickaxes.",
            "Deep pits where slaves once mined stone. The undead miners still blindly swing their pickaxes.",
            "Глибокі ями де раби добували камінь. Мертві шахтарі досі сліпо махають кирками.");
        Add("Sunken Outpost", "Sunken Outpost", "Затоплений форпост");
        Add("A flooded military camp. Movement is slightly impaired, and enemies hit harder.",
            "A flooded military camp. Movement is slightly impaired, and enemies hit harder.",
            "Затоплений військовий табір. Рухи трохи скуті, вороги б'ють сильніше.");
        Add("Howling Valley", "Howling Valley", "Виюча долина");
        Add("The wind through this canyon sounds like screaming. The cursed souls here are relentless.",
            "The wind through this canyon sounds like screaming. The cursed souls here are relentless.",
            "Вітер у каньйоні звучить як крик. Прокляті душі тут невтомні.");
        Add("The Ashen Woods", "The Ashen Woods", "Попелясті ліси");
        Add("A forest burned down by dragon fire centuries ago. Resources are scarce but valuable.",
            "A forest burned down by dragon fire centuries ago. Resources are scarce but valuable.",
            "Ліс, спалений драконовим вогнем сторіччя тому. Ресурсів мало — але цінних.");
        Add("Ironpeak Pass", "Ironpeak Pass", "Залізовершинний перевал");
        Add("A dangerous mountain road. You'll need decent armor and a sharp blade to survive the swarm.",
            "A dangerous mountain road. You'll need decent armor and a sharp blade to survive the swarm.",
            "Небезпечний гірський шлях. Потрібна пристойна броня і гострий клинок щоб пережити рій.");
        Add("Deadman's Gorge", "Deadman's Gorge", "Ущелина мертвяка");
        Add("A massive graveyard of fallen knights. Their rusted armor makes them tougher to kill.",
            "A massive graveyard of fallen knights. Their rusted armor makes them tougher to kill.",
            "Величезний цвинтар полеглих лицарів. Іржава броня робить їх складнішими для вбивства.");
        Add("Smuggler's Cove", "Smuggler's Cove", "Бухта контрабандистів");
        Add("Hidden caches of stolen goods remain here, guarded by the ghosts of greedy mercenaries.",
            "Hidden caches of stolen goods remain here, guarded by the ghosts of greedy mercenaries.",
            "Тут лишились сховки крадених товарів під охороною привидів жадібних найманців.");
        Add("Cursed Swampland", "Cursed Swampland", "Прокляті болота");
        Add("Toxic fog blankets the ground. You must move fast, strike hard, and leave quickly.",
            "Toxic fog blankets the ground. You must move fast, strike hard, and leave quickly.",
            "Отруйний туман вкриває землю. Рухайся швидко, бий сильно, виходь швидко.");
        Add("Bloodstone Mines", "Bloodstone Mines", "Копальні кривавого каменю");
        Add("The crystals here glow with dark energy. The enemies are highly mutated and resilient.",
            "The crystals here glow with dark energy. The enemies are highly mutated and resilient.",
            "Кристали тут світяться темною енергією. Вороги дуже мутовані й живучі.");
        Add("Desolate Tundra", "Desolate Tundra", "Пустельна тундра");
        Add("A frozen wasteland where stamina drains fast. Perfect for gathering rare frozen supplies.",
            "A frozen wasteland where stamina drains fast. Perfect for gathering rare frozen supplies.",
            "Замерзла пустка, де витривалість тане швидко. Ідеальна для збору рідкісних заморожених припасів.");
        Add("Warlord's Camp", "Warlord's Camp", "Табір воєводи");
        Add("The staging ground for the undead army. Huge swarms of enemies will test your crowd control.",
            "The staging ground for the undead army. Huge swarms of enemies will test your crowd control.",
            "Плацдарм армії мертвих. Величезні рої ворогів випробують твоє контроль натовпу.");
        Add("Shattered Bridge", "Shattered Bridge", "Розтрощений міст");
        Add("The only way to the Dark Citadel. The defense here is brutal. Don't go without upgrading your forge.",
            "The only way to the Dark Citadel. The defense here is brutal. Don't go without upgrading your forge.",
            "Єдиний шлях до Темної Цитаделі. Оборона тут жорстока. Не йди без поліпшення кузні.");
        Add("Obsidian Crags", "Obsidian Crags", "Обсидіанові скелі");
        Add("Sharp volcanic rocks tear at your boots. The undead here are infused with molten magic.",
            "Sharp volcanic rocks tear at your boots. The undead here are infused with molten magic.",
            "Гострі вулканічні скелі рвуть чоботи. Мертві тут насичені розплавленою магією.");
        Add("The Poisoned Vein", "The Poisoned Vein", "Отруєна жила");
        Add("The water supply for the citadel. It is completely corrupted. A grim and difficult battleground.",
            "The water supply for the citadel. It is completely corrupted. A grim and difficult battleground.",
            "Водопостачання цитаделі. Повністю зіпсуте. Похмуре й важке поле бою.");
        Add("Abyssal Descent", "Abyssal Descent", "Прірва");
        Add("A dark staircase leading into the depths of the earth. Claustrophobic combat awaits.",
            "A dark staircase leading into the depths of the earth. Claustrophobic combat awaits.",
            "Темні сходи вглиб землі. Чекає клаустрофобний бій.");
        Add("Citadel Outskirts", "Citadel Outskirts", "Околиці цитаделі");
        Add("The inner walls of the fortress. Elite guards patrol these ruins relentlessly.",
            "The inner walls of the fortress. Elite guards patrol these ruins relentlessly.",
            "Внутрішні стіни фортеці. Елітна варта невпинно патрулює руїни.");
        Add("Gates of Ruin", "Gates of Ruin", "Ворота Руїни");
        Add("The final barrier. Only the strongest heroes with the sharpest blades stand a chance here.",
            "The final barrier. Only the strongest heroes with the sharpest blades stand a chance here.",
            "Останній бар'єр. Тільки найсильніші герої з найгострішими клинками мають шанс тут.");
        Add("The Throne Room", "The Throne Room", "Тронна зала");
        Add("The heart of the curse. Survival is a miracle. The rewards, however, are legendary.",
            "The heart of the curse. Survival is a miracle. The rewards, however, are legendary.",
            "Серце прокляття. Вижити — це диво. Але нагороди — легендарні.");

        // === Mission Paper ===
        Add("MISSION_TARGET_LABEL", "Target: {0}", "Ціль: {0}");
        Add("MISSION_REWARDS_LABEL", "Rewards:", "Нагороди:");
        Add("MISSION_RES_WOOD", "{0} Wood", "{0} Дерева");
        Add("MISSION_RES_STONE", "{0} Stone", "{0} Каменю");
        Add("MISSION_RES_FOOD", "{0} Food", "{0} Їжі");
        Add("MISSION_RES_GEMS", "{0} Gems", "{0} Самоцвітів");

        // === Mission objectives (the WHAT-TO-DO line on the paper + HUD) ===
        // Full form — used on the mission paper the player picks up.
        Add7("OBJECTIVE_KILL_ENEMIES",
             "Defeat {0} enemies",
             "Здолай {0} ворогів",
             "Победи {0} врагов",
             "Derrota a {0} enemigos",
             "Besiege {0} Gegner",
             "Vaincs {0} ennemis",
             "Pokonaj {0} wrogów");
        Add7("OBJECTIVE_COLLECT_CRYSTALS",
             "Collect {0} crystals",
             "Збери {0} кристалів",
             "Собери {0} кристаллов",
             "Recoge {0} cristales",
             "Sammle {0} Kristalle",
             "Récupère {0} cristaux",
             "Zbierz {0} kryształów");
        Add7("OBJECTIVE_SURVIVE_MINUTES",
             "Survive for {0} minutes",
             "Виживи {0} хвилин",
             "Продержись {0} минут",
             "Sobrevive {0} minutos",
             "Überlebe {0} Minuten",
             "Survis {0} minutes",
             "Przeżyj {0} minut");
        Add7("OBJECTIVE_SURVIVE_MIN_SEC",
             "Survive for {0}m {1}s",
             "Виживи {0} хв {1} с",
             "Продержись {0} мин {1} с",
             "Sobrevive {0}m {1}s",
             "Überlebe {0} Min {1} Sek",
             "Survis {0}m {1}s",
             "Przeżyj {0}m {1}s");
        Add7("OBJECTIVE_SURVIVE_SECONDS",
             "Survive for {0} seconds",
             "Виживи {0} секунд",
             "Продержись {0} секунд",
             "Sobrevive {0} segundos",
             "Überlebe {0} Sekunden",
             "Survis {0} secondes",
             "Przeżyj {0} sekund");
        Add7("OBJECTIVE_BUILD_STRUCTURES",
             "Build {0} structures",
             "Збудуй {0} споруд",
             "Построй {0} строений",
             "Construye {0} estructuras",
             "Errichte {0} Bauwerke",
             "Construis {0} bâtiments",
             "Zbuduj {0} budowli");

        // Short form — used in the HUD widget under the mission title,
        // where the (X/Y) progress counter follows automatically.
        Add7("OBJECTIVE_SHORT_KILL",     "Defeat enemies",  "Здолай ворогів",  "Побеждай врагов", "Derrota enemigos", "Gegner besiegen", "Vaincs les ennemis", "Pokonaj wrogów");
        Add7("OBJECTIVE_SHORT_COLLECT",  "Collect crystals","Збирай кристали", "Собирай кристаллы","Recoge cristales", "Kristalle sammeln","Récupère les cristaux","Zbieraj kryształy");
        Add7("OBJECTIVE_SHORT_SURVIVE",  "Survive",         "Виживи",          "Продержись",      "Sobrevive",        "Überlebe",         "Survis",              "Przetrwaj");
        Add7("OBJECTIVE_SHORT_BUILD",    "Build structures","Будуй споруди",   "Строй строения",  "Construye",        "Bauen",            "Construis",           "Buduj");

        // === Mission names + descriptions ===
        // Each mission's English text is used as the key so calling
        // LocalizationManager.Tr(missionData.missionName) transparently
        // returns EN if untranslated, UK when a matching Add() below exists.
        // Extend these Add() lines to Add7() to cover the other 5 languages.
        Add("Skeleton Cull", "Skeleton Cull", "Проріджування скелетів");
        Add("Thin the patrols circling the outer wood-line. Their bones rot what the loam should feed.",
            "Thin the patrols circling the outer wood-line. Their bones rot what the loam should feed.",
            "Розсіч патрулі, що кружляють на лісовому узліссі. Їхні кістки гноять землю, яка мала б живити.");
        Add("Scrap Run", "Scrap Run", "Пошук аетершардів");
        Add("Drag back enough aether shards to feed the camp's furnace through dawn.",
            "Drag back enough aether shards to feed the camp's furnace through dawn.",
            "Принеси досить аетершардів, щоб кузня табору протрималась до світанку.");
        Add("Long Watch", "Long Watch", "Довга варта");
        Add("Hold position while the runners get clear. Don't die. Don't move.",
            "Hold position while the runners get clear. Don't die. Don't move.",
            "Тримай позицію, поки гінці прориваються. Не помирай. Не рухайся.");
        Add("Bone Tide", "Bone Tide", "Кістяний приплив");
        Add("The hollowed dead drift in waves now. Break the next one before it reaches the palisade.",
            "The hollowed dead drift in waves now. Break the next one before it reaches the palisade.",
            "Порожні мерці котяться хвилями. Розбий наступну до того, як вона досягне палісаду.");
        Add("Crystal Vein", "Crystal Vein", "Кришталева жила");
        Add("A cluster surfaced near the old shrine. Strip it before the mist returns.",
            "A cluster surfaced near the old shrine. Strip it before the mist returns.",
            "Скупчення виступило біля старого капища. Здери його, поки не повернувся туман.");
        Add("Forest Patrol", "Forest Patrol", "Лісовий патруль");
        Add("Sweep the south road. The traders will not pay tribute if they cannot reach us.",
            "Sweep the south road. The traders will not pay tribute if they cannot reach us.",
            "Пройди південний шлях. Купці не платять данини, якщо не можуть до нас дістатись.");
        Add("Hold the Line", "Hold the Line", "Тримай лінію");
        Add("Three runners are crossing the bog. Hold here until the signal-fire lights.",
            "Three runners are crossing the bog. Hold here until the signal-fire lights.",
            "Троє гінців перетинають болото. Тримайся тут, поки не запалять сигнальний вогонь.");
        Add("Camp Expansion", "Camp Expansion", "Розширення табору");
        Add("Lay the first stones for a new outpost. The wood comes from your hand.",
            "Lay the first stones for a new outpost. The wood comes from your hand.",
            "Заклади перше каміння нового аванпосту. Дерево — з твоєї руки.");
        Add("Elite Hunt", "Elite Hunt", "Полювання на еліту");
        Add("An iron-marked captain leads the next patrol. End him and his honor guard.",
            "An iron-marked captain leads the next patrol. End him and his honor guard.",
            "Капітан з залізним тавром веде наступний патруль. Прикінчи його та його почесну варту.");
        Add("Mountain Vigil", "Mountain Vigil", "Гірська сторожа");
        Add("Keep the high pass watched. If you fall, the eastern villages fall with you.",
            "Keep the high pass watched. If you fall, the eastern villages fall with you.",
            "Тримай високий перевал під наглядом. Якщо ти впадеш — східні села впадуть із тобою.");
        Add("Crystal Rush", "Crystal Rush", "Кришталева гарячка");
        Add("The aetherwells are bleeding. Reap what they leak before the corruption seals them.",
            "The aetherwells are bleeding. Reap what they leak before the corruption seals them.",
            "Аетерні джерела кровоточать. Збери те, що з них ллється, до того, як скверна їх запечатає.");
        Add("Highland Purge", "Highland Purge", "Очищення нагір'я");
        Add("Burn the hillside clean. Every skeleton, every wraith, every chained thing.",
            "Burn the hillside clean. Every skeleton, every wraith, every chained thing.",
            "Випали схил дочиста. Кожен скелет, кожен привид, кожна закута істота.");
        Add("Engineering Mastery", "Engineering Mastery", "Майстерність будівництва");
        Add("Two more structures, and the camp will hold a winter.",
            "Two more structures, and the camp will hold a winter.",
            "Ще дві споруди — і табір переживе зиму.");
        Add("Frontline Trench", "Frontline Trench", "Передова траншея");
        Add("Four minutes alone against everything in the wood. The wall comes after.",
            "Four minutes alone against everything in the wood. The wall comes after.",
            "Чотири хвилини сам-один проти всього у лісі. Мур — після.");
        Add("Bone Tide Eternal", "Bone Tide Eternal", "Вічний кістяний приплив");
        Add("Wave after wave, until the dawn breaks them. None pass the line.",
            "Wave after wave, until the dawn breaks them. None pass the line.",
            "Хвиля за хвилею — доки світанок їх не переломить. Ніхто не пройде.");
        Add("Aether Motherlode", "Aether Motherlode", "Аетерна жила");
        Add("A motherlode under the chapel. Strip it bare before the wardens wake.",
            "A motherlode under the chapel. Strip it bare before the wardens wake.",
            "Величезне родовище під каплицею. Здери його дочиста, поки не прокинулась варта.");
        Add("The Long Night", "The Long Night", "Довга ніч");
        Add("Six minutes between you and Aethelgard's mercy. Stand.",
            "Six minutes between you and Aethelgard's mercy. Stand.",
            "Шість хвилин між тобою і милосердям Етельгарду. Стій.");
        Add("Captain Hunt", "Captain Hunt", "Полювання на капітанів");
        Add("Five captains in the field tonight. Bring back proof of all five.",
            "Five captains in the field tonight. Bring back proof of all five.",
            "П'ять капітанів у полі цієї ночі. Принеси докази усіх п'яти.");
        Add("Master Architect", "Master Architect", "Великий будівничий");
        Add("Three pillars. Stone, wood, and patience. The camp must endure.",
            "Three pillars. Stone, wood, and patience. The camp must endure.",
            "Три стовпи. Камінь, дерево і терпіння. Табір мусить встояти.");
        Add("Final Stand", "Final Stand", "Останній бій");
        Add("The last contract on the board. Hold the wall until the bells of dawn. Then live, if you can.",
            "The last contract on the board. Hold the wall until the bells of dawn. Then live, if you can.",
            "Останній контракт на дошці. Тримай мур до дзвонів світанку. Тоді — живи, якщо зможеш.");
    }

    // Runtime-set labels that AutoLocalize can't reach (their text is
    // assigned in code AFTER the language flip fires). Callers use
    // LocalizationManager.Tr(key) directly; this table just supplies
    // the translations.
    private static void SeedInGameRuntime()
    {
        // --- Shop scene runtime labels ---
        Add7("DAMAGE",         "DAMAGE",         "УРОН",             "УРОН",            "DAÑO",              "SCHADEN",            "DÉGÂTS",              "OBRAŻENIA");
        Add7("ATK SPEED",      "ATK SPEED",      "ШВ. АТАКИ",        "СКОР. АТАКИ",     "VEL. ATAQUE",       "ANGRIFFSTEMPO",      "VIT. ATTAQUE",        "SZYB. ATAKU");
        Add7("CRIT",           "CRIT",           "КРИТ",             "КРИТ",            "CRÍTICO",           "KRIT",               "CRIT",                "KRYT.");
        Add7("HEALTH",         "HEALTH",         "ЗДОРОВ'Я",         "ЗДОРОВЬЕ",        "SALUD",             "GESUNDHEIT",         "SANTÉ",               "ZDROWIE");
        Add7("DEFENSE",        "DEFENSE",        "ЗАХИСТ",           "ЗАЩИТА",          "DEFENSA",           "VERTEIDIGUNG",       "DÉFENSE",             "OBRONA");
        Add7("POWER",          "POWER",          "СИЛА",             "СИЛА",            "PODER",             "MACHT",              "PUISSANCE",           "MOC");
        Add7("EQUIP",          "EQUIP",          "СПОРЯДИТИ",        "ЭКИПИРОВАТЬ",     "EQUIPAR",           "AUSRÜSTEN",          "ÉQUIPER",             "ZAŁÓŻ");
        Add7("EQUIPPED",       "EQUIPPED",       "СПОРЯДЖЕНО",       "ЭКИПИРОВАНО",     "EQUIPADO",          "AUSGERÜSTET",        "ÉQUIPÉ",              "ZAŁOŻONO");
        Add7("MAX",            "MAX",            "МАКС",             "МАКС",            "MÁX",               "MAX",                "MAX",                 "MAKS");
        Add7("Diamonds:",      "Diamonds:",      "Алмази:",          "Алмазы:",         "Diamantes:",        "Diamanten:",         "Diamants:",           "Diamenty:");
        Add7("Empty Category", "Empty Category", "Порожня категорія","Пустая категория","Categoría vacía",   "Leere Kategorie",    "Catégorie vide",      "Pusta kategoria");
        Add7("There are no items in this category yet.",
             "There are no items in this category yet.",
             "У цій категорії ще немає предметів.",
             "В этой категории пока нет предметов.",
             "Aún no hay artículos en esta categoría.",
             "In dieser Kategorie sind noch keine Gegenstände.",
             "Aucun objet dans cette catégorie pour l'instant.",
             "W tej kategorii nie ma jeszcze przedmiotów.");
        Add7("Upgrade for {0}",
             "Upgrade for {0}",
             "Покращити за {0}",
             "Улучшить за {0}",
             "Mejorar por {0}",
             "Verbessern für {0}",
             "Améliorer pour {0}",
             "Ulepsz za {0}");

        // --- HUD runtime labels ---
        Add7("LVL: {0}",       "LVL: {0}",       "РІВ: {0}",         "УР: {0}",         "NV: {0}",           "STUFE: {0}",         "NV: {0}",             "POZ.: {0}");
        Add7("STACK: {0}  |  x{1}",
             "STACK: {0}  |  x{1}",
             "СТЕК: {0}  |  x{1}",
             "СТЕК: {0}  |  x{1}",
             "ACUMUL.: {0}  |  x{1}",
             "STACK: {0}  |  x{1}",
             "PILE : {0}  |  x{1}",
             "STOS: {0}  |  x{1}");
        Add7("Diamonds: {0}",  "Diamonds: {0}",  "Алмази: {0}",      "Алмазы: {0}",     "Diamantes: {0}",    "Diamanten: {0}",     "Diamants: {0}",       "Diamenty: {0}");

        // --- Loading screen ---
        Add7("LOADING ASSETS... {0}%",
             "LOADING ASSETS... {0}%",
             "ЗАВАНТАЖЕННЯ РЕСУРСІВ... {0}%",
             "ЗАГРУЗКА РЕСУРСОВ... {0}%",
             "CARGANDO RECURSOS... {0}%",
             "LADE RESSOURCEN... {0}%",
             "CHARGEMENT... {0}%",
             "ŁADOWANIE ZASOBÓW... {0}%");
        Add7("GENERATING WORLD... {0}%",
             "GENERATING WORLD... {0}%",
             "ГЕНЕРАЦІЯ СВІТУ... {0}%",
             "ГЕНЕРАЦИЯ МИРА... {0}%",
             "GENERANDO MUNDO... {0}%",
             "GENERIERE WELT... {0}%",
             "GÉNÉRATION DU MONDE... {0}%",
             "GENEROWANIE ŚWIATA... {0}%");
        Add7("READY",          "READY",          "ГОТОВО",           "ГОТОВО",          "LISTO",             "BEREIT",             "PRÊT",                "GOTOWE");

        // --- Camp building prompts ---
        Add7("MAX LEVEL",      "MAX LEVEL",      "МАКС. РІВЕНЬ",     "МАКС. УРОВЕНЬ",   "NIVEL MÁX",         "MAX. STUFE",         "NIVEAU MAX",          "MAKS. POZIOM");
        Add7("HOLD [E] TO BUILD","HOLD [E] TO BUILD","УТРИМУЙ [E] ЩОБ БУДУВАТИ","УДЕРЖИВАЙ [E] ЧТОБЫ СТРОИТЬ","MANTÉN [E] PARA CONSTRUIR","[E] HALTEN ZUM BAUEN","MAINTENIR [E] POUR CONSTRUIRE","TRZYMAJ [E] BY BUDOWAĆ");
        Add7("HOLD [E] TO UPGRADE","HOLD [E] TO UPGRADE","УТРИМУЙ [E] ЩОБ ПОКРАЩИТИ","УДЕРЖИВАЙ [E] ЧТОБЫ УЛУЧШИТЬ","MANTÉN [E] PARA MEJORAR","[E] HALTEN ZUM AUFRÜSTEN","MAINTENIR [E] POUR AMÉLIORER","TRZYMAJ [E] BY ULEPSZYĆ");

        // --- Map / Region UI ---
        Add7("UPGRADE",        "UPGRADE",        "ПОКРАЩИТИ",        "УЛУЧШИТЬ",        "MEJORAR",           "VERBESSERN",         "AMÉLIORER",           "ULEPSZ");
        Add7("TRAVEL",         "TRAVEL",         "ПОДОРОЖУВАТИ",     "ПУТЕШЕСТВОВАТЬ",  "VIAJAR",            "REISEN",             "VOYAGER",             "PODRÓŻUJ");
        Add7("MAX LEVEL REACHED","MAX LEVEL REACHED","ДОСЯГНУТО МАКС. РІВНЯ","ДОСТИГНУТ МАКС. УРОВЕНЬ","NIVEL MÁX. ALCANZADO","MAXIMALSTUFE ERREICHT","NIVEAU MAX ATTEINT","OSIĄGNIĘTO MAKS. POZIOM");
        Add7("AREA LOCKED",   "AREA LOCKED",   "ЗОНА ЗАБЛОКОВАНА", "ЗОНА ЗАБЛОКИРОВАНА","ZONA BLOQUEADA",    "GEBIET GESPERRT",    "ZONE VERROUILLÉE",    "OBSZAR ZAMKNIĘTY");
        Add7("START JOURNEY", "START JOURNEY", "ПОЧАТИ ПОДОРОЖ",   "НАЧАТЬ ПУТЕШЕСТВИЕ","INICIAR VIAJE",     "REISE BEGINNEN",     "COMMENCER LE VOYAGE", "ROZPOCZNIJ WYPRAWĘ");
        Add7("RECOMMENDED",   "RECOMMENDED",   "РЕКОМЕНДОВАНО",    "РЕКОМЕНДУЕТСЯ",   "RECOMENDADO",       "EMPFOHLEN",          "RECOMMANDÉ",          "ZALECANE");
        Add7("YOUR POWER",    "YOUR POWER",    "ТВОЯ СИЛА",        "ТВОЯ СИЛА",       "TU PODER",          "DEINE MACHT",        "VOTRE PUISSANCE",     "TWOJA MOC");

        // --- Notice board ---
        Add7("You already have 3 active missions.\nComplete them first!",
             "You already have 3 active missions.\nComplete them first!",
             "У вас вже 3 активні місії.\nСпершу завершіть їх!",
             "У вас уже 3 активные миссии.\nСначала завершите их!",
             "Ya tienes 3 misiones activas.\n¡Termínalas primero!",
             "Du hast bereits 3 aktive Missionen.\nSchließe sie zuerst ab!",
             "Vous avez déjà 3 missions actives.\nTerminez-les d'abord !",
             "Masz już 3 aktywne misje.\nNajpierw je ukończ!");
        Add7("No new missions available right now.\nCheck back later.",
             "No new missions available right now.\nCheck back later.",
             "Нових місій зараз немає.\nПеревір пізніше.",
             "Новых миссий сейчас нет.\nЗайди позже.",
             "No hay misiones nuevas ahora.\nVuelve más tarde.",
             "Zurzeit keine neuen Missionen.\nSchau später vorbei.",
             "Aucune mission disponible pour le moment.\nRevenez plus tard.",
             "Brak nowych misji.\nWróć później.");

        // --- Cinematic skip prompt ---
        Add7("Press <b>SPACE</b> to Skip",
             "Press <b>SPACE</b> to Skip",
             "Натисни <b>ПРОБІЛ</b> щоб пропустити",
             "Нажми <b>ПРОБЕЛ</b> чтобы пропустить",
             "Pulsa <b>ESPACIO</b> para saltar",
             "<b>LEERTASTE</b> zum Überspringen",
             "Appuyez sur <b>ESPACE</b> pour passer",
             "Wciśnij <b>SPACJĘ</b>, by pominąć");

        // --- Save & Close ---
        Add7("SAVE & CLOSE",   "SAVE & CLOSE",   "ЗБЕРЕГТИ І ЗАКРИТИ","СОХРАНИТЬ И ЗАКРЫТЬ","GUARDAR Y CERRAR","SPEICHERN & SCHLIESSEN","SAUVEGARDER ET FERMER","ZAPISZ I ZAMKNIJ");
    }

    // Full-locale add — feed all 6 translations at once. Polish silently
    // falls back to English here; use Add7 below to provide all 7.
    private static void Add6(string key, string en, string uk, string ru, string es, string de, string fr)
    {
        s_en[key] = en;
        s_uk[key] = uk;
        s_ru[key] = ru;
        s_es[key] = es;
        s_de[key] = de;
        s_fr[key] = fr;
    }

    private static void Add7(string key, string en, string uk, string ru, string es, string de, string fr, string pl)
    {
        s_en[key] = en;
        s_uk[key] = uk;
        s_ru[key] = ru;
        s_es[key] = es;
        s_de[key] = de;
        s_fr[key] = fr;
        s_pl[key] = pl;
    }

    private static void AddLore(string key, string titleEn, string titleUk, string bodyEn, string bodyUk)
    {
        s_en[key + "_TITLE"] = titleEn;
        s_uk[key + "_TITLE"] = titleUk;
        s_en[key + "_BODY"] = bodyEn;
        s_uk[key + "_BODY"] = bodyUk;
    }
}