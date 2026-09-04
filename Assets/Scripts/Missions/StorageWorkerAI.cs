using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(NavMeshAgent))]
public class StorageWorkerAI : MonoBehaviour
{
    public NavMeshAgent agent;
    public Animator anim;
    public GameObject carryVisual;
    public Transform storageDropPoint;

    [Header("Night Schedule (optional)")]
    // At deep night the storage worker drops the shift and walks to this
    // point (usually near the campfire). Leave null → keep hauling
    // overnight.
    public Transform nightGatherPoint;
    public string sittingAnimBool = "IsSitting";
    public float sittingArriveRadius = 1.6f;

    private List<CampBuilding> productionBuildings = new List<CampBuilding>();

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (carryVisual != null) carryVisual.SetActive(false);

        // Role guard: the storage worker prefab must never also run the
        // lumberjack brain. If a CampWorkerAI ended up on this object (or
        // a child) — easy to do when duplicating NPC prefabs — both AIs
        // fight over the same NavMeshAgent and the storage NPC walks off
        // to chop trees. Storage wins; the stowaway gets disabled.
        foreach (var lumber in GetComponentsInChildren<CampWorkerAI>(true))
        {
            Debug.LogWarning($"[StorageWorkerAI] '{name}' also carries a CampWorkerAI — destroying it so the storage NPC doesn't wander off to chop trees. Remove the extra component from the prefab.");
            // Destroy, not disable — CampWorkerAI's coroutines keep running
            // on a merely-disabled component.
            Destroy(lumber);
        }

        // Root motion off (see BarracksUnitAI for the roller-skate fix).
        if (anim != null && anim.applyRootMotion) anim.applyRootMotion = false;
        if (anim != null) anim.SetBoolSafe("IsGrounded", true);

        StartCoroutine(InitAndStartRoutine());
    }

    private void Update()
    {
        NPCGait.Sync(agent, anim, agent != null ? agent.speed : NPCGait.DEFAULT_SPEED);
        if (anim != null && !string.IsNullOrEmpty(sittingAnimBool))
            anim.SetBoolSafe(sittingAnimBool, NPCGait.ShouldSit(agent, sittingArriveRadius));
    }

    // Agent-aware: never fight the NavMeshAgent for the transform (that was the
    // "turns on the spot instead of walking" bug).
    private void LateUpdate() => NPCGait.GroundSnap(transform, agent);

    private IEnumerator InitAndStartRoutine()
    {
        yield return new WaitForSeconds(0.5f);
        if (transform.position.y < -2f)
        {
            if (agent != null) agent.enabled = false;
            yield return new WaitForSeconds(2.5f);
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 10f, NavMesh.AllAreas))
                transform.position = hit.position;
        }
        if (agent != null)
        {
            // Force the default (baked Humanoid) agent type — but ONLY while the
            // agent is DISABLED. Changing agentTypeID on an ENABLED agent leaves it
            // in a broken state where it reports velocity but never moves the
            // transform ("walks in place"). Toggle disabled → set type → enable.
            int defType = NavMesh.GetSettingsByIndex(0).agentTypeID;
            if (agent.agentTypeID != defType)
            {
                agent.enabled = false;
                agent.agentTypeID = defType;
            }
            agent.enabled = true;
            agent.updatePosition = true;   // guard: something may have parked it
            agent.updateRotation = true;
            NPCGait.Configure(agent, stoppingDistance: 0.5f);
            // Snap onto the NEAREST navmesh point, not the exact spawn. If the
            // NPC spawned a hair off the mesh (on a foundation, a slope, or just
            // above the ground), Warp(transform.position) left isOnNavMesh false
            // — and every movement path is gated on isOnNavMesh, so it just
            // stood there forever doing nothing.
            NavMeshHit navHit;
            if (NavMesh.SamplePosition(transform.position, out navHit, 8f, NavMesh.AllAreas))
                agent.Warp(navHit.position);
            else
                agent.Warp(transform.position);
        }

        FindBuildings();
        StartCoroutine(LogisticsRoutine());
    }

    // Rescanned every loop, not just once at Start: buildings raised or upgraded
    // after the worker spawned were never added to the list, so he had nothing to
    // haul for the rest of the session and just idled by the storage.
    void FindBuildings()
    {
        productionBuildings.Clear();
        CampBuilding[] all = FindObjectsByType<CampBuilding>(FindObjectsSortMode.None);
        foreach (var b in all)
        {
            if (b != null && !b.isStorageVault) productionBuildings.Add(b);
        }
    }

    private IEnumerator WanderAroundStorage()
    {
        // Recover if the agent drifted off the navmesh — every branch below is
        // gated on isOnNavMesh, so without this the worker would stand frozen.
        if (agent != null && !agent.isOnNavMesh)
        {
            NavMeshHit rh;
            if (NavMesh.SamplePosition(transform.position, out rh, 8f, NavMesh.AllAreas))
                agent.Warp(rh.position);
        }
        if (agent != null && agent.isOnNavMesh) agent.isStopped = false;

        if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f)
        {
            // Pick a point a REAL distance away. The old insideUnitSphere pick
            // often landed within the stopping distance, so the worker never
            // actually walked — he just kept turning in place by the storage.
            Vector3 anchor = storageDropPoint != null ? storageDropPoint.position : transform.position;
            Vector2 dir2 = Random.insideUnitCircle.normalized;
            if (dir2 == Vector2.zero) dir2 = Vector2.right;
            Vector3 target = anchor + new Vector3(dir2.x, 0f, dir2.y) * Random.Range(3.5f, 7f);

            NavMeshHit hit;
            if (NavMesh.SamplePosition(target, out hit, 6f, NavMesh.AllAreas) &&
                Vector3.Distance(hit.position, transform.position) > agent.stoppingDistance + 1.5f)
            {
                agent.SetDestination(hit.position);
            }
        }
        yield return new WaitForSeconds(Random.Range(4f, 8f));
    }

    // Walks to the campfire and sits there until deep-night ends. Drops
    // the carry visual so the worker doesn't cradle a log all night.
    private IEnumerator NightGatherRoutine()
    {
        if (carryVisual != null) carryVisual.SetActive(false);
        Vector3 destPos = nightGatherPoint != null
            ? nightGatherPoint.position
            : (storageDropPoint != null ? storageDropPoint.position : transform.position);
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(destPos, out hit, 4f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);
            yield return StartCoroutine(WaitArrival());
            agent.isStopped = true;
        }
        // Face the fire smoothly — ~1s turn-in-place, not an instant snap.
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

    private IEnumerator LogisticsRoutine()
    {
        yield return new WaitForSeconds(3f);

        while (true)
        {
            // Night beats hauling. Runs whether or not nightGatherPoint is
            // wired (NightGatherRoutine has a fallback), so a half-config
            // NPC still respects the day/night cycle.
            if (CampSchedule.IsDeepNight())
            {
                yield return StartCoroutine(NightGatherRoutine());
                continue;
            }

            FindBuildings();   // pick up anything built or upgraded since the last pass
            bool collectedAnything = false;

            foreach (var building in productionBuildings)
            {
                if (building != null && building.currentLevel > 0 && building.pendingResourcesCount > 0)
                {
                    collectedAnything = true;

                    // 1. ����� �� Pickup Point (� �������� �������)
                    agent.isStopped = false;
                    Vector3 rawTargetPos = building.pickupPoint != null ? building.pickupPoint.position : building.transform.position;
                    Vector3 targetPos = rawTargetPos;

                    NavMeshHit hit;
                    if (NavMesh.SamplePosition(rawTargetPos, out hit, 5f, NavMesh.AllAreas))
                    {
                        targetPos = hit.position;
                    }

                    agent.SetDestination(targetPos);
                    yield return StartCoroutine(WaitArrival());

                    agent.isStopped = true;
                    // Face the crate on the XZ plane. transform.LookAt on a point
                    // he is already standing on gives a degenerate direction and
                    // reads as a random spin.
                    Vector3 faceDir = targetPos - transform.position; faceDir.y = 0f;
                    if (faceDir.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.LookRotation(faceDir.normalized, Vector3.up);

                    // 2. ϳ�������
                    if (anim != null) anim.SetTrigger("Pickup");
                    yield return new WaitForSeconds(1.5f);

                    int amount = building.CollectResourcesByStorageNPC();
                    if (carryVisual != null) carryVisual.SetActive(true);

                    yield return new WaitForSeconds(0.5f);

                    // 3. ������ �� ����� (� �������� ������� ������� ��� ����� ������!)
                    agent.isStopped = false;
                    Vector3 rawDropPos = storageDropPoint != null ? storageDropPoint.position : transform.position;
                    Vector3 dropTargetPos = rawDropPos;

                    NavMeshHit dropHit;
                    if (NavMesh.SamplePosition(rawDropPos, out dropHit, 5f, NavMesh.AllAreas))
                    {
                        dropTargetPos = dropHit.position;
                    }

                    agent.SetDestination(dropTargetPos);
                    yield return StartCoroutine(WaitArrival());

                    // 4. �������
                    agent.isStopped = true;
                    // ������ ����������� � ������� ������
                    if (storageDropPoint != null) transform.rotation = storageDropPoint.rotation;

                    if (anim != null) anim.SetTrigger("Pickup");
                    yield return new WaitForSeconds(1.0f);

                    if (carryVisual != null) carryVisual.SetActive(false);

                    if (building.productionType == ResourceType.Wood)
                        ResourceManager.Instance.AddStashResources(amount, 0, 0);
                    else if (building.productionType == ResourceType.Food)
                        ResourceManager.Instance.AddStashResources(0, 0, amount);
                    else if (building.productionType == ResourceType.Stone)
                        ResourceManager.Instance.AddStashResources(0, amount, 0);

                    yield return new WaitForSeconds(1.5f);
                }
            }

            if (!collectedAnything)
            {
                yield return StartCoroutine(WanderAroundStorage());
            }
            else
            {
                yield return new WaitForSeconds(2f);
            }
        }
    }

    private IEnumerator WaitArrival()
    {
        // ���� ����� Unity 1 ����, ��� �� 100% ����� ������ ���������� �����
        yield return null;

        float timeout = 0f;
        while (timeout < 20f)
        {
            timeout += Time.deltaTime;
            if (agent != null && agent.isOnNavMesh && !agent.pathPending)
            {
                if (agent.pathStatus == NavMeshPathStatus.PathInvalid) break;
                if (agent.remainingDistance <= agent.stoppingDistance + 0.1f) break;
            }
            yield return null;
        }
    }
}