using System.Collections;
using UnityEngine;

// A destructible "corruption anchor" the player must break to begin a region
// raid capture. Implements IDamageable so the player's melee / weapons damage it
// like any target. If no visual prefab is supplied it builds a glowing, hovering
// crystal procedurally, so it always reads as an objective. Fires onDestroyed
// when its health hits zero.
public class CorruptionAnchor : MonoBehaviour, IDamageable
{
    public float maxHealth = 120f;
    public GameObject hitVFXPrefab;
    public GameObject destroyVFXPrefab;

    public System.Action<CorruptionAnchor> onDestroyed;

    private float health;
    private bool dead;
    private Transform _visual;
    private Renderer _rend;
    private Color _baseEmission;

    public void Setup(float hp)
    {
        maxHealth = Mathf.Max(1f, hp);
        health = maxHealth;
    }

    private void Awake()
    {
        if (health <= 0f) health = maxHealth;
        EnsureVisualAndCollider();
    }

    private void EnsureVisualAndCollider()
    {
        // If the prefab already carries a mesh + collider, leave it be.
        if (GetComponentInChildren<Renderer>() != null)
        {
            _rend = GetComponentInChildren<Renderer>();
            if (GetComponentInChildren<Collider>() == null)
            {
                var c = gameObject.AddComponent<CapsuleCollider>();
                c.height = 3f; c.radius = 0.8f; c.center = new Vector3(0, 1.5f, 0);
            }
            return;
        }

        // Procedural crystal: a stretched, tilted octahedron-ish capsule with a
        // corrupt purple emissive glow, hovering + spinning.
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "AnchorCrystal";
        go.transform.SetParent(transform, false);
        go.transform.localScale = new Vector3(1.1f, 1.6f, 1.1f);
        go.transform.localPosition = new Vector3(0f, 1.7f, 0f);
        go.transform.localRotation = Quaternion.Euler(18f, 0f, 12f);
        _visual = go.transform;
        _rend = go.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.color = new Color(0.32f, 0.05f, 0.42f);
        mat.EnableKeyword("_EMISSION");
        _baseEmission = new Color(0.55f, 0.1f, 0.85f) * 2.2f;
        mat.SetColor("_EmissionColor", _baseEmission);
        _rend.material = mat;

        // Collider on the root so weapons hit it.
        var col = gameObject.AddComponent<CapsuleCollider>();
        col.height = 3.4f; col.radius = 0.9f; col.center = new Vector3(0f, 1.7f, 0f);
    }

    private void Update()
    {
        if (_visual != null)
        {
            _visual.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);
            Vector3 p = _visual.localPosition;
            p.y = 1.7f + Mathf.Sin(Time.time * 1.6f) * 0.18f;
            _visual.localPosition = p;
        }
    }

    public void TakeDamage(DamageInfo info)
    {
        if (dead) return;
        health -= info.Amount;

        if (hitVFXPrefab != null) Instantiate(hitVFXPrefab, transform.position + Vector3.up * 1.7f, Quaternion.identity);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Player_HitResource, transform.position);
        StartCoroutine(FlashRoutine());

        if (health <= 0f) Die();
    }

    private IEnumerator FlashRoutine()
    {
        if (_rend == null) yield break;
        _rend.material.SetColor("_EmissionColor", Color.white * 3f);
        yield return new WaitForSeconds(0.06f);
        if (_rend != null) _rend.material.SetColor("_EmissionColor", _baseEmission);
    }

    private void Die()
    {
        if (dead) return;
        dead = true;
        if (destroyVFXPrefab != null) Instantiate(destroyVFXPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Attack, transform.position);
        CameraShakeUtil.TryShake(0.25f, 0.1f);
        onDestroyed?.Invoke(this);
        Destroy(gameObject);
    }
}
