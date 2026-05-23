using UnityEngine;
using System.Collections.Generic;

public enum ArmorSlot { Head, Chest, Arms, Belt, Legs, Feet }

public class ModularArmorManager : MonoBehaviour
{
    [Header("Hierarchy Roots")]
    [Tooltip("Скрипт знайде цю папку автоматично, якщо залишити поле пустим")]
    [SerializeField] private Transform armorPartsRoot;

    private Dictionary<ArmorSlot, List<GameObject>> armorSlotsObjects = new Dictionary<ArmorSlot, List<GameObject>>();
    private bool isInitialized = false;

    private void Awake()
    {
        InitializeArmorSlots();
        LoadEquippedArmor();
    }

    private void InitializeArmorSlots()
    {
        if (isInitialized) return;

        // ЗАХИСТ: Примусово знищуємо рідний скрипт асету ModularHeroController,
        // якщо він залишився на префабі, бо він ламає логіку і вмикає дефолтні речі назад кожні кілька кадрів!
        Component assetController = GetComponent("ModularHeroController");
        if (assetController != null)
        {
            Destroy(assetController);
        }

        // Завжди шукаємо об'єкт строго всередині цього екземпляра, щоб уникнути посилань на префаб
        armorPartsRoot = FindDeepChild(transform, "ARMOR PARTS");

        if (armorPartsRoot == null)
        {
            Debug.LogError($"[ModularArmorManager] КРИТИЧНА ПОМИЛКА: Не знайдено об'єкт 'ARMOR PARTS' у {gameObject.name}. Броня не працюватиме!");
            return;
        }

        // Готуємо списки словника
        armorSlotsObjects.Clear();
        foreach (ArmorSlot slot in System.Enum.GetValues(typeof(ArmorSlot)))
        {
            armorSlotsObjects[slot] = new List<GameObject>();
        }

        // Скануємо всі папки категорій (HEADS, CHESTS і т.д.)
        foreach (Transform category in armorPartsRoot)
        {
            string nameUpper = category.name.ToUpper();
            ArmorSlot targetSlot;

            if (nameUpper.Contains("HEAD")) targetSlot = ArmorSlot.Head;
            else if (nameUpper.Contains("CHEST")) targetSlot = ArmorSlot.Chest;
            else if (nameUpper.Contains("ARM")) targetSlot = ArmorSlot.Arms;
            else if (nameUpper.Contains("BELT")) targetSlot = ArmorSlot.Belt;
            else if (nameUpper.Contains("LEG")) targetSlot = ArmorSlot.Legs;
            else if (nameUpper.Contains("FEET")) targetSlot = ArmorSlot.Feet;
            else continue;

            // Збираємо елементи броні та примусово ховаємо їх з екрану
            foreach (Transform item in category)
            {
                armorSlotsObjects[targetSlot].Add(item.gameObject);
                item.gameObject.SetActive(false);
            }
        }

        isInitialized = true;
    }

    // TІЛЬКИ ВІЗУАЛ: Для примірки в магазині (не зберігає в PlayerPrefs)
    public void EquipArmorVisuals(ArmorSlot slot, int itemIndex)
    {
        if (!isInitialized) InitializeArmorSlots();

        if (!armorSlotsObjects.ContainsKey(slot) || armorSlotsObjects[slot].Count == 0) return;

        List<GameObject> parts = armorSlotsObjects[slot];

        // 1. Примусово вимикаємо абсолютно ВСІ елементи у цьому слоті
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] != null) parts[i].SetActive(false);
        }

        // 2. Одягаємо лише один потрібний елемент
        if (itemIndex >= 0 && itemIndex < parts.Count)
        {
            if (parts[itemIndex] != null) parts[itemIndex].SetActive(true);
        }
    }

    // ВІЗУАЛ + ЗБЕРЕЖЕННЯ: Для покупного або активованого екіпірування
    public void EquipAndSaveArmor(ArmorSlot slot, int itemIndex)
    {
        EquipArmorVisuals(slot, itemIndex);
        PlayerPrefs.SetInt($"EquippedArmor_{slot}", itemIndex);
        PlayerPrefs.Save();
    }

    // АВТО-ЗАВАНТАЖЕННЯ: При вході в магазин чи спавні
    public void LoadEquippedArmor()
    {
        if (!isInitialized) InitializeArmorSlots();

        foreach (ArmorSlot slot in System.Enum.GetValues(typeof(ArmorSlot)))
        {
            EquipArmorVisuals(slot, PlayerPrefs.GetInt($"EquippedArmor_{slot}", 0));
        }
    }

    private Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name.ToUpper() == name.ToUpper()) return child;
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }
}