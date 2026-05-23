using UnityEngine;
using UnityEditor; // Обов'язково для Editor-скриптів

public class AutoAssignArmorIcons : MonoBehaviour
{
    // Цей рядок додасть нову кнопку прямо у верхнє меню Unity!
    [MenuItem("Tools/Auto-Assign Armor Icons")]
    public static void AssignIcons()
    {
        // 1. Знаходимо всі файли ArmorData в проекті
        string[] armorGuids = AssetDatabase.FindAssets("t:ArmorData");

        // 2. Знаходимо всі Спрайти в папці, куди ми їх згенерували
        string[] spriteGuids = AssetDatabase.FindAssets("t:Sprite", new[] { "Assets/GeneratedArmorIcons" });

        if (spriteGuids.Length == 0)
        {
            Debug.LogError("Не знайдено жодного спрайта в папці 'Assets/GeneratedArmorIcons'! Переконайся, що папка існує і тип картинок змінено на Sprite (2D and UI).");
            return;
        }

        int assignedCount = 0;

        // 3. Проходимося по кожному файлу броні
        foreach (string armorGuid in armorGuids)
        {
            string armorPath = AssetDatabase.GUIDToAssetPath(armorGuid);
            ArmorData armorData = AssetDatabase.LoadAssetAtPath<ArmorData>(armorPath);

            if (armorData == null) continue;

            // Створюємо ключ пошуку з категорії (напр. "Head" -> "HEAD", "Arms" -> "ARM")
            string catKey = armorData.category.ToString().ToUpper();
            if (catKey == "ARMS") catKey = "ARM";
            if (catKey == "LEGS") catKey = "LEG";

            // Ми знаємо, що генератор додавав індекс в кінець (напр. "_0")
            string expectedSuffix = $"_{armorData.prefabIndex}";

            // 4. Шукаємо відповідний спрайт
            foreach (string spriteGuid in spriteGuids)
            {
                string spritePath = AssetDatabase.GUIDToAssetPath(spriteGuid);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);

                if (sprite == null) continue;

                string spriteName = sprite.name.ToUpper();

                // Якщо ім'я спрайта містить категорію і закінчується на правильний індекс
                if (spriteName.Contains(catKey) && spriteName.EndsWith(expectedSuffix))
                {
                    // ПРИЗНАЧАЄМО ІКОНКУ!
                    armorData.icon = sprite;

                    // Кажемо Unity, що файл змінився і його треба буде зберегти
                    EditorUtility.SetDirty(armorData);
                    assignedCount++;
                    break; // Знайшли — переходимо до наступної броні
                }
            }
        }

        // 5. Зберігаємо всі зміни на диск
        AssetDatabase.SaveAssets();
        Debug.Log($"<color=green>✅ ГОТОВО! Автоматично призначено {assignedCount} іконок до файлів ArmorData.</color>");
    }
}