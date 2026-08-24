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

    [Header("Look")]
    [Tooltip("Corrupt crystal tint.")]
    public Color crystalColor = new Color(0.30f, 0.06f, 0.40f);
    [Tooltip("Emission tint (before intensity). Kept calm — the anchor pulses instead of glowing at full blast.")]
    public Color glowColor = new Color(0.55f, 0.15f, 0.85f);
    [Tooltip("Peak emission intensity of the pulse. Was 2.2 (too bright).")]
    public float glowIntensity = 0.9f;

    private float health;
    private bool dead;
    private Transform _visual;
    private Renderer _rend;                       // primary shard (kept for flash)
    private readonly System.Collections.Generic.List<Renderer> _shards = new System.Collections.Generic.List<Renderer>();
    private Light _light;
    private float _lightBaseIntensity;
    private Color _baseEmission;
    private bool _flashing;

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

        // Procedural corruption anchor: a dark stone base with a small cluster of
        // faceted crystal shards, a calm PULSING glow (not a constant blast) and a
        // soft point light. Reads clearly as an objective without hurting the eyes.
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // Container that hovers + spins.
        var root = new GameObject("AnchorVisual");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        _visual = root.transform;

        // --- dark jagged stone base ---
        var baseMat = new Material(lit);
        baseMat.color = new Color(0.09f, 0.07f, 0.11f);
        for (int i = 0; i < 3; i++)
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(rock.GetComponent<Collider>());
            rock.transform.SetParent(root.transform, false);
            float a = i * 120f + Random.Range(-15f, 15f);
            rock.transform.localPosition = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * 0.35f, -0.9f + i * 0.12f, Mathf.Sin(a * Mathf.Deg2Rad) * 0.35f);
            rock.transform.localRotation = Quaternion.Euler(Random.Range(-25f, 25f), a, Random.Range(-25f, 25f));
            rock.transform.localScale = new Vector3(0.9f - i * 0.15f, 0.5f, 0.9f - i * 0.15f);
            rock.GetComponent<MeshRenderer>().sharedMaterial = baseMat;
        }

        // --- crystal shards (faceted look via rotated, elongated cubes) ---
        var crystalMat = new Material(lit);
        crystalMat.color = crystalColor;
        crystalMat.EnableKeyword("_EMISSION");
        _baseEmission = glowColor * glowIntensity;
        crystalMat.SetColor("_EmissionColor", _baseEmission);

        // Central tall shard + three smaller leaning ones.
        SpawnShard(root.transform, crystalMat, new Vector3(0f, 0.9f, 0f), Quaternion.Euler(0f, 45f, 0f), new Vector3(0.34f, 1.7f, 0.34f));
        for (int i = 0; i < 3; i++)
        {
            float a = i * 120f + 30f;
            Vector3 pos = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * 0.45f, 0.35f, Mathf.Sin(a * Mathf.Deg2Rad) * 0.45f);
            Quaternion rot = Quaternion.Euler(Random.Range(18f, 34f), a, 45f);
            SpawnShard(root.transform, crystalMat, pos, rot, new Vector3(0.22f, 1.0f, 0.22f));
        }
        _rend = _shards.Count > 0 ? _shards[0] : null;

        // --- soft corrupt light (low intensity; pulses in Update) ---
        var lgo = new GameObject("AnchorLight");
        lgo.transform.SetParent(root.transform, false);
        lgo.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        _light = lgo.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = glowColor;
        _light.range = 6f;
        _light.intensity = 1.1f;
        _light.shadows = LightShadows.None;
        _lightBaseIntensity = _light.intensity;

        // Collider on the root so weapons hit it.
        var col = gameObject.AddComponent<CapsuleCollider>();
        col.height = 3.4f; col.radius = 0.9f; col.center = new Vector3(0f, 1.7f, 0f);
    }

    private void SpawnShard(Transform parent, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(s.GetComponent<Collider>());
        s.transform.SetParent(parent, false);
        s.transform.localPosition = pos;
        s.transform.localRotation = rot;
        s.transform.localScale = scale;
        var r = s.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _shards.Add(r);
    }

    private void Update()
    {
        if (_visual != null)
        {
            _visual.Rotate(0f, 30f * Time.deltaTime, 0f, Space.World);
            Vector3 p = _visual.localPosition;
            p.y = 1.2f + Mathf.Sin(Time.time * 1.5f) * 0.15f;
            _visual.localPosition = p;
        }

        // Calm breathing pulse instead of a constant harsh glow.
        if (!_flashing)
        {
            float pulse = 0.65f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f));
            if (_rend != null) _rend.sharedMaterial.SetColor("_EmissionColor", _baseEmission * pulse);
            if (_light != null) _light.intensity = _lightBaseIntensity * pulse;
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
        _flashing = true;
        _rend.sharedMaterial.SetColor("_EmissionColor", Color.white * 2f); // brief hit pop
        yield return new WaitForSeconds(0.06f);
        if (_rend != null) _rend.sharedMaterial.SetColor("_EmissionColor", _baseEmission);
        _flashing = false;
    }

    private void Die()
    {
        if (dead) return;
        dead = true;
        if (destroyVFXPrefab != null) Instantiate(destroyVFXPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Region_AnchorDestroy, transform.position);
        CameraShakeUtil.TryShake(0.25f, 0.1f);
        onDestroyed?.Invoke(this);
        Destroy(gameObject);
    }
}
