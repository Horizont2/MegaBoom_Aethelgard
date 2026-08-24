using UnityEngine;
using System.Collections.Generic;

// Roadside caged-ally event. A captured mercenary sits in a cage guarded by a
// pack of skeletons. When the player comes near, the guards spawn and aggro;
// clearing every guard shatters the cage and frees the ally, who then fights
// for the player (via AllyAI). A one-shot side objective — great for the dead
// ends of roads alongside totems.
//
// Setup: place this on an empty at the road end. Assign guardPrefabs (skeletons),
// the cageObject (cage mesh), and allyObject — a barracks-unit prefab that has
// an AllyAI component and starts INACTIVE (or the AllyAI disabled). On clear the
// ally is activated/enabled and starts helping.
public class CagedAllyEvent : MonoBehaviour
{
    [Header("Guards")]
    public GameObject[] guardPrefabs;
    public int guardCount = 5;
    public float guardSpawnRadius = 4f;
    [Tooltip("Extra HP/damage multiplier for the guards (1 = base).")]
    public float guardStatMult = 1f;

    [Header("Cage & Ally")]
    [Tooltip("Cage mesh — hidden (and VFX played) when the event is cleared. Leave empty to build a procedural bone cage (no asset needed).")]
    public GameObject cageObject;
    public GameObject cageBreakVFXPrefab;
    [Tooltip("The captive unit ALREADY in the scene. Should carry an AllyAI and start inactive; freed on clear. Leave empty and use Ally Prefab to spawn one instead.")]
    public GameObject allyObject;
    [Tooltip("Captive to SPAWN inside the cage (used when Ally Object is empty). Any humanoid prefab — an AllyAI is added automatically if missing.")]
    public GameObject allyPrefab;
    [Tooltip("Build a procedural cage from primitives when no Cage Object is assigned.")]
    public bool buildProceduralCage = true;
    public Color cageBarColor = new Color(0.32f, 0.28f, 0.22f);

    [Header("Trigger")]
    public float triggerRadius = 10f;

    [Header("Rewards")]
    public int diamondReward = 10;
    public int xpReward = 20;

    private bool started = false;
    private bool cleared = false;
    private bool promptShowing = false;
    private readonly List<GameObject> guards = new List<GameObject>();
    private Transform player;

    private void Start()
    {
        CachePlayer();

        // Spawn the captive from a prefab if none was placed by hand.
        if (allyObject == null && allyPrefab != null)
        {
            Vector3 p = transform.position; p.y = GroundY(p);
            allyObject = Instantiate(allyPrefab, p, Quaternion.identity);
        }

        // Keep the captive inert (and non-hostile) until freed.
        if (allyObject != null)
        {
            // If it's a humanoid prefab without AllyAI, add one so it can fight.
            AllyAI ai = allyObject.GetComponent<AllyAI>();
            if (ai == null) ai = allyObject.AddComponent<AllyAI>();
            ai.enabled = false;
            // A captive spawned from an ENEMY prefab must not attack the player —
            // disable any EnemyAI on it while caged/allied.
            EnemyAI foe = allyObject.GetComponent<EnemyAI>();
            if (foe != null) foe.enabled = false;
        }

        // Build a procedural cage around the captive if no mesh was assigned.
        if (cageObject == null && buildProceduralCage)
            cageObject = BuildProceduralCage(transform.position);
    }

    // A simple bone/wood cage from primitives — a ring of vertical bars plus a
    // top and bottom hoop. No external asset required.
    private GameObject BuildProceduralCage(Vector3 center)
    {
        center.y = GroundY(center);
        GameObject cage = new GameObject("ProceduralCage");
        cage.transform.position = center;

        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        if (mat != null) mat.color = cageBarColor;

        const int bars = 8;
        const float radius = 1.1f;
        const float height = 2.6f;
        for (int i = 0; i < bars; i++)
        {
            float ang = (Mathf.PI * 2f / bars) * i;
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(bar.GetComponent<Collider>()); // bars are decorative; guards block the player
            bar.transform.SetParent(cage.transform, false);
            bar.transform.localPosition = new Vector3(Mathf.Cos(ang) * radius, height * 0.5f, Mathf.Sin(ang) * radius);
            bar.transform.localScale = new Vector3(0.08f, height * 0.5f, 0.08f);
            if (mat != null) bar.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
        // Top + bottom hoops (flattened tori faked with thin cylinders in a ring).
        for (int ring = 0; ring < 2; ring++)
        {
            float y = ring == 0 ? 0.15f : height - 0.1f;
            int seg = 12;
            for (int i = 0; i < seg; i++)
            {
                float a0 = (Mathf.PI * 2f / seg) * i;
                GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(piece.GetComponent<Collider>());
                piece.transform.SetParent(cage.transform, false);
                piece.transform.localPosition = new Vector3(Mathf.Cos(a0) * radius, y, Mathf.Sin(a0) * radius);
                piece.transform.localRotation = Quaternion.Euler(0f, -a0 * Mathf.Rad2Deg, 0f);
                piece.transform.localScale = new Vector3(0.6f, 0.08f, 0.08f);
                if (mat != null) piece.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }
        return cage;
    }

    private void CachePlayer()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
    }

    private void Update()
    {
        if (cleared) return;
        if (player == null) { CachePlayer(); if (player == null) return; }

        if (!started)
        {
            if (FlatDist(transform.position, player.position) <= triggerRadius)
                BeginEvent();
            return;
        }

        guards.RemoveAll(g => g == null);
        if (guards.Count == 0) Clear();
    }

    private void BeginEvent()
    {
        started = true;

        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("FREE THE CAPTIVE — DEFEAT THE GUARDS!"));
            promptShowing = true;
        }

        if (guardPrefabs == null || guardPrefabs.Length == 0) { Clear(); return; }

        for (int i = 0; i < guardCount; i++)
        {
            GameObject prefab = guardPrefabs[Random.Range(0, guardPrefabs.Length)];
            if (prefab == null) continue;

            Vector2 c = Random.insideUnitCircle.normalized * Random.Range(guardSpawnRadius * 0.5f, guardSpawnRadius);
            Vector3 pos = transform.position + new Vector3(c.x, 0f, c.y);
            pos.y = GroundY(pos);

            GameObject g = Instantiate(prefab, pos, Quaternion.identity);
            if (guardStatMult != 1f)
            {
                EnemyAI ai = g.GetComponent<EnemyAI>();
                if (ai != null) { ai.maxHealth *= guardStatMult; ai.damage *= guardStatMult; }
            }
            guards.Add(g);
        }
    }

    private void Clear()
    {
        if (cleared) return;
        cleared = true;

        if (promptShowing && GlobalHUD.Instance != null) { GlobalHUD.Instance.HidePrompt(); promptShowing = false; }

        // Shatter the cage.
        if (cageObject != null)
        {
            if (cageBreakVFXPrefab != null) Instantiate(cageBreakVFXPrefab, cageObject.transform.position, Quaternion.identity);
            cageObject.SetActive(false);
        }
        CameraShakeUtil.TryShake(0.25f, 0.1f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Totem_Activate, transform.position);

        // Free the ally.
        if (allyObject != null)
        {
            allyObject.SetActive(true);
            AllyAI ai = allyObject.GetComponent<AllyAI>();
            if (ai != null) ai.enabled = true;
        }

        // Payout.
        if (ResourceManager.Instance != null && diamondReward > 0) ResourceManager.Instance.AddDiamonds(diamondReward);
        if (player != null)
        {
            PlayerController pc = player.GetComponent<PlayerController>();
            if (pc != null && xpReward > 0) pc.GainXP(xpReward);
        }
    }

    private static float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    private float GroundY(Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 60f, ~(1 << 9)))
            return hit.point.y;
        if (Terrain.activeTerrain != null)
            return Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;
        return pos.y;
    }
}
