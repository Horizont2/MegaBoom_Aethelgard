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

    // �������� ��� ���������� ����������� �� �������� ����� ��������
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
            // �������� ������ ������
            if (r is ParticleSystemRenderer) continue;

            originalMaterials[r] = r.sharedMaterials;
            Material[] transMats = new Material[r.sharedMaterials.Length];

            for (int i = 0; i < r.sharedMaterials.Length; i++)
            {
                Material orig = r.sharedMaterials[i];
                if (orig == null) continue;

                Material transMat = new Material(orig);

                // ������� URP ������� ����������� ���������
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

    // Dormant = fully opaque and idle. We keep the component + its cloned
    // transparent materials alive (instead of Destroy-ing on every fade-in)
    // so walking back and forth through the same trees doesn't re-clone all
    // their materials each time — that repeated AddComponent + material clone
    // + Destroy churn was a steady GC / hitching source in wooded areas.
    private bool isDormant = false;

    public void FadeOut()
    {
        isFadingOut = true;
        isDormant = false;
    }

    public void FadeIn()
    {
        isFadingOut = false;
    }

    private void Update()
    {
        if (!isInitialized || isDormant) return;

        float target = isFadingOut ? fadeTargetAlpha : 1f;

        if (Mathf.Abs(currentAlpha - target) > 0.01f)
        {
            currentAlpha = Mathf.Lerp(currentAlpha, target, Time.deltaTime * fadeSpeed);
            ApplyAlpha(currentAlpha);
        }
        else if (!isFadingOut)
        {
            // Fully opaque again — snap to 1, restore the original (batchable)
            // shared materials, and go dormant. We do NOT Destroy: the clones
            // stay cached so the next FadeOut reuses them instantly.
            currentAlpha = 1f;
            RestoreOriginalMaterials();
            isDormant = true;
        }
    }

    private void ApplyAlpha(float alpha)
    {
        foreach (Renderer r in renderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;

            // �������� ����²���: ������������� TryGetValue, ��� �������� KeyNotFoundException
            if (transparentMaterials.TryGetValue(r, out Material[] transMats))
            {
                if (r.sharedMaterials != transMats)
                {
                    r.sharedMaterials = transMats;
                }

                // sharedMaterials allocates a fresh Material[] on EVERY read,
                // and this read it again immediately after the assignment above
                // — two array allocations per renderer per frame while a tree is
                // mid-fade. transMats is the same array.
                foreach (Material mat in transMats)
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
            // ����������� �������� ��������
            if (r != null && originalMaterials.TryGetValue(r, out Material[] origMats))
            {
                r.sharedMaterials = origMats;
            }
        }
    }

    // --- ��ò� ��� ̲Ͳ���� ---

    private void OnEnable()
    {
        // ϳ��������� �� ��䳿 ���������� ����� URP
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

        // �������: ������� �������� ����� ��������, ��� ���������� ���'��� �� ���������������
        foreach (var kvp in transparentMaterials)
        {
            foreach (Material mat in kvp.Value)
            {
                if (mat != null) Destroy(mat);
            }
        }
        transparentMaterials.Clear();
    }

    // ==== camera.name IS A NATIVE GETTER, AND IT ALLOCATES ====
    //
    // CameraOcclusion adds a FadingObject to every tree the camera has ever
    // passed behind and never removes it — the component goes dormant instead
    // of being destroyed. Each one subscribes to beginCameraRendering AND
    // endCameraRendering, and both cameras raise both events, so the multicast
    // delegate dispatches four handlers per object per frame. N only ever grows.
    //
    // Each handler compared camera.name against a string literal, and a Camera
    // name getter marshals a fresh managed string out of native on every call.
    // At a couple of hundred faders that is on the order of a thousand string
    // allocations a frame, purely to answer "is this the minimap".
    //
    // Resolved once and compared by reference afterwards. A null result simply
    // means there is no minimap camera, which is the correct answer in the camp.
    private static Camera s_minimapCam;

    private static bool IsMinimap(Camera cam)
    {
        if (cam == null) return false;
        if (s_minimapCam != null) return cam == s_minimapCam;
        if (cam.name == "MinimapCamera") { s_minimapCam = cam; return true; }
        return false;
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // ����� ��� �� MinimapCamera ����� ��������, ��������� ������ ���������� �������� ��������
        if (IsMinimap(camera) && currentAlpha < 1f)
        {
            RestoreOriginalMaterials();
        }
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // ϲ��� ���� �� ���� �����������, ��������� ������ ������������ ���� �����
        if (IsMinimap(camera) && currentAlpha < 1f && isInitialized)
        {
            foreach (Renderer r in renderers)
            {
                // ����������� �������� ��������
                if (r != null && transparentMaterials.TryGetValue(r, out Material[] transMats))
                {
                    r.sharedMaterials = transMats;
                }
            }
        }
    }
}