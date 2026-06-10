using UnityEngine;

public class OptimizedObject : MonoBehaviour
{
    [Header("Components to Disable")]
    public MeshRenderer[] renderers;
    public Animator animator;
    public Light[] lights;
    public ParticleSystem[] particles;

    [HideInInspector] public bool isCurrentlyVisible = true;

    private void OnEnable()
    {
        // Реєструємось лише якщо Оптимізатор вже існує
        if (DistanceOptimizer.Instance != null)
        {
            DistanceOptimizer.Instance.RegisterObject(this);
        }
    }

    private void OnDisable()
    {
        if (DistanceOptimizer.Instance != null)
        {
            DistanceOptimizer.Instance.UnregisterObject(this);
        }
    }

    private void OnDestroy()
    {
        // Відписуємось при знищенні (наприклад, ворог помер)
        if (DistanceOptimizer.Instance != null)
        {
            DistanceOptimizer.Instance.UnregisterObject(this);
        }
    }

    // Замість SetActive(false/true) вимикаємо лише рендер і логіку
    public void SetVisibility(bool state)
    {
        if (isCurrentlyVisible == state) return;
        isCurrentlyVisible = state;

        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = state;
        }

        if (animator != null) animator.enabled = state;

        if (lights != null)
        {
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].enabled = state;
        }

        if (particles != null)
        {
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] == null) continue;
                if (state) particles[i].Play(); else particles[i].Stop();
            }
        }
    }
}