using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class EnemyEncounterGroup : MonoBehaviour
{
    public enum EncounterStyle { Patrol, Camp }

    [Header("Style")]
    public EncounterStyle style = EncounterStyle.Patrol;

    [Header("Enemy Setup")]
    public GameObject[] enemyPrefabs;
    [Range(1, 8)] public int enemyCount = 3;
    [Tooltip("Радіус, у якому вороги розставляються відносно центру групи. Для кампу — радіус кільця навколо вогнища")]
    public float spawnSpread = 2.5f;

    [Header("Passive Behavior")]
    [Tooltip("Радіус блукання навколо anchor у патрулі. Для кампу не використовується (камп-стражі стоять)")]
    public float roamRadius = 3.5f;
    [Tooltip("Дистанція, на якій група агриться")]
    public float aggroRange = 14f;

    [Header("Patrol (Style=Patrol)")]
    [Tooltip("Якщо передано — група циклічно ходить по точках. Інакше WorldEncounterDirector згенерує їх")]
    public Transform[] patrolPoints;
    [Tooltip("Скільки секунд група стоїть на кожному waypoint")]
    public float waypointPauseDuration = 6f;
    [Tooltip("Швидкість руху центру групи між waypoints")]
    public float patrolMoveSpeed = 1.6f;

    [Header("Camp (Style=Camp)")]
    [Tooltip("Префаб вогнища у центрі табору. Якщо null — без вогнища")]
    public GameObject campfirePrefab;
    [Tooltip("Чи мають вороги повертатися обличчям до вогнища, коли стоять")]
    public bool campGuardsFaceFire = true;

    [Header("Lifecycle")]
    public bool autoStart = true;
    [Tooltip("Винагорода, що випадає на місці групи, коли всіх вбили. Звичайно XP кристал")]
    public GameObject clearedRewardPrefab;
    [Tooltip("Скільки штук винагороди упасти при зачистці")]
    [Range(0, 8)] public int clearedRewardCount = 3;

    private readonly List<EnemyAI> spawnedEnemies = new List<EnemyAI>(8);
    private int currentPatrolIndex = 0;
    private bool spawned = false;
    private bool clearedFired = false;
    private GameObject spawnedCampfire;
    private float clearedCheckTimer = 0f;

    public IReadOnlyList<EnemyAI> SpawnedEnemies => spawnedEnemies;

    private void Start()
    {
        if (autoStart) SpawnGroup();
    }

    public void SpawnGroup()
    {
        if (spawned) return;
        spawned = true;

        Vector3 groupCenter = GroundedCenter(transform.position);
        transform.position = groupCenter;

        if (style == EncounterStyle.Camp && campfirePrefab != null)
        {
            // Preserve the prefab's own rotation so meshes don't end up sideways.
            // Don't parent under the (randomly-rotated) group root either —
            // worldPositionStays would still drag scale from the parent.
            spawnedCampfire = Instantiate(campfirePrefab, groupCenter, campfirePrefab.transform.rotation);
            spawnedCampfire.transform.SetParent(transform, true);
        }

        if (enemyPrefabs == null || enemyPrefabs.Length == 0) return;

        for (int i = 0; i < enemyCount; i++)
        {
            GameObject prefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
            if (prefab == null) continue;

            Vector3 spawnPos;
            Quaternion spawnRot;

            if (style == EncounterStyle.Camp)
            {
                // Even ring around the fire so it reads as a council, not a mob.
                float angle = (i / (float)enemyCount) * Mathf.PI * 2f + Random.Range(-0.15f, 0.15f);
                spawnPos = groupCenter + new Vector3(Mathf.Cos(angle) * spawnSpread, 0f, Mathf.Sin(angle) * spawnSpread);
                spawnPos = GroundedCenter(spawnPos);

                Vector3 inward = groupCenter - spawnPos;
                inward.y = 0f;
                spawnRot = inward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(inward.normalized) : Quaternion.identity;
            }
            else
            {
                Vector2 off = Random.insideUnitCircle * spawnSpread;
                spawnPos = groupCenter + new Vector3(off.x, 0f, off.y);
                spawnPos = GroundedCenter(spawnPos);
                spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            }

            GameObject obj = Instantiate(prefab, spawnPos, spawnRot);
            EnemyAI ai = obj.GetComponent<EnemyAI>();
            if (ai != null)
            {
                ai.startPassive = true;
                ai.parentGroup = this;
                ai.aggroRange = aggroRange;

                if (style == EncounterStyle.Camp)
                {
                    // Each camp guard stays put at its own spot. The fire is the anchor only
                    // for facing direction.
                    ai.anchorPoint = spawnPos;
                    ai.anchorTransform = null;
                    ai.roamRadius = 0.2f;
                    ai.roamWhilePassive = false;
                    ai.faceAnchorWhenIdle = campGuardsFaceFire;
                }
                else
                {
                    // Patrol enemies follow the group center, which the patrol routine moves.
                    ai.anchorTransform = transform;
                    ai.roamRadius = roamRadius;
                    ai.roamWhilePassive = true;
                    ai.faceAnchorWhenIdle = false;
                }

                spawnedEnemies.Add(ai);
            }
        }

        if (style == EncounterStyle.Patrol && patrolPoints != null && patrolPoints.Length > 1)
        {
            StartCoroutine(PatrolRoutine());
        }
    }

    private Vector3 GroundedCenter(Vector3 pos)
    {
        // Raycast wins (handles non-terrain ground) with terrain SampleHeight as fallback.
        if (Physics.Raycast(pos + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
        {
            pos.y = hit.point.y;
            return pos;
        }
        if (Terrain.activeTerrain != null)
        {
            pos.y = Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;
        }
        return pos;
    }

    private IEnumerator PatrolRoutine()
    {
        // Let spawned enemies finish their rise-from-ground animation before
        // we start dragging the group center.
        yield return new WaitForSeconds(2f);

        while (true)
        {
            if (AnyAggroed())
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            Transform wp = patrolPoints[currentPatrolIndex];
            if (wp == null)
            {
                currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
                yield return null;
                continue;
            }

            while (true)
            {
                if (AnyAggroed()) yield break;

                Vector3 toWP = wp.position - transform.position;
                toWP.y = 0f;
                float distXZ = toWP.magnitude;
                if (distXZ < 1.2f) break;

                Vector3 step = toWP / distXZ * (patrolMoveSpeed * Time.deltaTime);
                Vector3 nextPos = transform.position + step;
                if (Terrain.activeTerrain != null)
                    nextPos.y = Terrain.activeTerrain.SampleHeight(nextPos) + Terrain.activeTerrain.transform.position.y;
                transform.position = nextPos;

                yield return null;
            }

            yield return new WaitForSeconds(waypointPauseDuration);
            currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
        }
    }

    private void Update()
    {
        // Cleanup check — fires the cleared reward once when everyone is dead/null.
        if (!spawned || clearedFired || spawnedEnemies.Count == 0) return;

        clearedCheckTimer -= Time.deltaTime;
        if (clearedCheckTimer > 0f) return;
        clearedCheckTimer = 1f;

        for (int i = 0; i < spawnedEnemies.Count; i++)
        {
            if (spawnedEnemies[i] != null) return; // someone alive
        }

        clearedFired = true;
        OnGroupCleared();
    }

    private void OnGroupCleared()
    {
        if (TutorialHints.Instance != null)
            TutorialHints.Instance.ShowIfNew("EncounterCleared",
                "Cleared encounter — bonus loot dropped at the camp center. Wipe more groups to stack rewards.", 5f);

        if (clearedRewardPrefab == null || clearedRewardCount <= 0) return;

        Vector3 center = transform.position;
        if (Terrain.activeTerrain != null)
            center.y = Terrain.activeTerrain.SampleHeight(center) + Terrain.activeTerrain.transform.position.y;

        for (int i = 0; i < clearedRewardCount; i++)
        {
            Vector2 off = Random.insideUnitCircle * 1.2f;
            Vector3 pos = center + new Vector3(off.x, 0.5f, off.y);

            GameObject reward = null;
            if (ObjectPoolManager.Instance != null)
                reward = ObjectPoolManager.Instance.SpawnFromPool(clearedRewardPrefab, pos, Quaternion.identity);
            if (reward == null)
                Instantiate(clearedRewardPrefab, pos, Quaternion.identity);
        }
    }

    private bool AnyAggroed()
    {
        for (int i = 0; i < spawnedEnemies.Count; i++)
        {
            EnemyAI e = spawnedEnemies[i];
            if (e != null && e.IsAggroed) return true;
        }
        return false;
    }

    public void AlertAll()
    {
        for (int i = 0; i < spawnedEnemies.Count; i++)
        {
            EnemyAI e = spawnedEnemies[i];
            if (e != null) e.Aggro();
        }
    }
}
