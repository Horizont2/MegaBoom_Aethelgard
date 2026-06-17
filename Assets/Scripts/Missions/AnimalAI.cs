using UnityEngine;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
public abstract class AnimalAI : MonoBehaviour
{
    public enum AnimalState { Idle, Wander, Flee, MoveToPOI, InteractPOI, Jumping }

    [Header("Base FSM Settings")]
    public AnimalState currentState = AnimalState.Idle;
    public float wanderRadius = 10f;
    public float minStateTime = 3f;
    public float maxStateTime = 8f;

    [Header("Sensors")]
    public float detectionRadius = 5f;
    public LayerMask playerLayer;

    [Header("Saving System")]
    [Tooltip("Унікальний ID для збереження (напр. Cat_01, Chicken_05). Має бути різним для кожної тварини!")]
    public string uniqueID;
    public bool shouldSavePosition = true;

    protected NavMeshAgent agent;
    protected Animator anim;

    // ФІКС ОПТИМІЗАЦІЇ: Кешуємо гравця і його контролер один раз!
    protected Transform player;
    protected CharacterController playerCC;

    protected float stateTimer;
    protected Vector3 startPosition;

    private float smoothedSpeed;
    private Vector3 lastPosition;

    protected virtual void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.applyRootMotion = false;

        startPosition = transform.position;
        lastPosition = transform.position;

        agent.updateRotation = false;
        agent.autoTraverseOffMeshLink = false;
        agent.acceleration = 12f;
        agent.angularSpeed = 0f;

        // Отримуємо компоненти гравця раз і назавжди
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerCC = p.GetComponent<CharacterController>();
        }
    }

    protected virtual void Start()
    {
        if (shouldSavePosition && !string.IsNullOrEmpty(uniqueID))
        {
            LoadAnimalPosition();
        }
    }

    protected virtual void Update()
    {
        if (currentState != AnimalState.Jumping)
        {
            HandleAnimations();
            HandleSmoothRotation();
        }

        if (agent.isOnOffMeshLink && currentState != AnimalState.Jumping)
        {
            StartCoroutine(HandleJumpRoutine());
            return;
        }

        if (currentState == AnimalState.Jumping) return;

        CheckPlayerPresence();

        switch (currentState)
        {
            case AnimalState.Idle: UpdateIdle(); break;
            case AnimalState.Wander: UpdateWander(); break;
            case AnimalState.Flee: UpdateFlee(); break;
            case AnimalState.MoveToPOI: UpdateMoveToPOI(); break;
            case AnimalState.InteractPOI: UpdateInteractPOI(); break;
        }
    }

    protected abstract void UpdateIdle();
    protected abstract void UpdateWander();
    protected abstract void UpdateFlee();
    protected abstract void UpdateMoveToPOI();
    protected abstract void UpdateInteractPOI();
    protected virtual void CheckPlayerPresence() { }

    public virtual void ChangeState(AnimalState newState)
    {
        currentState = newState;
        stateTimer = 0f;
    }

    protected virtual void HandleAnimations()
    {
        if (anim != null)
        {
            float currentSpeed = agent.velocity.magnitude;
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, currentSpeed, Time.deltaTime * 10f);

            if (smoothedSpeed < 0.1f) smoothedSpeed = 0f;
            anim.SetFloat("Speed", smoothedSpeed);
        }
    }

    private void HandleSmoothRotation()
    {
        if (agent.velocity.sqrMagnitude > 0.01f)
        {
            Vector3 direction = agent.velocity.normalized;
            direction.y = 0;

            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 8f);
        }
    }

    private IEnumerator HandleJumpRoutine()
    {
        AnimalState previousState = currentState;
        ChangeState(AnimalState.Jumping);

        if (anim != null) anim.SetFloat("Speed", 4.5f);

        OffMeshLinkData data = agent.currentOffMeshLinkData;
        Vector3 startPos = transform.position;
        Vector3 endPos = data.endPos + Vector3.up * agent.baseOffset;

        float jumpDistance = Vector3.Distance(startPos, endPos);
        float duration = Mathf.Clamp(jumpDistance / 5f, 0.4f, 1.2f);
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = time / duration;

            float yOffset = Mathf.Sin(t * Mathf.PI) * Mathf.Max(0.6f, jumpDistance * 0.4f);
            transform.position = Vector3.Lerp(startPos, endPos, t) + Vector3.up * yOffset;

            Vector3 dir = (endPos - startPos).normalized;
            dir.y = 0;
            if (dir != Vector3.zero) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 15f);

            yield return null;
        }

        transform.position = endPos;
        agent.CompleteOffMeshLink();

        ChangeState(previousState == AnimalState.MoveToPOI ? AnimalState.MoveToPOI : AnimalState.Idle);
    }

    private void SaveAnimalPosition()
    {
        if (!shouldSavePosition || string.IsNullOrEmpty(uniqueID)) return;

        PlayerPrefs.SetFloat(uniqueID + "_PosX", transform.position.x);
        PlayerPrefs.SetFloat(uniqueID + "_PosY", transform.position.y);
        PlayerPrefs.SetFloat(uniqueID + "_PosZ", transform.position.z);
        PlayerPrefs.SetFloat(uniqueID + "_RotY", transform.rotation.eulerAngles.y);
        PlayerPrefs.SetInt(uniqueID + "_HasSave", 1);
        PlayerPrefs.Save();
    }

    private void LoadAnimalPosition()
    {
        if (PlayerPrefs.GetInt(uniqueID + "_HasSave", 0) == 1)
        {
            float x = PlayerPrefs.GetFloat(uniqueID + "_PosX");
            float y = PlayerPrefs.GetFloat(uniqueID + "_PosY");
            float z = PlayerPrefs.GetFloat(uniqueID + "_PosZ");
            float rotY = PlayerPrefs.GetFloat(uniqueID + "_RotY");

            agent.enabled = false;
            transform.position = new Vector3(x, y, z);
            transform.rotation = Quaternion.Euler(0, rotY, 0);
            agent.enabled = true;
        }
    }

    protected Vector3 GetRandomNavMeshPoint(Vector3 origin, float distance)
    {
        Vector3 randomDirection = Random.insideUnitSphere * distance;
        randomDirection += origin;
        NavMeshHit navHit;

        if (NavMesh.SamplePosition(randomDirection, out navHit, distance, NavMesh.AllAreas))
        {
            return navHit.position;
        }
        return origin;
    }

    private void OnDestroy()
    {
        SaveAnimalPosition();
    }
}