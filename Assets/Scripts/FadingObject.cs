using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

public class FadingObject : MonoBehaviour
{
    private float fadeTargetAlpha = 0.25f;
    private float currentAlpha = 1f;
    private float fadeSpeed = 4f;
    private bool isFadingOut = false;

    private Renderer[] renderers;

    // Словники для збереження оригінальних та прозорих версій матеріалів
    private Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
    private Dictionary<Renderer, Material[]> transparentMaterials = new Dictionary<Renderer, Material[]>();

    private bool isInitialized = false;

    public void Initialize(float targetAlpha, float speed)
    {
        if (isInitialized) return;

        fadeTargetAlpha = targetAlpha;
        fadeSpeed = speed;

        renderers = GetComponentsInChildren<Renderer>();

        foreach (Renderer r in renderers)
        {
            // Ігноруємо ефекти часток
            if (r is ParticleSystemRenderer) continue;

            originalMaterials[r] = r.sharedMaterials;
            Material[] transMats = new Material[r.sharedMaterials.Length];

            for (int i = 0; i < r.sharedMaterials.Length; i++)
            {
                Material orig = r.sharedMaterials[i];
                if (orig == null) continue;

                Material transMat = new Material(orig);

                // Змушуємо URP матеріал підтримувати прозорість
                transMat.SetFloat("_Surface", 1);
                transMat.SetFloat("_Blend", 0);
                transMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                transMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                transMat.SetInt("_ZWrite", 0);
                transMat.renderQueue = (int)RenderQueue.Transparent;

                transMats[i] = transMat;
            }
            transparentMaterials[r] = transMats;
        }

        currentAlpha = 1f;
        isInitialized = true;
    }

    public void FadeOut()
    {
        isFadingOut = true;
    }

    public void FadeIn()
    {
        isFadingOut = false;
    }

    private void Update()
    {
        if (!isInitialized) return;

        float target = isFadingOut ? fadeTargetAlpha : 1f;

        if (Mathf.Abs(currentAlpha - target) > 0.01f)
        {
            currentAlpha = Mathf.Lerp(currentAlpha, target, Time.deltaTime * fadeSpeed);
            ApplyAlpha(currentAlpha);
        }
        else if (!isFadingOut)
        {
            // Самовидалення для економії ресурсів, коли дерево знову повністю непрозоре
            Destroy(this);
        }
    }

    private void ApplyAlpha(float alpha)
    {
        foreach (Renderer r in renderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;

            // БЕЗПЕЧНА ПЕРЕВІРКА: використовуємо TryGetValue, щоб уникнути KeyNotFoundException
            if (transparentMaterials.TryGetValue(r, out Material[] transMats))
            {
                if (r.sharedMaterials != transMats)
                {
                    r.sharedMaterials = transMats;
                }

                foreach (Material mat in r.sharedMaterials)
                {
                    if (mat == null) continue;

                    if (mat.HasProperty("_BaseColor"))
                    {
                        Color c = mat.GetColor("_BaseColor");
                        c.a = alpha;
                        mat.SetColor("_BaseColor", c);
                    }
                    else if (mat.HasProperty("_Color"))
                    {
                        Color c = mat.GetColor("_Color");
                        c.a = alpha;
                        mat.SetColor("_Color", c);
                    }
                }
            }
        }
    }

    private void RestoreOriginalMaterials()
    {
        foreach (Renderer r in renderers)
        {
            // Оптимізована безпечна перевірка
            if (r != null && originalMaterials.TryGetValue(r, out Material[] origMats))
            {
                r.sharedMaterials = origMats;
            }
        }
    }

    // --- МАГІЯ ДЛЯ МІНІМАПИ ---

    private void OnEnable()
    {
        // Підписуємося на події рендерингу камер URP
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        RestoreOriginalMaterials();
    }

    private void OnDestroy()
    {
        RestoreOriginalMaterials();

        // ВАЖЛИВО: Очищуємо створені клони матеріалів, щоб оперативна пам'ять не переповнювалась
        foreach (var kvp in transparentMaterials)
        {
            foreach (Material mat in kvp.Value)
            {
                if (mat != null) Destroy(mat);
            }
        }
        transparentMaterials.Clear();
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // ПЕРЕД тим як MinimapCamera почне малювати, повертаємо дереву оригінальні непрозорі матеріали
        if (camera.name == "MinimapCamera" && currentAlpha < 1f)
        {
            RestoreOriginalMaterials();
        }
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // ПІСЛЯ того як мапа намальована, повертаємо дереву напівпрозорий стан назад
        if (camera.name == "MinimapCamera" && currentAlpha < 1f && isInitialized)
        {
            foreach (Renderer r in renderers)
            {
                // Оптимізована безпечна перевірка
                if (r != null && transparentMaterials.TryGetValue(r, out Material[] transMats))
                {
                    r.sharedMaterials = transMats;
                }
            }
        }
    }
}