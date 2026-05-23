using UnityEngine;
using UnityEditor;
using System.IO;

public class ArmorDataGenerator : EditorWindow
{
    [MenuItem("Tools/MegaBonk/Generate Armor Data")]
    public static void ShowWindow()
    {
        GetWindow<ArmorDataGenerator>("Armor Generator");
    }

    private void OnGUI()
    {
        GUILayout.Label("GanzSe Armor Data Generator", EditorStyles.boldLabel);
        GUILayout.Label("Це створить 108 файлів ArmorData з балансом.");

        if (GUILayout.Button("Generate All Armor!"))
        {
            GenerateArmor();
        }
    }

    private void GenerateArmor()
    {
        string basePath = "Assets/GameData/Armor";

        // Створюємо папки, якщо їх немає
        if (!AssetDatabase.IsValidFolder("Assets/GameData")) AssetDatabase.CreateFolder("Assets", "GameData");
        if (!AssetDatabase.IsValidFolder(basePath)) AssetDatabase.CreateFolder("Assets/GameData", "Armor");

        string[] categories = { "Head", "Chest", "Arms", "Belt", "Legs", "Feet" };
        string[] tierPrefixes = { "Novice", "Mercenary", "Knight", "Paladin", "Royal", "Abyssal" };
        string[] categoryNames = { "Helm", "Chestplate", "Gauntlets", "Belt", "Greaves", "Boots" };

        int globalID = 100; // Починаємо ID з 100, щоб не плутати зі зброєю

        for (int catIndex = 0; catIndex < categories.Length; catIndex++)
        {
            ArmorCategory currentCat = (ArmorCategory)catIndex;
            string catFolderPath = basePath + "/" + categories[catIndex];

            if (!AssetDatabase.IsValidFolder(catFolderPath))
                AssetDatabase.CreateFolder(basePath, categories[catIndex]);

            for (int type = 1; type <= 6; type++)
            {
                for (int color = 1; color <= 3; color++)
                {
                    // Створюємо екземпляр ScriptableObject
                    ArmorData newArmor = ScriptableObject.CreateInstance<ArmorData>();

                    int prefabIndex = ((type - 1) * 3) + (color - 1); // Формула індексу (0-17)

                    // Назва
                    string colorVariant = color == 1 ? "" : color == 2 ? " (Sturdy)" : " (Elite)";
                    newArmor.armorName = $"{tierPrefixes[type - 1]} {categoryNames[catIndex]}{colorVariant}";
                    newArmor.armorID = globalID++;
                    newArmor.category = currentCat;
                    newArmor.prefabIndex = prefabIndex;

                    // Опис
                    newArmor.description = $"A reliable {categoryNames[catIndex].ToLower()} forged for a {tierPrefixes[type - 1].ToLower()} warrior. Provides decent protection in battle.";

                    // --- БАЛАНС ТА ЕКОНОМІКА ---
                    // Базові речі 1 типу робимо безкоштовними (щоб вони були одягнені на старті)
                    bool isDefaultStarter = (type == 1 && color == 1);

                    newArmor.price = isDefaultStarter ? 0 : (type * type * 250) + (color * 150);

                    newArmor.basePower = (type * 20) + (color * 5);
                    newArmor.powerPerLevel = type * 3;

                    newArmor.baseHealthBonus = (type * 15f) + (color * 5f);
                    newArmor.healthPerLevel = 10f;

                    // Броня дає відсоток поглинання урону (наприклад Type 6 дає 6% + за апгрейди)
                    newArmor.baseDamageReduction = type * 0.01f;
                    newArmor.reductionPerLevel = 0.005f;

                    // Зберігаємо файл
                    string assetPath = $"{catFolderPath}/{categories[catIndex]}_T{type}_C{color}.asset";
                    AssetDatabase.CreateAsset(newArmor, assetPath);
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("✅ Успішно згенеровано 108 файлів броні!");
    }
}