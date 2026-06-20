using UnityEngine;
using System.Collections;

public class DiamondPickup : MonoBehaviour
{
    public int diamondAmount = 1;

    [Header("AAA Flight Physics (Bezier Curve)")]
    public float minFlightTime = 0.4f;
    public float maxFlightTime = 0.65f;
    public float arcHeight = 4f;
    public float arcWidth = 3f;

    [Header("Juice Spawning (Pop & Hover)")]
    public float popDuration = 0.6f;
    public float popHeight = 2.5f;
    public float popDistance = 1.8f;
    public float hoverSpeed = 2f;
    public float hoverAmplitude = 0.15f;

    public bool IsMagnetized { get { return isMagnetized; } }

    private static Transform s_cachedPlayer;
    private static PlayerController s_cachedPlayerController;

    private Transform player;
    private PlayerController playerController;
    private bool isMagnetized = false;
    private bool canBeMagnetized = false;

    private Collider col;
    private Renderer[] renderers;
    private TrailRenderer trail;
    private float hoverStartY;

    private void Awake()
    {
        gameObject.layer = 9;
        int minimapLayer = LayerMask.NameToLayer("MinimapOnly");
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.gameObject.layer != minimapLayer) t.gameObject.layer = 9;
        }

        col = GetComponent<Collider>();
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        renderers = GetComponentsInChildren<Renderer>();

        trail = GetComponent<TrailRenderer>();
        if (trail == null) trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.startWidth = 0.15f;
        trail.endWidth = 0f;
        trail.emitting = false;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.startColor = new Color(0.8f, 0.2f, 1f, 0.7f); // ĳ������ ����� ���������� ���
        trail.endColor = new Color(0.8f, 0.2f, 1f, 0f);
    }

    private void OnEnable()
    {
        // Reset pickup state on every spawn (matters for pool reuse — without this
        // a re-spawned diamond stays magnetized=true and never picks up)
        isMagnetized = false;
        canBeMagnetized = false;
        if (trail != null) trail.emitting = false;
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = true;
        }

        if (s_cachedPlayer == null || s_cachedPlayerController == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null)
            {
                s_cachedPlayer = p.transform;
                s_cachedPlayerController = p.GetComponent<PlayerController>();
            }
        }
        player = s_cachedPlayer;
        playerController = s_cachedPlayerController;

        StartCoroutine(PopSpawnRoutine());
    }

    private IEnumerator PopSpawnRoutine()
    {
        if (col != null) col.enabled = false;

        Vector3 startPos = transform.position;
        Vector2 randomCircle = Random.insideUnitCircle.normalized * Random.Range(popDistance * 0.5f, popDistance);
        Vector3 targetPos = startPos + new Vector3(randomCircle.x, 0, randomCircle.y);

        targetPos.y = GetGroundHeight(targetPos) + 0.4f;

        float elapsed = 0f;
        while (elapsed < popDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / popDuration;
            float easeT = t * (2f - t);

            Vector3 currentPos = Vector3.Lerp(startPos, targetPos, easeT);
            currentPos.y += Mathf.Sin(t * Mathf.PI) * popHeight;

            transform.position = currentPos;
            transform.Rotate(Vector3.up * 360f * Time.deltaTime, Space.World);

            yield return null;
        }

        transform.position = targetPos;
        hoverStartY = targetPos.y;

        if (col != null) col.enabled = true;
        canBeMagnetized = true;
    }

    private float GetGroundHeight(Vector3 pos)
    {
        if (Terrain.activeTerrain != null)
            return Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;

        return pos.y;
    }

    private void Update()
    {
        if (!isMagnetized && canBeMagnetized)
        {
            float newY = hoverStartY + Mathf.Sin(Time.time * hoverSpeed) * hoverAmplitude;
            transform.position = new Vector3(transform.position.x, newY, transform.position.z);
            transform.Rotate(Vector3.up * 90f * Time.deltaTime, Space.World);
        }

        if (player == null || playerController == null || !canBeMagnetized) return;

        if (!isMagnetized)
        {
            float distSqr = (transform.position - player.position).sqrMagnitude;
            if (distSqr <= playerController.pickupRadius * playerController.pickupRadius)
            {
                StartCoroutine(FlyToPlayerRoutine());
            }
        }
    }

    private IEnumerator FlyToPlayerRoutine()
    {
        isMagnetized = true;
        if (col != null) col.enabled = false;
        if (trail != null) trail.emitting = true;

        Vector3 startPos = transform.position;
        float flightTime = Random.Range(minFlightTime, maxFlightTime);
        float elapsed = 0f;

        Vector3 initialDirToPlayer = (player.position - startPos).normalized;
        initialDirToPlayer.y = 0;

        Vector3 rightDir = Vector3.Cross(initialDirToPlayer, Vector3.up).normalized;
        float randomSide = Random.value > 0.5f ? Random.Range(arcWidth * 0.5f, arcWidth) : Random.Range(-arcWidth, -arcWidth * 0.5f);

        Vector3 controlPoint = startPos + Vector3.up * Random.Range(arcHeight * 0.7f, arcHeight) + rightDir * randomSide - initialDirToPlayer * 1.5f;

        while (elapsed < flightTime)
        {
            if (player == null) break;

            elapsed += Time.deltaTime;
            float t = elapsed / flightTime;
            float curveT = t * t * t;

            Vector3 targetPos = player.position + Vector3.up * 1.2f;

            Vector3 m1 = Vector3.Lerp(startPos, controlPoint, curveT);
            Vector3 m2 = Vector3.Lerp(controlPoint, targetPos, curveT);
            transform.position = Vector3.Lerp(m1, m2, curveT);

            transform.Rotate(Vector3.up * 1500f * Time.deltaTime, Space.World);

            yield return null;
        }

        playerController.GainDiamond(diamondAmount);

        if (TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("DiamondPickup",
                "Diamonds are persistent currency. Carry them out alive — they're spent in the Shop on weapons, armor, and meta-upgrades.", 5f);

        foreach (Renderer r in renderers) if (r != null) r.enabled = false;
        this.enabled = false;

        if (ObjectPoolManager.Instance != null) ObjectPoolManager.Instance.ReturnToPool(gameObject);
        else Destroy(gameObject, 2f);
    }
}