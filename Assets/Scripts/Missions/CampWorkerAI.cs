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
            anim.SetFloat("Speed", agent.velocity.magnitude);
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
                Vector3 randomDirection = Random.insideUnitSphere * 8f;
                randomDirection += transform.position;
                NavMeshHit hit;
                if (NavMesh.SamplePosition(randomDirection, out hit, 8f, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);
                }
            }
            yield return new WaitForSeconds(Random.Range(4f, 8f));
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
                    if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.NPC_Work);

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
        // Ô²ÊÑ ÎÏÒÈÌ²ÇÀÖ²¯: Âèêîðèñòîâóºìî OverlapSphere çàì³ñòü âàæêîãî ïîøóêó ïî âñ³é ñöåí³
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