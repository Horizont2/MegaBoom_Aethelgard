using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ResourceDrop : MonoBehaviour
{
    public enum ResourceType { Wood, Stone, Food, Diamond }

    [Header("Drop Settings")]
    public ResourceType resourceType;
    public int amount = 1;
    public float popForce = 6f;

    [Header("Idle Animation")]
    public float spinSpeed = 120f;

    [Header("Magnet Settings")]
    public float magnetSpeed = 20f;
    private bool isMagnetizing = false;

    private Transform player;
    private PlayerController playerController;
    private Rigidbody rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();

        Vector3 randomDir = new Vector3(Random.Range(-1f, 1f), 2f, Random.Range(-1f, 1f)).normalized;
        rb.AddForce(randomDir * popForce, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * popForce * 2f, ForceMode.Impulse);

        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerController = p.GetComponent<PlayerController>();
        }

        Invoke(nameof(StartSpinning), 1.5f);
    }

    private void StartSpinning()
    {
        if (!isMagnetizing)
        {
            rb.isKinematic = true;
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }
    }

    private void Update()
    {
        if (player == null || playerController == null) return;

        if (rb.isKinematic && !isMagnetizing)
        {
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        }

        float dist = Vector3.Distance(transform.position, player.position);

        if (!isMagnetizing && dist <= playerController.pickupRadius)
        {
            isMagnetizing = true;
            rb.isKinematic = true;
            Collider col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        if (isMagnetizing)
        {
            transform.position = Vector3.MoveTowards(transform.position, player.position + Vector3.up, magnetSpeed * Time.deltaTime);

            if (Vector3.Distance(transform.position, player.position + Vector3.up) < 0.5f)
            {
                Collect();
            }
        }
    }

    private bool isCollected = false;

    private void Collect()
    {
        // Guard against double-collection. Destroy() is deferred to end-of-frame,
        // so without this a drop that lingered a frame (physics settle, low FPS,
        // a second magnet tick) could add its resources more than once — the
        // "one node fills the whole backpack" bug.
        if (isCollected) return;
        isCollected = true;

        if (ResourceManager.Instance != null)
        {
            // ResourceManager.AddRunResources now fires its own toast
            // through GlobalHUD, so we no longer call ShowPickupPopup
            // here — that produced two stacking toasts per pickup.
            if (resourceType == ResourceType.Wood)
            {
                ResourceManager.Instance.AddRunResources(amount, 0, 0);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Camp_CollectItem);
            }
            else if (resourceType == ResourceType.Stone)
            {
                ResourceManager.Instance.AddRunResources(0, amount, 0);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Camp_CollectItem);
            }
            else if (resourceType == ResourceType.Food)
            {
                ResourceManager.Instance.AddRunResources(0, 0, amount);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Camp_CollectItem);
            }
            else if (resourceType == ResourceType.Diamond)
            {
                playerController.GainDiamond(amount);
            }
        }

        Destroy(gameObject);
    }
}