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
        if (anim != null && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            anim.SetFloatSafe("Speed", agent.velocity.magnitude);
        }
    }

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

            if (!CheckIfWorkAvailable())
            {
                yield return StartCoroutine(WanderAround());
                continue;
            }

            CampTree targetTree = FindNearestTree();
            if (targetTree == null) { yield return new WaitForSeconds(2f); continue; }

            if (carryItemVisual != null) carryItemVisual.SetActive(false);

            agent.isStopped = false;
            agent.stoppingDistance = workDistance;
            agent.SetDestination(targetTree.transform.position);

            float timeout = 0f;
            while (timeout < 15f)
            {
                timeout += Time.deltaTime;
                if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f) break;
                yield return null;
            }

            if (agent.isOnNavMesh) agent.isStopped = true;

            if (targetTree != null && !targetTree.isChopped)
            {
                while (targetTree != null && !targetTree.isChopped)
                {
                    Vector3 lookPos = targetTree.transform.position;
                    lookPos.y = transform.position.y;
                    transform.LookAt(lookPos);

                    if (anim != null) anim.SetTrigger("Work");
                    // 3D so the chopping sound sits at the worker/tree in
                    // the world instead of glued to the player's ears.
                    if (AudioManager.Instance != null)
                        AudioManager.Instance.PlaySFX3D(AudioID.NPC_Work, transform.position);

                    yield return new WaitForSeconds(timeBetweenHits);

                    if (targetTree != null && !targetTree.isChopped) targetTree.TakeHit();
                }
                yield return new WaitForSeconds(1f);
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