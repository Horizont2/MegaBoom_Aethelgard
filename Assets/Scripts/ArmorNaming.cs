using System.Collections.Generic;

// Builds a gear name that is grammatical in the language it is shown in.
//
// ==== WHY THE GENERIC TRANSLATOR CANNOT DO THIS ====
//
// Armour is authored as "Tier Slot (Variant)" — "Novice Helm", "Abyssal
// Chestplate (Elite)" — and there are 108 of them, so registering each one by
// hand in seven languages was never going to happen. LocalizationManager
// therefore falls back to translating the words ONE AT A TIME and gluing them
// back together in the order they arrived.
//
// That works in English and in nothing else, because the order IS the grammar.
// The player reported the result: "Новачок Шолом" — two correctly translated
// words in an order no Ukrainian speaker would produce. It should be "Шолом
// новачка", noun first, tier behind it in the genitive. Spanish wants "Casco de
// novato", French "Casque de novice", German glues into one word. Word-by-word
// translation cannot reach any of those, however good the individual words are.
//
// ==== THE TRICK THAT KEEPS THIS SMALL ====
//
// Six tiers, six slots and three variants is 108 names — but only fifteen
// pieces of vocabulary per language, if the tier is rendered as a NOUN IN THE
// GENITIVE rather than as an adjective. "Шолом новачка", "Кіраса новачка",
// "Рукавиці новачка": one form of the tier fits every slot, whatever that
// slot's gender, so no agreement table is needed at all.
//
// The variant tag does need agreement, because it is a real adjective — but
// that is two words across five forms, not a hundred and eight.
public static class ArmorNaming
{
    // ---- vocabulary ---------------------------------------------------------

    // Five, not four. Slavic plurals carry no gender, but Spanish and French
    // ones do — "Guanteletes reforzados" against "Grebas reforzadas" — so a
    // single Plural bucket cannot hold the right word for both.
    private enum Gender { Masculine, Feminine, Neuter, MasculinePlural, FemininePlural }

    private struct Slot
    {
        public string Uk, Ru, Es, De, Fr, Pl;
        public Gender GenderUk, GenderRu, GenderPl, GenderEs, GenderFr;
    }

    // Grammatical gender is per language: "Кіраса" is feminine in Ukrainian and
    // Russian, while its Polish counterpart "Napierśnik" is masculine.
    private static readonly Dictionary<string, Slot> Slots = new Dictionary<string, Slot>
    {
        ["Helm"] = new Slot {
            Uk = "Шолом",    Ru = "Шлем",     Es = "Casco",      De = "Helm",        Fr = "Casque",      Pl = "Hełm",
            GenderUk = Gender.Masculine, GenderRu = Gender.Masculine, GenderPl = Gender.Masculine,
            GenderEs = Gender.Masculine, GenderFr = Gender.Masculine },
        ["Chestplate"] = new Slot {
            Uk = "Кіраса",   Ru = "Кираса",   Es = "Coraza",     De = "Brustpanzer", Fr = "Cuirasse",    Pl = "Napierśnik",
            GenderUk = Gender.Feminine,  GenderRu = Gender.Feminine,  GenderPl = Gender.Masculine,
            GenderEs = Gender.Feminine,  GenderFr = Gender.Feminine },
        ["Gauntlets"] = new Slot {
            Uk = "Рукавиці", Ru = "Перчатки", Es = "Guanteletes",De = "Panzerhandschuhe", Fr = "Gantelets", Pl = "Rękawice",
            GenderUk = Gender.MasculinePlural, GenderRu = Gender.MasculinePlural, GenderPl = Gender.MasculinePlural,
            GenderEs = Gender.MasculinePlural, GenderFr = Gender.MasculinePlural },
        ["Belt"] = new Slot {
            Uk = "Пояс",     Ru = "Пояс",     Es = "Cinturón",   De = "Gürtel",      Fr = "Ceinture",    Pl = "Pas",
            GenderUk = Gender.Masculine, GenderRu = Gender.Masculine, GenderPl = Gender.Masculine,
            GenderEs = Gender.Masculine, GenderFr = Gender.Feminine },
        ["Greaves"] = new Slot {
            Uk = "Поножі",   Ru = "Поножи",   Es = "Grebas",     De = "Beinschienen",Fr = "Jambières",   Pl = "Nagolenniki",
            GenderUk = Gender.MasculinePlural, GenderRu = Gender.MasculinePlural, GenderPl = Gender.MasculinePlural,
            GenderEs = Gender.FemininePlural,  GenderFr = Gender.FemininePlural },
        ["Boots"] = new Slot {
            Uk = "Чоботи",   Ru = "Сапоги",   Es = "Botas",      De = "Stiefel",     Fr = "Bottes",      Pl = "Buty",
            GenderUk = Gender.MasculinePlural, GenderRu = Gender.MasculinePlural, GenderPl = Gender.MasculinePlural,
            GenderEs = Gender.FemininePlural,  GenderFr = Gender.FemininePlural },
    };

    private struct Tier
    {
        // Slavic languages take the tier as a genitive noun, which sidesteps
        // gender agreement entirely. Romance languages take a "de X" phrase.
        public string UkGen, RuGen, PlGen, EsDe, FrDe, DePrefix;
    }

    private static readonly Dictionary<string, Tier> Tiers = new Dictionary<string, Tier>
    {
        ["Novice"] = new Tier {
            UkGen = "новачка", RuGen = "новичка", PlGen = "nowicjusza",
            EsDe = "de novato", FrDe = "de novice", DePrefix = "Novizen" },
        ["Mercenary"] = new Tier {
            UkGen = "найманця", RuGen = "наёмника", PlGen = "najemnika",
            EsDe = "de mercenario", FrDe = "de mercenaire", DePrefix = "Söldner" },
        ["Knight"] = new Tier {
            UkGen = "лицаря", RuGen = "рыцаря", PlGen = "rycerza",
            EsDe = "de caballero", FrDe = "de chevalier", DePrefix = "Ritter" },
        ["Paladin"] = new Tier {
            UkGen = "паладина", RuGen = "паладина", PlGen = "paladyna",
            EsDe = "de paladín", FrDe = "de paladin", DePrefix = "Paladin" },
        ["Royal"] = new Tier {
            UkGen = "короля", RuGen = "короля", PlGen = "króla",
            EsDe = "real", FrDe = "royal", DePrefix = "Königs" },
        ["Abyssal"] = new Tier {
            UkGen = "безодні", RuGen = "бездны", PlGen = "otchłani",
            EsDe = "del abismo", FrDe = "des abysses", DePrefix = "Abgrund" },
    };

    // The variant IS an adjective, so it agrees with the slot it is tagging.
    private struct Variant
    {
        // Indexed by Gender: masculine, feminine, neuter, masc-plural, fem-plural.
        public string[] Uk, Ru, Pl, Es, Fr;
        // German keeps ONE form: as a parenthetical tag it is predicative, and a
        // predicative adjective in German is not declined.
        public string De;
    }

    private static readonly Dictionary<string, Variant> Variants = new Dictionary<string, Variant>
    {
        ["Sturdy"] = new Variant {
            Uk = new[] { "міцний", "міцна", "міцне", "міцні", "міцні" },
            Ru = new[] { "прочный", "прочная", "прочное", "прочные", "прочные" },
            Pl = new[] { "wzmocniony", "wzmocniona", "wzmocnione", "wzmocnione", "wzmocnione" },
            Es = new[] { "reforzado", "reforzada", "reforzado", "reforzados", "reforzadas" },
            Fr = new[] { "renforcé", "renforcée", "renforcé", "renforcés", "renforcées" },
            De = "verstärkt" },
        ["Elite"] = new Variant {
            Uk = new[] { "елітний", "елітна", "елітне", "елітні", "елітні" },
            Ru = new[] { "элитный", "элитная", "элитное", "элитные", "элитные" },
            Pl = new[] { "elitarny", "elitarna", "elitarne", "elitarne", "elitarne" },
            // Invariable in both: "de élite" / "d'élite" never inflect.
            Es = new[] { "de élite", "de élite", "de élite", "de élite", "de élite" },
            Fr = new[] { "d'élite", "d'élite", "d'élite", "d'élite", "d'élite" },
            De = "Elite" },
    };

    // ---- entry point --------------------------------------------------------

    // Returns a display name for an authored English gear name. Anything that
    // is not "Tier Slot" or "Tier Slot (Variant)" — the weapons, the shields,
    // anything hand-named — goes through the ordinary translator untouched.
    public static string Display(string englishName)
    {
        if (string.IsNullOrEmpty(englishName)) return englishName;

        int lang = LocalizationManager.CurrentLanguage;
        if (lang == (int)LocalizationManager.Lang.English) return englishName;

        string body = englishName;
        string variantKey = null;

        int paren = body.LastIndexOf(" (");
        if (paren > 0 && body.EndsWith(")"))
        {
            variantKey = body.Substring(paren + 2, body.Length - paren - 3);
            body = body.Substring(0, paren);
        }

        int space = body.IndexOf(' ');
        if (space <= 0) return LocalizationManager.Tr(englishName);

        string tierKey = body.Substring(0, space);
        string slotKey = body.Substring(space + 1);

        if (!Tiers.TryGetValue(tierKey, out Tier tier) || !Slots.TryGetValue(slotKey, out Slot slot))
            return LocalizationManager.Tr(englishName);

        string name = Compose(lang, tier, slot);

        if (variantKey != null && Variants.TryGetValue(variantKey, out Variant v))
            name += " (" + VariantWord(lang, v, slot) + ")";
        else if (variantKey != null)
            name += " (" + variantKey + ")";

        return name;
    }

    private static string Compose(int lang, Tier tier, Slot slot)
    {
        switch ((LocalizationManager.Lang)lang)
        {
            // Noun first, tier behind it in the genitive: "Шолом новачка".
            case LocalizationManager.Lang.Ukrainian: return slot.Uk + " " + tier.UkGen;
            case LocalizationManager.Lang.Russian:   return slot.Ru + " " + tier.RuGen;
            case LocalizationManager.Lang.Polish:    return slot.Pl + " " + tier.PlGen;

            // "Casco de novato", "Casque de novice".
            case LocalizationManager.Lang.Spanish:   return slot.Es + " " + tier.EsDe;
            case LocalizationManager.Lang.French:    return slot.Fr + " " + tier.FrDe;

            // German compounds: "Ritterhelm", "Abgrundbrustpanzer".
            case LocalizationManager.Lang.German:    return tier.DePrefix + slot.De.ToLowerInvariant();
        }
        return slot.Uk + " " + tier.UkGen;
    }

    private static string VariantWord(int lang, Variant v, Slot slot)
    {
        switch ((LocalizationManager.Lang)lang)
        {
            case LocalizationManager.Lang.Ukrainian: return v.Uk[(int)slot.GenderUk];
            case LocalizationManager.Lang.Russian:   return v.Ru[(int)slot.GenderRu];
            case LocalizationManager.Lang.Polish:    return v.Pl[(int)slot.GenderPl];
            case LocalizationManager.Lang.Spanish:   return v.Es[(int)slot.GenderEs];
            case LocalizationManager.Lang.French:    return v.Fr[(int)slot.GenderFr];
            case LocalizationManager.Lang.German:    return v.De;
        }
        return v.Uk[(int)slot.GenderUk];
    }
}
