using UnityEngine;
using System.IO;
using System.Collections;

public class ArmorIconGenerator : MonoBehaviour
{
    [Header("Studio Setup")]
    public Camera renderCamera;
    public Transform armorPartsRoot;
    [Tooltip("Перетягни сюди базовий меш тіла лицаря, щоб скрипт його сховав під час зйомки")]
    public GameObject baseCharacterMesh;
    public int iconResolution = 512;

    [Tooltip("Наскільки далеко камера від'їде від предмета (1.0 = впритул, 1.3 = з відступами)")]
    public float padding = 1.3f;

    [Header("Output")]
    public string folderName = "GeneratedArmorIcons";

    [ContextMenu("📸 GENERATE ICONS NOW")]
    public void StartGeneration()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Генератор працює тільки в режимі Play!");
            return;
        }
        StartCoroutine(GenerateRoutine());
    }

    private IEnumerator GenerateRoutine()
    {
        string fullPath = Path.Combine(Application.dataPath, folderName);
        if (!Directory.Exists(fullPath)) Directory.CreateDirectory(fullPath);

        // Ховаємо тіло лицаря
        if (baseCharacterMesh != null) baseCharacterMesh.SetActive(false);

        // Налаштовуємо камеру на Ортографію (2D вигляд без перспективи) для ідеальних іконок
        renderCamera.clearFlags = CameraClearFlags.SolidColor;
        renderCamera.backgroundColor = new Color(0, 0, 0, 0);
        renderCamera.orthographic = true;

        RenderTexture rt = new RenderTexture(iconResolution, iconResolution, 24);
        renderCamera.targetTexture = rt;
        Texture2D screenShot = new Texture2D(iconResolution, iconResolution, TextureFormat.ARGB32, false);

        // Вимикаємо всю броню
        foreach (Transform category in armorPartsRoot)
        {
            foreach (Transform item in category) item.gameObject.SetActive(false);
        }

        foreach (Transform category in armorPartsRoot)
        {
            for (int i = 0; i < category.childCount; i++)
            {
                Transform item = category.GetChild(i);
                item.gameObject.SetActive(true);

                // --- МАГІЯ АВТО-ФОКУСУ ---
                Renderer[] renderers = item.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    // Знаходимо спільні габарити (Bounds) всіх шматків цієї броні
                    Bounds bounds = renderers[0].bounds;
                    for (int r = 1; r < renderers.Length; r++)
                    {
                        bounds.Encapsulate(renderers[r].bounds);
                    }

                    // Ставимо камеру рівно по центру предмета
                    Vector3 center = bounds.center;
                    renderCamera.transform.position = center - renderCamera.transform.forward * 5f;

                    // Підбираємо ідеальний зум (Orthographic Size), щоб предмет вліз у кадр
                    float maxExtents = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
                    renderCamera.orthographicSize = maxExtents * padding;
                }

                yield return new WaitForEndOfFrame();

                // Знімок
                RenderTexture.active = rt;
                renderCamera.Render();
                screenShot.ReadPixels(new Rect(0, 0, iconResolution, iconResolution), 0, 0);
                screenShot.Apply();

                byte[] bytes = screenShot.EncodeToPNG();
                string fileName = $"{category.name}_{item.name}_{i}.png";
                File.WriteAllBytes(Path.Combine(fullPath, fileName), bytes);

                item.gameObject.SetActive(false);
            }
        }

        // Очищення
        renderCamera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
        Destroy(screenShot);

        // Повертаємо тіло на місце
        if (baseCharacterMesh != null) baseCharacterMesh.SetActive(true);

        GameLog.Info($"<color=green>✅ ГОТОВО! Збережено 108 ідеально відцентрованих іконок у папку {folderName}</color>");
    }
}