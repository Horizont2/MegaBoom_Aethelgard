using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// The camp's porter: walks to a building that has produced something, picks it
// up, carries it to the storage vault, and does it again.
//
// ==== WHY THIS IS A REWRITE AND NOT ANOTHER FIX ====
//
// The version before this one was four hundred and fifty lines, and most of
// them were repairs layered on repairs: a guard against a rival AI component, a
// warp for spawning off the navmesh, a second warp in case the first missed, a
// re-pick because the wander target landed inside the stopping distance, a
// fallback to transform.position on every leg. Each was a real fix for a real
// report. Together they made a machine where nothing said what it was doing, so
// the next failure looked exactly like the last three and got another patch.
//
// What was actually wrong, in the end, was not in the code at all. TWO building
// prefabs carried isStorageVault - the vault, and the Scout's Lodge, which had
// been duplicated from it and never renamed. So "find the one building with the
// flag" returned whichever FindObjectsByType happened to list first: sometimes
// the vault, sometimes a hut on the other side of camp. Non-deterministic, and
// therefore unfixable by staring at the AI.
//
// ==== WHAT THIS ONE DOES DIFFERENTLY ====
//
// One state variable, one loop, and a line in the log at every transition. When
// it stops working the log says which state it stopped in and why, which is the
// thing none of the previous versions could tell anyone.
//
// Nothing silently succeeds. Every leg checks that its destination exists, is
// reachable, and is somewhere OTHER than where the worker already stands -
// because the old bug where he "worked" without moving was a SetDestination to
// his own feet followed by an arrival check that passed instantly.
//
// Every wait has a timeout. A building that cannot be pathed to costs one leg
// and a log line, not the rest of the session.
[RequireComponent(typeof(NavMeshAgent))]
public class StorageWorkerAI : MonoBehaviour
{
    [Header("Wiring")]
    public NavMeshAgent agent;
    public Animator anim;
    [Tooltip("Shown while carrying, hidden otherwise. Optional.")]
    public GameObject carryVisual;
    [Tooltip("Where to drop off. Left empty, the building flagged Is Storage Vault is used.")]
    public Transform storageDropPoint;

    [Header("Night")]
    [Tooltip("At deep night he stops hauling and walks here. Empty = works through the night.")]
    public Transform nightGatherPoint;
    public string sittingAnimBool = "IsSitting";
    public float sittingArriveRadius = 1.6f;

    [Header("Timing")]
    [Tooltip("Seconds spent loading at a building.")]
    public float pickupSeconds = 1.4f;
    [Tooltip("Seconds spent unloading at the vault.")]
    public float depositSeconds = 1.2f;
    [Tooltip("How long to wait when nothing has produced anything yet.")]
    public float idlePollSeconds = 3f;
    [Tooltip("Give up on a leg after this long and try something else. A leg that cannot finish must not stop the shift.")]
    public float walkTimeout = 45f;

    [Header("Animation")]
    public string pickupTrigger = "Pickup";

    private enum State { Starting, Idle, ToSource, Loading, ToVault, Unloading, Resting }

    private State state = State.Starting;
    private CampBuilding vault;
    private Transform dropPoint;
    private int carrying;
    private ResourceType carryingType;

    // Walk's answer. A coroutine cannot return one and `yield return` is a
    // statement, not an expression, so the result comes back through here.
    private bool arrivedOk;

    // ===================== life =====================

    private void Start()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        Carry(false);

        // Duplicating an NPC prefab is how a second brain ends up on one body,
        // and two AIs sharing a NavMeshAgent is a worker who walks off to chop
        // trees. Destroyed rather than disabled: a disabled component's
        // coroutines keep running.
        foreach (var rival in GetComponentsInChildren<CampWorkerAI>(true))
        {
            Debug.LogWarning($"[Storage] '{name}' also carries a CampWorkerAI. Removing it - two AIs cannot share " +
                             "one NavMeshAgent. Take the extra component off the prefab.");
            Destroy(rival);
        }

        if (anim != null)
        {
            anim.applyRootMotion = false;          // or he skates
            anim.SetBoolSafe("IsGrounded", true);
        }

        StartCoroutine(Shift());
    }

    private void Update()
    {
        NPCGait.Sync(agent, anim, agent != null ? agent.speed : NPCGait.DEFAULT_SPEED);
        if (anim != null && !string.IsNullOrEmpty(sittingAnimBool))
            anim.SetBoolSafe(sittingAnimBool, NPCGait.ShouldSit(agent, sittingArriveRadius, nightGatherPoint));
    }

    // Never fight the agent for the transform - that was the "turns on the spot
    // instead of walking" bug.
    private void LateUpdate() => NPCGait.GroundSnap(transform, agent);

    // ===================== the shift =====================

    private IEnumerator Shift()
    {
        yield return StartCoroutine(PlaceOnNavMesh());

        if (!ResolveVault())
        {
            Enter(State.Idle);
            Debug.LogError("[Storage] Nowhere to deliver to: no building has Is Storage Vault ticked and no " +
                           "Storage Drop Point is assigned. The worker will stand still until one of those exists.");
            yield break;
        }

        while (true)
        {
            if (IsNight() && nightGatherPoint != null)
            {
                Enter(State.Resting);
                yield return Walk(nightGatherPoint.position, "the night point");
                while (IsNight()) yield return new WaitForSeconds(1f);
                continue;
            }

            CampBuilding source = PickSource();
            if (source == null)
            {
                if (state != State.Idle) Enter(State.Idle);
                yield return new WaitForSeconds(idlePollSeconds);
                continue;
            }

            // ---- out ----
            Enter(State.ToSource);
            yield return Walk(source.transform.position, source.buildingName);
            if (!arrivedOk)
            {
                // Walk logs its own reason. Nothing is being carried, so back to
                // looking - one unreachable building must not end the shift.
                continue;
            }

            // ---- load ----
            Enter(State.Loading);
            Face(source.transform.position);
            if (anim != null && !string.IsNullOrEmpty(pickupTrigger)) anim.SetTriggerSafe(pickupTrigger);
            yield return new WaitForSeconds(pickupSeconds);

            carryingType = source.productionType;
            carrying = source.CollectResourcesByStorageNPC();
            if (carrying <= 0)
            {
                // Somebody else took it, or it was collected by hand while he
                // walked. Not an error, and not worth a trip to the vault.
                Log($"arrived at {source.buildingName} to find it already emptied");
                continue;
            }
            Carry(true);
            Log($"picked up {carrying} from {source.buildingName}");

            // ---- back ----
            //
            // ==== HE MUST NOT BANK FROM WHEREVER HE HAPPENS TO BE ====
            //
            // The outbound leg checks arrivedOk and gives up on a building it
            // cannot reach. This one did not. So a failed walk to the vault -
            // no path, off the navmesh, or a forty-five second timeout - fell
            // straight through to Deliver, and the load went into the stash
            // from wherever he was standing. That is the worker who carries
            // resources without walking anywhere.
            //
            // He keeps the load and keeps trying instead. A vault he genuinely
            // cannot reach now shows up as a porter standing in the camp
            // holding a crate, which is a thing somebody can see and fix,
            // rather than as an economy that works with nobody moving.
            Enter(State.ToVault);
            int vaultTries = 0;
            while (true)
            {
                yield return Walk(dropPoint.position, "the vault");
                if (arrivedOk) break;

                vaultTries++;
                if (vaultTries == 1)
                {
                    Debug.LogError($"[Storage] '{name}' is carrying {carrying} and cannot reach the vault. " +
                                   "Walk logged the reason above. He will keep trying and will not deliver " +
                                   "until he gets there.", this);
                }
                yield return new WaitForSeconds(idlePollSeconds);
            }

            // ---- unload ----
            Enter(State.Unloading);
            Face(dropPoint.position);
            if (anim != null && !string.IsNullOrEmpty(pickupTrigger)) anim.SetTriggerSafe(pickupTrigger);
            yield return new WaitForSeconds(depositSeconds);

            Deliver();
        }
    }

    // ===================== the legs =====================

    // Walks to a point and leaves the answer in arrivedOk.
    private IEnumerator Walk(Vector3 target, string what)
    {
        arrivedOk = false;

        if (agent == null || !agent.isOnNavMesh)
        {
            yield return StartCoroutine(PlaceOnNavMesh());
            if (agent == null || !agent.isOnNavMesh) { Stuck($"cannot walk to {what}: he is not on the navmesh"); yield break; }
        }

        // ==== A DESTINATION UNDER HIS OWN FEET IS NOT A JOURNEY ====
        //
        // Every leg of the old version fell back to transform.position when its
        // target was missing. SetDestination to where you already stand makes
        // the arrival test pass on the first frame, so the animation played, the
        // loop went round, and the worker "worked" without ever moving. That is
        // the bug everyone was looking at.
        if (!NavMesh.SamplePosition(target, out NavMeshHit onMesh, 6f, agent.areaMask))
        {
            Stuck($"cannot walk to {what}: there is no navmesh within six metres of it");
            yield break;
        }

        if ((onMesh.position - transform.position).sqrMagnitude < 0.75f * 0.75f)
        {
            Log($"already standing at {what}");
            arrivedOk = true; yield break;
        }

        // Asked before committing, so an unreachable building costs one frame
        // rather than the whole timeout.
        var path = new NavMeshPath();
        if (!agent.CalculatePath(onMesh.position, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            Stuck($"cannot walk to {what}: no complete path leads there");
            yield break;
        }

        agent.isStopped = false;
        agent.SetPath(path);

        float spent = 0f;
        while (spent < walkTimeout)
        {
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.15f) break;
            spent += Time.deltaTime;
            yield return null;
        }

        arrivedOk = spent < walkTimeout;
        if (!arrivedOk) Stuck($"gave up walking to {what} after {walkTimeout:0}s");

        agent.isStopped = true;
    }

    private void Deliver()
    {
        // ==== WHAT HE CARRIED IS WHAT ARRIVES ====
        //
        // Everything used to land in the stash as wood, because that is the
        // first argument. A hunter's cabin produces food and a quarry stone;
        // hauling either one and banking timber is a quiet economy bug that
        // nobody would trace back to the porter.
        if (carrying > 0 && ResourceManager.Instance != null)
        {
            switch (carryingType)
            {
                case ResourceType.Stone: ResourceManager.Instance.AddStashResources(0, carrying, 0); break;
                case ResourceType.Food:  ResourceManager.Instance.AddStashResources(0, 0, carrying); break;
                default:                 ResourceManager.Instance.AddStashResources(carrying, 0, 0); break;
            }
        }

        Log($"delivered {carrying} {carryingType} to the vault");
        carrying = 0;
        Carry(false);
    }

    // ===================== what to haul, and where =====================

    // Only a building that has actually produced something, and never the vault
    // itself. The old filter was "everything that is not the vault", which with
    // two buildings claiming the flag meant he could be sent to collect from the
    // place he was supposed to deliver to.
    private CampBuilding PickSource()
    {
        CampBuilding best = null;
        float bestScore = float.MaxValue;
        Vector3 here = transform.position;

        foreach (var b in FindObjectsByType<CampBuilding>(FindObjectsSortMode.None))
        {
            if (b == null || b == vault) continue;
            if (b.isStorageVault) continue;
            if (!b.producesResource) continue;
            if (b.pendingResourcesCount <= 0) continue;

            // Nearest first, but a big pile is worth a few extra steps.
            float score = Vector3.Distance(here, b.transform.position) - b.pendingResourcesCount * 0.5f;
            if (score < bestScore) { bestScore = score; best = b; }
        }
        return best;
    }

    private bool ResolveVault()
    {
        var flagged = new List<CampBuilding>();
        foreach (var b in FindObjectsByType<CampBuilding>(FindObjectsSortMode.None))
            if (b != null && b.isStorageVault) flagged.Add(b);

        if (flagged.Count > 1)
        {
            // This is the bug that hid behind every other one. Named loudly so
            // it can never be invisible again.
            var names = new List<string>();
            foreach (var b in flagged) names.Add($"{b.buildingName} ({b.buildingID})");
            Debug.LogError("[Storage] " + flagged.Count + " buildings have Is Storage Vault ticked: " +
                           string.Join(", ", names) + ". Only one may. Until that is fixed the worker will haul " +
                           "to whichever one happens to be nearest, which is not necessarily the right one.");
        }

        if (flagged.Count > 0)
        {
            vault = flagged[0];
            float best = Vector3.Distance(transform.position, vault.transform.position);
            for (int i = 1; i < flagged.Count; i++)
            {
                float d = Vector3.Distance(transform.position, flagged[i].transform.position);
                if (d < best) { best = d; vault = flagged[i]; }
            }
        }

        // The explicit drop point wins when it is set: it is a marker somebody
        // placed at the door, while the building's own transform is its pivot,
        // which on a large prefab can be inside a wall.
        dropPoint = storageDropPoint != null ? storageDropPoint
                  : (vault != null ? vault.transform : null);

        if (dropPoint != null)
            Log($"delivering to {(storageDropPoint != null ? "the assigned drop point" : vault.buildingName)}");

        return dropPoint != null;
    }

    // ===================== plumbing =====================

    private IEnumerator PlaceOnNavMesh()
    {
        yield return new WaitForSeconds(0.25f);
        if (agent == null) yield break;

        agent.updatePosition = true;
        agent.updateRotation = true;
        NPCGait.Configure(agent, stoppingDistance: 0.6f);

        // The nearest point ON the mesh, not the exact spawn. Spawned a hair off
        // it - on a foundation, on a slope - isOnNavMesh stays false and every
        // branch below is gated on it, which is a worker who stands still
        // forever with nothing in the log.
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 12f, agent.areaMask))
            agent.Warp(hit.position);
        else
            Debug.LogWarning($"[Storage] '{name}' spawned more than twelve metres from any navmesh of its agent " +
                             "type. Move him onto the camp's walkable ground.");
    }

    private bool IsNight()
    {
        var cycle = Object.FindFirstObjectByType<DayNightCycle>();
        if (cycle == null) return false;
        return cycle.timeOfDay >= 22f || cycle.timeOfDay < 5f;
    }

    private void Face(Vector3 target)
    {
        Vector3 flat = target - transform.position; flat.y = 0f;
        if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(flat);
    }

    private void Carry(bool on)
    {
        if (carryVisual != null) carryVisual.SetActive(on);
    }

    private void Enter(State next)
    {
        if (state == next) return;
        state = next;
        Log($"-> {next}");
    }

    // One channel, one prefix. When he stops, the last line says where.
    private void Log(string what)
    {
        GameLog.Info($"[Storage] {what}");
    }

    // ==== A LEG THAT CANNOT BE WALKED IS NOT ROUTINE ====
    //
    // These reasons used to go out through Log, which is Debug.Log and which is
    // one line among hundreds. "The porter never moves" is a bug somebody will
    // spend an evening on, and the answer was already being printed where
    // nobody would ever pick it out. A warning is yellow, it carries the object
    // so clicking it selects him in the hierarchy, and it cannot be missed.
    private void Stuck(string what)
    {
        Debug.LogWarning($"[Storage] {what}", this);
    }
}
