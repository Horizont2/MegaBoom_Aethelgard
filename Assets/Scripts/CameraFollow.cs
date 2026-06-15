using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform target;
    public Vector3 targetOffset = new Vector3(0, 1.5f, 0);

    [Header("Dynamic Distances (AAA Framing)")]
    public float idleDistance = 2.5f;
    public float runDistance = 4.0f;
    public float combatDistance = 5.5f;
    public float minDistance = 0.5f;

    [Tooltip("Наскільки плавно камера змінює дистанцію (SmoothDamp Time)")]
    public float distanceSmoothTime = 0.5f; // ФІКС ПЛАВНОСТІ: Тепер це час пружини

    [Header("Dynamic FOV")]
    public float idleFOV = 60f;
    public float runFOV = 68f;
    public float combatFOV = 65f;
    public float fovTransitionSpeed = 2.5f; // Трохи пом'якшили зум

    [Header("Collision & Smoothing")]
    public LayerMask collisionLayers;
    public float positionSmoothTime = 0.1f;

    [Header("Mouse Control")]
    public float mouseSensitivity = 3f;
    // ФІКС МИШІ: Прибрали желейність, миша тепер миттєва, згладжується лише сама камера
    public float minYAngle = -20f;
    public float maxYAngle = 80f;

    [Header("Cinematic Bridge")]
    public bool isCinematicMode = false;

    // Внутрішні змінні
    private float shakeTimer;
    private float currentShakeIntensity;
    private Vector3 shakeDirection;
    private float directionalShakeForce;

    // Обертання камери
    private float currentX = 0f;
    private float currentY = 45f;

    private float targetDistance;
    private float currentDistance;
    private float distanceVelocity; // Для SmoothDamp
    private float actualCollisionDistance;

    private float targetFOVValue;
    private Camera camComponent;

    private Vector3 currentTargetPos;
    private Vector3 targetPosVelocity;

    private Vector3 lastTargetPos;
    private float smoothedPlayerSpeed;

    private float combatTimer = 0f;
    private const float COMBAT_COOLDOWN = 4.0f;

    private void Start()
    {
        transform.parent = null;
        Cursor.lockState = CursorLockMode.Locked;

        targetDistance = idleDistance;
        currentDistance = idleDistance;
        actualCollisionDistance = idleDistance;

        camComponent = GetComponent<Camera>();
        if (camComponent != null) targetFOVValue = idleFOV;

        if (target != null)
        {
            currentTargetPos = target.position;
            lastTargetPos = target.position;
        }
    }

    private void LateUpdate()
    {
        if (isCinematicMode || Time.timeScale == 0f || target == null) return;

        // 1. Стабільне обчислення швидкості гравця
        float currentFrameSpeed = (target.position - lastTargetPos).magnitude / Time.deltaTime;
        smoothedPlayerSpeed = Mathf.Lerp(smoothedPlayerSpeed, currentFrameSpeed, Time.deltaTime * 5f);
        lastTargetPos = target.position;

        // 2. Слідування за гравцем (трохи м'якше)
        currentTargetPos = Vector3.SmoothDamp(currentTargetPos, target.position, ref targetPosVelocity, positionSmoothTime);

        // 3. ФІКС МИШІ: Миттєве, точне керування без желе
        currentX += Input.GetAxis("Mouse X") * mouseSensitivity;
        currentY -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        currentY = Mathf.Clamp(currentY, minYAngle, maxYAngle);
        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);

        UpdateCameraState();

        // 4. ФІКС ПЛАВНОСТІ КАМЕРИ: Використовуємо SmoothDamp для ефекту м'якої пружини
        currentDistance = Mathf.SmoothDamp(currentDistance, targetDistance, ref distanceVelocity, distanceSmoothTime);

        if (camComponent != null)
        {
            camComponent.fieldOfView = Mathf.Lerp(camComponent.fieldOfView, targetFOVValue, Time.deltaTime * fovTransitionSpeed);
        }

        Vector3 dynamicOffset = targetOffset;
        if (combatTimer > 0) dynamicOffset.y += 0.3f;

        Vector3 lookAtPos = currentTargetPos + dynamicOffset;
        Vector3 direction = -(rotation * Vector3.forward);

        // 5. Колізії (SphereCast)
        float hitDistance = currentDistance;
        if (Physics.SphereCast(lookAtPos, 0.25f, direction, out RaycastHit hit, currentDistance, collisionLayers))
        {
            hitDistance = Mathf.Clamp(hit.distance, minDistance, currentDistance);
        }

        // Якщо камера вдарилась - наближаємо миттєво. Якщо віддаляється від стіни - робимо це плавно.
        if (hitDistance < actualCollisionDistance) actualCollisionDistance = hitDistance;
        else actualCollisionDistance = Mathf.Lerp(actualCollisionDistance, hitDistance, Time.deltaTime * 4f);

        Vector3 finalPosition = lookAtPos + direction * actualCollisionDistance;

        // 6. Тряска
        if (shakeTimer > 0)
        {
            finalPosition += Random.insideUnitSphere * currentShakeIntensity;
            if (directionalShakeForce > 0)
            {
                float pushForce = directionalShakeForce * (shakeTimer / 0.2f);
                finalPosition += shakeDirection * pushForce;
            }
            shakeTimer -= Time.unscaledDeltaTime;
        }
        else
        {
            directionalShakeForce = 0f;
        }

        transform.position = finalPosition;
        transform.LookAt(lookAtPos);

        // 7. Захист від Terrain
        if (Terrain.activeTerrain != null)
        {
            float terrainHeight = Terrain.activeTerrain.SampleHeight(transform.position) + Terrain.activeTerrain.transform.position.y;
            float minCameraHeight = terrainHeight + 1.2f;

            if (transform.position.y < minCameraHeight)
            {
                Vector3 safePos = transform.position;
                safePos.y = minCameraHeight;
                transform.position = safePos;
            }
        }
    }

    private void UpdateCameraState()
    {
        if (combatTimer > 0) combatTimer -= Time.deltaTime;

        bool isRunning = smoothedPlayerSpeed > 4.5f;

        if (combatTimer > 0)
        {
            targetDistance = combatDistance;
            targetFOVValue = combatFOV;
        }
        else if (isRunning)
        {
            targetDistance = runDistance;
            targetFOVValue = runFOV;
        }
        else
        {
            targetDistance = idleDistance;
            targetFOVValue = idleFOV;
        }
    }

    public void SetCombatState() { combatTimer = COMBAT_COOLDOWN; }

    public void TriggerShake(float duration, float intensity)
    {
        shakeTimer = duration;
        currentShakeIntensity = intensity;
        directionalShakeForce = 0f;
    }

    public void TriggerDirectionalShake(Vector3 direction, float force, float duration, float randomIntensity)
    {
        shakeTimer = duration;
        shakeDirection = direction.normalized;
        directionalShakeForce = force;
        currentShakeIntensity = randomIntensity;
    }

    public void StartShake() { TriggerShake(0.2f, 0.3f); }

    // ФІКС СИНХРОНІЗАЦІЇ
    public void SyncRotation(float x, float y) { currentX = x; currentY = y; }
}