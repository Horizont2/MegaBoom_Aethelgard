using UnityEngine;
using System.Collections;

public class DiamondPickup : MonoBehaviour
{
    public int diamondAmount = 1;

    [Header("Smart Magnet AI")]
    public float maxMagnetSpeed = 35f;
    public float acceleration = 20f;
    public float dropOffMultiplier = 1.8f;
    public float timeBeforeMagnet = 0.8f; // Час, поки діамант розлітається і падає

    [Header("Hover Animation")]
    public float rotationSpeed = 150f;

    private Transform player;
    private PlayerController playerController;
    private bool isMagnetized = false;
    private bool canBeMagnetized = false;
    private float currentFlySpeed = 0f;

    private float pickupRadiusSqr;
    private float dropOffRadiusSqr;

    private Collider col;
    private Rigidbody rb;
    private Renderer[] renderers;

    private void Awake()
    {
        // Встановлюємо потрібний шар для фізики
        gameObject.layer = 9;
        int minimapLayer = LayerMask.NameToLayer("MinimapOnly");

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.gameObject.layer != minimapLayer) t.gameObject.layer = 9;
        }

        col = GetComponent<Collider>();
        rb = GetComponent<Rigidbody>();
        renderers = GetComponentsInChildren<Renderer>();
    }

    private void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerController = p.GetComponent<PlayerController>();

            // Математична оптимізація (використовуємо квадрати дистанцій)
            if (playerController != null)
            {
                pickupRadiusSqr = playerController.pickupRadius * playerController.pickupRadius;
                dropOffRadiusSqr = (playerController.pickupRadius * dropOffMultiplier) * (playerController.pickupRadius * dropOffMultiplier);
            }
        }

        // --- НОВЕ: Фізичний вибух (Фонтан) ---
        if (rb != null)
        {
            rb.isKinematic = false;
            // Кидаємо діамант випадково вгору і вбік
            Vector3 randomDir = new Vector3(Random.Range(-1f, 1f), Random.Range(1.5f, 2.5f), Random.Range(-1f, 1f)).normalized;
            rb.AddForce(randomDir * Random.Range(5f, 9f), ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 50f, ForceMode.Impulse);
        }

        StartCoroutine(WaitBeforeMagnetRoutine());
    }

    private IEnumerator WaitBeforeMagnetRoutine()
    {
        yield return new WaitForSeconds(timeBeforeMagnet);
        canBeMagnetized = true; // Після падіння дозволяємо магнітитись
    }

    private void Update()
    {
        // Якщо ще летить/падає або гравця немає - просто крутимось на землі
        if (player == null || playerController == null || !canBeMagnetized)
        {
            transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime, Space.World);
            return;
        }

        float distSqr = (transform.position - player.position).sqrMagnitude;

        // Магнітимо, якщо гравець близько
        if (!isMagnetized && distSqr <= pickupRadiusSqr)
        {
            isMagnetized = true;
            currentFlySpeed = 0f;

            if (col != null) col.enabled = false;
            if (rb != null) rb.isKinematic = true; // Вимикаємо гравітацію для польоту
        }
        else if (isMagnetized && distSqr > dropOffRadiusSqr)
        {
            isMagnetized = false;
            if (col != null) col.enabled = true;
            if (rb != null) rb.isKinematic = false;
        }

        if (isMagnetized)
        {
            // Плавне прискорення польоту до гравця
            currentFlySpeed = Mathf.Lerp(currentFlySpeed, maxMagnetSpeed, Time.deltaTime * acceleration);
            Vector3 targetPos = player.position + Vector3.up * 1f;

            transform.position = Vector3.MoveTowards(transform.position, targetPos, currentFlySpeed * Time.deltaTime);

            if ((transform.position - targetPos).sqrMagnitude < 0.25f)
            {
                playerController.GainDiamond(diamondAmount);

                // Ховаємо об'єкт і знищуємо із затримкою
                foreach (Renderer r in renderers) if (r != null) r.enabled = false;
                this.enabled = false;
                Destroy(gameObject, 2f);
            }
        }
        else
        {
            transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime, Space.World);
        }
    }
}