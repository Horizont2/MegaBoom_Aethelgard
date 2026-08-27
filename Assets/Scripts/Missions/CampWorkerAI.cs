using UnityEngine;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
public class CampWorkerAI : MonoBehaviour
{
    [Header("Link to Building")]
    public CampBuilding myBuilding;

    [Header("Locations")]
    public Transform dropPoint;
    public Transform spawnPoint;
    public float searchRadius = 30f;

    [Header("Distances")]
    public float workDistance = 0.8f;
    public float dropDistance = 1.0f;

    [Header("Timings")]
    public float timeBetweenHits = 1.2f;
    public float dropDuration = 2f;

    [Header("Night Schedule (optional)")]
    // Assign a point near the campfire. When it's deep night the worker
    // walks here and idles instead of wandering — sells the "everyone
    // rests around the fire" vibe. Leave null → normal wander at night.
    public Transform nightGatherPoint;
    // Animator bool that flips true when the worker has arrived at
    // nightGatherPoint during deep night. Wire a "Sitting" state in your
    // Animator that this bool transitions into (default name "IsSitting").
    // Fed via SetBoolSafe — safe to leave the animator without this
    // parameter, the setter just no-ops.
    public string sittingAnimBool = "IsSitting";
    // Distance from nightGatherPoint below which the worker counts as
    // "arrived" and the bool flips on.
    public float sittingArriveRadius = 1.6f;

    [Header("Visuals & Animation")]
    public Animator anim;
    public GameObject carryItemVisual;

    private NavMeshAgent agent;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (carryItemVisual != null) carryItemVisual.SetActive(false);

        transform.position = new Vector3(0, -1000f, 0);

        StartCoroutine(InitAndStartRoutine());
    }

    private void Update()
    {
        // Full gait sync — foot-planted animator tempo, MoveX/MoveZ blend
        // params, IsGrounded, root-motion off — replaces the old
        // Speed-only write that was letting the worker slide.
        NPCGait.Sync(agent, anim, agent != null ? agent.speed : NPCGait.DEFAULT_SPEED);

        // Sitting bool at the fire — CampWorkerAI USED TO DECLARE the
        // field but never write it, so the sit anim never played. Fixed.
        if (anim != null && !string.IsNullOrEmpty(sittingAnimBool))
            anim.SetBoolSafe(sittingAnimBool, NPCGait.ShouldSit(agent, sittingArriveRadius));
    }

    private void LateUpdate() => NPCGait.GroundSnap(transform);

    private IEnumerator InitAndStartRoutine()
    {
        if (myBuilding != null)
        {
            while (myBuilding.currentLevel == 0) yield return null;
        }

        yield return new WaitForSeconds(0.5f);

        Vector3 startPos = spawnPoint != null ? spawnPoint.position : (dropPoint != null ? dropPoint.position : Vector3.zero);

        if (Terrain.activeTerrain != null)
        {
            float terrainHeight = Terrain.activeTerrain.SampleHeight(startPos) + Terrain.activeTerrain.transform.position.y;
            startPos.y = terrainHeight;
        }

        transform.position = startPos;

        if (agent != null)
        {
            agent.enabled = true;
            NPCGait.Configure(agent);
            yield return null;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(startPos, out hit, 4f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }

        if (agent != null && dropPoint != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.stoppingDistance = dropDistance;
            agent.SetDestination(dropPoint.position);

            while (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + 0.1f)
            {
                yield return null;
            }

            agent.isStopped = true;
            transform.rotation = dropPoint.rotation;
            yield return new WaitForSeconds(1f);
        }

        StartCoroutine(WorkerRoutine());
    }

    private bool CheckIfWorkAvailable()
    {
        if (myBuilding != null && myBuilding.IsVisualsFull()) return false;
        return FindNearestTree() != null;
    }

    private IEnumerator WanderAround()
    {
        if (agent != null && agent.isOnNavMesh) agent.isStopped = false;

        while (true)
        {
            if (CheckIfWorkAvailable()) break;

            if (agent != null && agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f)
            {
                Vector3 dest;
                // At deep night, if a fire-gathering point is wired, walk
                // to it with a tiny jitter so multiple workers don't stack
                // on the exact same spot.
                if (nightGatherPoint != null && CampSchedule.IsDeepNight())
                {
                    Vector2 jitter = Random.insideUnitCircle * 1.2f;
                    dest = nightGatherPoint.position + new Vector3(jitter.x, 0f, jitter.y);
                }
                else
                {
                    Vector3 randomDirection = Random.insideUnitSphere * 8f;
                    randomDirection += transform.position;
                    dest = randomDirection;
                }
                NavMeshHit hit;
                if (NavMesh.SamplePosition(dest, out hit, 8f, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);
                }
            }
            // At night workers linger longer at the fire; during day they
            // move around more actively.
            float wait = CampSchedule.IsDeepNight() && nightGatherPoint != null
                ? Random.Range(8f, 14f)
                : Random.Range(4f, 8f);
            yield return new WaitForSeconds(wait);
        }
    }

    private IEnumerator NightGatherRoutine()
    {
        if (carryItemVisual != null) carryItemVisual.SetActive(false);
        Vector3 target = nightGatherPoint != null
            ? nightGatherPoint.position
            : (dropPoint != null ? dropPoint.position : transform.position);
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.stoppingDistance = 1.0f;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(target, out hit, 4f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);
            float timeout = 0f;
            while (timeout < 15f)
            {
                timeout += Time.deltaTime;
                if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f) break;
                yield return null;
            }
            if (agent.isOnNavMesh) agent.isStopped = true;
        }
        // Face the fire — smoothly, ~1 second turn-in-place instead of
        // an instant snap that looked robotic.
        float faceTimer = 0f;
        while (nightGatherPoint != null && faceTimer < 1.2f)
        {
            NPCGait.FaceTarget(transform, nightGatherPoint.position, 240f);
            faceTimer += Time.deltaTime;
            yield return null;
        }
        while (CampSchedule.IsDeepNight())
            yield return new WaitForSeconds(1f);
    }

    private IEnumerator WorkerRoutine()
    {
        yield return new WaitForSeconds(Random.Range(0f, 1f));

        while (true)
        {
            if (agent == null || !agent.isOnNavMesh)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            // Night beats work — drop the axe and walk to the fire even if
            // trees are ready. Previously this check only fired via
            // WanderAround when there was NOTHING to chop, so a well-
            // stocked forest kept the worker chopping 24/7.
            if (CampSchedule.IsDeepNight())
            {
                yield return StartCoroutine(NightGatherRoutine());
                continue;
            }

            if (!CheckIfWorkAvailable())
            {
                yield return StartCoroutine(WanderAround());
                continue;
            }

            CampTree targetTree = FindNearestTree();
            if (targetTree == null) { yield return new WaitForSeconds(2f); continue; }

            if (carryItemVisual != null) carryItemVisual.SetActive(false);

            agent.isStopped = false;
            // Reach the approach point itself (small stopping distance) so the
            // worker actually stands NEXT to the trunk instead of halting
            // ~1.8m short (old stoppingDistance = workDistance stacked on the
            // 1m offset made it chop from too far away).
            agent.stoppingDistance = 0.15f;
            // Pathfind to a point just outside the tree trunk.
            Vector3 approachOffset = transform.position - targetTree.transform.position;
            approachOffset.y = 0f;
            if (approachOffset.sqrMagnitude < 0.01f) approachOffset = Vector3.forward;
            approachOffset = approachOffset.normalized * 1.1f;
            Vector3 approachPos = targetTree.transform.position + approachOffset;
            agent.SetDestination(approachPos);

            float timeout = 0f;
            bool arrived = false;
            while (timeout < 15f)
            {
                timeout += Time.deltaTime;
                if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f) { arrived = true; break; }
                yield return null;
            }

            if (agent.isOnNavMesh) agent.isStopped = true;

            // The NavMesh path is truncated at the trunk's carved edge, so the
            // agent often "arrives" 2-4m short and then chopped from way out
            // there. Close that last gap manually: step the worker straight in
            // to a proper chop distance from the trunk before swinging.
            const float chopStand = 1.4f;   // planar metres from trunk centre
            if (targetTree != null && !targetTree.isChopped)
            {
                Vector3 tp = targetTree.transform.position;
                Vector3 flatSelf = new Vector3(transform.position.x, 0f, transform.position.z);
                Vector3 flatTree = new Vector3(tp.x, 0f, tp.z);
                float planar = Vector3.Distance(flatSelf, flatTree);
                if (planar > chopStand + 0.35f)
                {
                    Vector3 back = (flatSelf - flatTree);
                    if (back.sqrMagnitude < 0.01f) back = -transform.forward;
                    back.y = 0f; back = back.normalized;
                    Vector3 standPos = flatTree + back * chopStand;
                    standPos.y = transform.position.y;
                    // Temporarily hand control from the agent to a short manual
                    // walk-in so it isn't yanked back to the navmesh edge.
                    if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
                    float nt = 0f;
                    Vector3 from = transform.position;
                    while (nt < 1f)
                    {
                        nt += Time.deltaTime * 2.2f;
                        transform.position = Vector3.Lerp(from, standPos, Mathf.Clamp01(nt));
                        yield return null;
                    }
                }
            }

            // Face the tree so the swing animation lands the right way.
            if (targetTree != null)
            {
                Vector3 lookAt = targetTree.transform.position;
                lookAt.y = transform.position.y;
                Vector3 faceDir = lookAt - transform.position;
                if (faceDir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(faceDir.normalized, Vector3.up);
            }

            // Chop when we're actually next to the trunk. After the manual
            // walk-in above the worker stands ~1.4m out, so a tight 2.2m gate
            // both confirms he reached it and stops the old "chop from 6m away".
            bool canChop = targetTree != null && !targetTree.isChopped &&
                Vector3.Distance(new Vector3(transform.position.x, 0f, transform.position.z),
                                 new Vector3(targetTree.transform.position.x, 0f, targetTree.transform.position.z)) <= 2.2f;

            bool didChop = false;
            if (canChop)
            {
                while (targetTree != null && !targetTree.isChopped)
                {
                    // No worker animator actually has a "Work" chop state, so drive
                    // the swing PROCEDURALLY — the model leans in and back on each
                    // hit — guaranteeing a visible chop regardless of the rig.
                    // (Still fires the trigger in case a controller does have it.)
                    if (anim != null) anim.SetTriggerSafe("Work");
                    yield return StartCoroutine(ChopSwingRoutine());
                    didChop = true;

                    if (targetTree != null && !targetTree.isChopped) targetTree.TakeHit();
                }
                yield return new WaitForSeconds(0.4f);
            }
            else
            {
                // Couldn't actually reach the tree — DON'T fake-deliver resources
                // (the old code deposited anyway, so it looked like it walked up,
                // did nothing, and carried logs back). Try again next loop.
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            // The manual walk-in toward the trunk can leave the worker a hair
            // off the carved navmesh; snap the agent back on before it paths to
            // the drop point (otherwise agent.isOnNavMesh is false and it never
            // walks back).
            if (agent != null && !agent.isOnNavMesh)
            {
                NavMeshHit snap;
                if (NavMesh.SamplePosition(transform.position, out snap, 4f, NavMesh.AllAreas))
                    agent.Warp(snap.position);
            }

            if (myBuilding != null && myBuilding.IsVisualsFull())
            {
                if (carryItemVisual != null) carryItemVisual.SetActive(false);
                continue;
            }

            if (carryItemVisual != null) carryItemVisual.SetActive(true);

            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.stoppingDistance = dropDistance;
                if (dropPoint != null) agent.SetDestination(dropPoint.position);
            }

            timeout = 0f;
            while (dropPoint != null && timeout < 15f)
            {
                timeout += Time.deltaTime;
                if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f) break;
                yield return null;
            }

            if (agent.isOnNavMesh) agent.isStopped = true;
            if (dropPoint != null) transform.rotation = dropPoint.rotation;

            yield return new WaitForSeconds(dropDuration);

            if (carryItemVisual != null) carryItemVisual.SetActive(false);
            if (myBuilding != null) myBuilding.ShowNextVisualResource();
        }
    }

    // Procedural chop: lean the model in toward the tree and back, with the
    // impact SFX at the bottom of the swing. Works with ANY rig since no worker
    // animator actually has a chop state.
    private IEnumerator ChopSwingRoutine()
    {
        Transform model = (anim != null) ? anim.transform : transform;
        Quaternion rest = model.localRotation;

        // Small wind-up back.
        float t = 0f, wind = 0.10f;
        while (t < wind) { t += Time.deltaTime; model.localRotation = rest * Quaternion.Euler(-10f * (t / wind), 0f, 0f); yield return null; }

        // Fast chop down toward the trunk.
        Quaternion top = model.localRotation;
        Quaternion low = rest * Quaternion.Euler(40f, 0f, 0f);
        t = 0f; float down = 0.09f;
        while (t < down) { t += Time.deltaTime; model.localRotation = Quaternion.Slerp(top, low, t / down); yield return null; }

        // Impact.
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.NPC_Work, transform.position);

        // Return to rest.
        t = 0f; float ret = 0.18f;
        while (t < ret) { t += Time.deltaTime; model.localRotation = Quaternion.Slerp(low, rest, t / ret); yield return null; }
        model.localRotation = rest;

        float pad = timeBetweenHits - (wind + down + ret);
        if (pad > 0f) yield return new WaitForSeconds(pad);
    }

    private CampTree FindNearestTree()
    {
        // Բ�� ����̲��ֲ�: ������������� OverlapSphere ������ ������� ������ �� ��� �����
        Collider[] hits = Physics.OverlapSphere(transform.position, searchRadius);
        CampTree nearest = null;
        float minDistance = Mathf.Infinity;

        foreach (Collider hit in hits)
        {
            CampTree tree = hit.GetComponent<CampTree>();
            if (tree != null && !tree.isChopped)
            {
                float dist = Vector3.Distance(transform.position, tree.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    nearest = tree;
                }
            }
        }
        return nearest;
    }
}