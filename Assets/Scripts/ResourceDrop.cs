using System.Collections.Generic;
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
    [Tooltip("A drop this old is collected on contact regardless of the pickup radius. The failsafe against one wedging somewhere it can never be reached from.")]
    public float giveUpAfter = 25f;
    private bool isMagnetizing = false;

    private Transform player;
    private PlayerController playerController;
    private Rigidbody rb;
    private readonly List<Collider> myColliders = new List<Collider>(4);
    private float bornAt;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        bornAt = Time.time;

        // EVERY collider, including children. The old code took
        // GetComponent<Collider>() — the root one only — so a drop whose collider
        // sits on a child model kept a live, solid collider for its whole life.
        // That is the one that jams against the player's capsule and hangs there:
        // physics will not let it through, the magnet moves it by transform so it
        // cannot push past either, and it never reaches the collect distance.
        GetComponentsInChildren(true, myColliders);

        Vector3 randomDir = new Vector3(Random.Range(-1f, 1f), 2f, Random.Range(-1f, 1f)).normalized;
        rb.AddForce(randomDir * popForce, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * popForce * 2f, ForceMode.Impulse);

        AcquirePlayer();
        Invoke(nameof(StartSpinning), 1.5f);
    }

    // The player may not exist yet when a drop is created — chests and nodes can
    // spawn during a load or a cinematic. Start used to look once and give up,
    // and a drop that missed simply hung forever: Update returns immediately
    // without a player, so it never span, never magnetised and never paid out.
    private void AcquirePlayer()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p == null) return;

        player = p.transform;
        // In parents AND children: the tag can sit on a collider proxy or on a
        // rig root while the controller lives one level away, and a plain
        // GetComponent silently returns null in both cases.
        if (playerController == null) playerController = p.GetComponent<PlayerController>();
        if (playerController == null) playerController = p.GetComponentInParent<PlayerController>();
        if (playerController == null) playerController = p.GetComponentInChildren<PlayerController>();
        if (playerController == null) playerController = FindFirstObjectByType<PlayerController>();

        // Never collide with the player at all. A pickup is not an obstacle, and
        // letting one rest against the capsule is how it ends up parked in mid-air
        // on the player's shoulder.
        foreach (var mine in myColliders)
        {
            if (mine == null) continue;
            foreach (var theirs in p.GetComponentsInChildren<Collider>(true))
            {
                if (theirs == null) continue;
                Physics.IgnoreCollision(mine, theirs, true);
            }
        }
    }

    private void StartSpinning()
    {
        if (!isMagnetizing)
        {
            rb.isKinematic = true;
            foreach (var c in myColliders) if (c != null) c.isTrigger = true;
        }
    }

    private void Update()
    {
        // THE PLAYER CONTROLLER IS OPTIONAL. THE PLAYER TRANSFORM IS NOT.
        //
        // This used to return early unless BOTH were resolved, and that is the
        // bug behind pickups hanging in the air beside the character doing
        // nothing at all. PlayerController is fetched with GetComponent on the
        // object tagged Player — so if the component ever sits on a child, or a
        // different rig is spawned, it comes back null and this method does
        // NOTHING for the rest of the drop's life. Not the magnet, not the
        // spin, not even the retry, because the retry was behind the same
        // return. A drop in that state is frozen forever, which is exactly what
        // it looks like on screen.
        //
        // The controller is only ever needed for one number — the pickup radius
        // — and a missing one is not a reason to abandon the resource. It is
        // searched for properly now, and a sensible radius stands in until it
        // turns up.
        if (player == null)
        {
            if (Time.frameCount % 30 == 0) AcquirePlayer();
            return;
        }
        if (playerController == null && Time.frameCount % 30 == 0) AcquirePlayer();

        if (rb.isKinematic && !isMagnetizing)
        {
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        }

        float dist = Vector3.Distance(transform.position, player.position);
        float radius = playerController != null ? playerController.pickupRadius : 3.5f;
        float age = Time.time - bornAt;

        // The failsafe: a drop that has sat around long enough is collected from
        // anywhere within arm's reach, whatever the pickup radius says. A
        // resource the player is standing inside and cannot pick up is worse than
        // one that is slightly too easy to pick up.
        bool stale = age > giveUpAfter;

        if (!isMagnetizing && (dist <= radius || (stale && dist <= 4f)))
        {
            isMagnetizing = true;
            magnetStartedAt = Time.time;
            rb.isKinematic = true;
            foreach (var c in myColliders) if (c != null) c.enabled = false;
        }

        if (isMagnetizing)
        {
            transform.position = Vector3.MoveTowards(transform.position, player.position + Vector3.up, magnetSpeed * Time.deltaTime);

            // Distance OR patience. A magnet that has been flying at the player
            // for two seconds and still has not arrived is not going to: it is
            // being pushed, or the player is outrunning it, or something moved
            // the goalposts. Paying out is always better than a resource that
            // orbits the character forever.
            if (Vector3.Distance(transform.position, player.position + Vector3.up) < 0.5f
                || Time.time - magnetStartedAt > 2f)
            {
                Collect();
            }
        }
    }

    private float magnetStartedAt = -1f;

    // Belt and braces: if the drop somehow overlaps the player as a trigger
    // without the magnet having fired, take it anyway.
    private void OnTriggerEnter(Collider other)
    {
        if (isCollected || other == null) return;
        if (!other.CompareTag("Player")) return;
        if (playerController == null) playerController = other.GetComponentInParent<PlayerController>();
        // Collected either way. Touching the player IS the pickup; whether the
        // controller reference resolved is this component's problem, not the
        // player's, and the diamond branch below already handles it being null.
        Collect();
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

        // ==== THE DROP GOES, WHATEVER THE PAYOUT DOES ====
        //
        // This is the third time a pickup has been reported flying to the player
        // and then hanging there, and the shape is always the same: something in
        // the payout throws, the exception escapes Collect, and the Destroy at
        // the bottom never runs. The drop is left with isCollected already true,
        // so every later trigger returns at the guard — frozen on the player,
        // permanently, with no way back.
        //
        // Every previous fix chased whichever call happened to be throwing that
        // month. The real defect is structural: disposal must not be the last
        // statement of a block that can fail. In a finally it cannot be skipped,
        // and the throw still reaches the console with its stack trace intact
        // instead of being swallowed.
        try
        {
            Payout();
        }
        finally
        {
            Destroy(gameObject);
        }
    }

    private void Payout()
    {
        if (ResourceManager.Instance != null)
        {
            // ResourceManager.AddRunResources now fires its own toast
            // through GlobalHUD, so we no longer call ShowPickupPopup
            // here — that produced two stacking toasts per pickup.
            if (resourceType == ResourceType.Wood)
            {
                ResourceManager.Instance.AddRunResources(amount, 0, 0);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Pickup_Wood);
            }
            else if (resourceType == ResourceType.Stone)
            {
                ResourceManager.Instance.AddRunResources(0, amount, 0);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Pickup_Stone);
            }
            else if (resourceType == ResourceType.Food)
            {
                ResourceManager.Instance.AddRunResources(0, 0, amount);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Pickup_Food);
            }
            else if (resourceType == ResourceType.Diamond)
            {
                // Through the player when it can be, straight to the wallet
                // otherwise. The controller carries the pickup flourish, but a
                // missing reference must never mean the diamond quietly
                // evaporates — the drop is destroyed either way, so a branch
                // that pays nothing is a branch that steals.
                if (playerController != null) playerController.GainDiamond(amount);
                else ResourceManager.Instance.AddDiamonds(amount);
            }
        }
    }
}
