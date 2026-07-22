using UnityEngine;

public class OptimizedObject : MonoBehaviour
{
    [Header("Components to Disable")]
    public MeshRenderer[] renderers;
    public Animator animator;
    public Light[] lights;
    public ParticleSystem[] particles;
    // NEW: colliders are toggled alongside renderers when the object is
    // culled. If left empty, the script AUTO-FILLS from GetComponentsIn-
    // Children so every existing prop starts respecting the invisible-
    // wall fix without a re-wiring pass. Non-null explicit assignment
    // still overrides.
    public Collider[] colliders;
    // When TRUE (default), a culled prop's colliders are disabled with
    // its renderers — kills invisible walls in the distance. Turn OFF
    // only for props the player MUST still bump into even when out of
    // sight (rare — usually only fences or map boundaries).
    public bool disableCollidersWhenHidden = true;

    [HideInInspector] public bool isCurrentlyVisible = true;

    private void OnEnable()
    {
        // Auto-fill colliders if the designer didn't wire them — this
        // means every existing prop gets the invisible-wall fix for
        // free, without a re-authoring pass on 100+ prefabs.
        if (colliders == null || colliders.Length == 0)
        {
            colliders = GetComponentsInChildren<Collider>(true);
        }
        // ���������� ���� ���� ���������� ��� ����
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
        // ³��������� ��� �������� (���������, ����� �����)
        if (DistanceOptimizer.Instance != null)
        {
            DistanceOptimizer.Instance.UnregisterObject(this);
        }
    }

    // ������ SetActive(false/true) �������� ���� ������ � �����
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

        // Toggle colliders alongside renderers — otherwise a culled prop
        // becomes an invisible wall. This was the true root cause of the
        // 'invisible collider in an empty field' bug: props past
        // DistanceOptimizer's disableDistance dropped their meshes but
        // kept their colliders, leaving invisible boxes the player
        // couldn't walk through.
        if (disableCollidersWhenHidden && colliders != null)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = state;
            }
        }
    }
}