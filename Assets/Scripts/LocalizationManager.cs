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
    public static int CurrentLanguage
    {
        get { EnsureLoaded(); return (int)s_lang; }
        set
        {
            EnsureLoaded();
            Lang newLang = (Lang)Mathf.Clamp(value, 0, 6);
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
        Dictionary<string, string> active = ActiveDictionary();
        if (active.TryGetValue(key, out string v)) return v;
        // Always fall through to English if the active locale is missing
        // the entry — keeps unlocalised strings readable instead of
        // surfacing the raw key in the UI.
        if (s_lang != Lang.English && s_en.TryGetValue(key, out v)) return v;
        return key; // last resort — key acts as the literal string
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