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
    public float distanceSmoothTime = 0.5f;

    [Header("Dynamic FOV")]
    public float idleFOV = 60f;
    public float runFOV = 68f;
    public float combatFOV = 65f;
    public float fovTransitionSpeed = 2.5f;

    [Header("Collision & Smoothing")]
    public LayerMask collisionLayers;
    public float positionSmoothTime = 0.1f;

    [Header("Mouse Control")]
    public float mouseSensitivity = 3f;
    public float minYAngle = -20f;
    public float maxYAngle = 80f;

    [Header("Cinematic Bridge")]
    public bool isCinematicMode = false;

    private float shakeTimer;
    private float currentShakeIntensity;
    private Vector3 shakeDirection;
    private float directionalShakeForce;

    private float currentX = 0f;
    private float currentY = 45f;

    private float targetDistance;
    private float currentDistance;
    private float distanceVelocity;
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
        // === ОПТИМІЗАЦІЯ І ФІКС: Блокуємо камеру під час туторіалу ===
        if (isCinematicMode || Time.timeScale == 0f || target == null || TutorialPanelUI.IsTutorialActive) return;

        float dt = Time.deltaTime;
        if (dt < 0.0001f) return;

        // 1. Стабільне обчислення швидкості гравця
        float currentFrameSpeed = (target.position - lastTargetPos).magnitude / dt;
        smoothedPlayerSpeed = Mathf.Lerp(smoothedPlayerSpeed, currentFrameSpeed, dt * 5f);
        lastTargetPos = target.position;

        // 2. Слідування за гравцем
        currentTargetPos = Vector3.SmoothDamp(currentTargetPos, target.position, ref targetPosVelocity, positionSmoothTime);

        // 3. Миттєве, точне керування без желе. Settings panel persists a
        // user-chosen multiplier (Settings_MouseSensitivity, default 1.0)
        // and an invert-Y toggle (Settings_InvertYAxis).
        float sensMul = PlayerPrefs.GetFloat("Settings_MouseSensitivity", 1f);
        float yInvert = PlayerPrefs.GetInt("Settings_InvertYAxis", 0) == 1 ? -1f : 1f;
        currentX += Input.GetAxis("Mouse X") * mouseSensitivity * sensMul;
        currentY -= Input.GetAxis("Mouse Y") * mouseSensitivity * sensMul * yInvert;
        currentY = Mathf.Clamp(currentY, minYAngle, maxYAngle);
        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);

        UpdateCameraState();

        currentDistance = Mathf.SmoothDamp(currentDistance, targetDistance, ref distanceVelocity, distanceSmoothTime);

        if (camComponent != null)
        {
            camComponent.fieldOfView = Mathf.Lerp(camComponent.fieldOfView, targetFOVValue, dt * fovTransitionSpeed);
        }

        Vector3 dynamicOffset = targetOffset;
        if (combatTimer > 0) dynamicOffset.y += 0.3f;

        Vector3 lookAtPos = currentTargetPos + dynamicOffset;
        Vector3 direction = -(rotation * Vector3.forward);

        // 4. Колізії
        float hitDistance = currentDistance;
        if (Physics.SphereCast(lookAtPos, 0.1f, direction, out RaycastHit hit, currentDistance, collisionLayers))
        {
            hitDistance = Mathf.Clamp(hit.distance, minDistance, currentDistance);
        }

        if (hitDistance < actualCollisionDistance) actualCollisionDistance = hitDistance;
        else actualCollisionDistance = Mathf.Lerp(actualCollisionDistance, hitDistance, dt * 4f);

        Vector3 finalPosition = lookAtPos + direction * actualCollisionDistance;

        // 5. Тряска
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

        // Compute the natural target rotation (matches transform.LookAt below),
        // then blend toward it from the snapshot we took when the cutscene ended.
        Quaternion finalRotation = Quaternion.LookRotation(lookAtPos - finalPosition);
        ApplyHandoffBlendIfActive(ref finalPosition, ref finalRotation, dt);

        transform.position = finalPosition;
        transform.rotation = finalRotation;

        // 6. Захист від Terrain — picks the terrain whose XZ footprint
        // actually contains the camera, so a far-away scenery terrain
        // (e.g. PauseLocation prefab with its own terrain) can't drag
        // the camera up. Without this guard, Terrain.activeTerrain
        // returned the most-recently-loaded terrain regardless of
        // distance — that's why placing the pause scenery flipped the
        // gameplay camera high into the sky.
        Terrain relevant = GetTerrainAt(transform.position);
        if (relevant != null)
        {
            float terrainHeight = relevant.SampleHeight(transform.position) + relevant.transform.position.y;

            if (transform.position.y < terrainHeight + 0.3f)
            {
                Vector3 safePos = transform.position;
                safePos.y = terrainHeight + 0.3f;
                transform.position = safePos;

                actualCollisionDistance = Mathf.Lerp(actualCollisionDistance, minDistance, dt * 8f);
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
        // Settings_ScreenShake toggle — when off, every caller's shake
        // request becomes a no-op without each call site having to
        // check the pref individually. Default = on (1).
        if (PlayerPrefs.GetInt("Settings_ScreenShake", 1) != 1) return;
        shakeTimer = duration;
        currentShakeIntensity = intensity;
        directionalShakeForce = 0f;
    }

    public void TriggerDirectionalShake(Vector3 direction, float force, float duration, float randomIntensity)
    {
        if (PlayerPrefs.GetInt("Settings_ScreenShake", 1) != 1) return;
        shakeTimer = duration;
        shakeDirection = direction.normalized;
        directionalShakeForce = force;
        currentShakeIntensity = randomIntensity;
    }

    public void StartShake() { TriggerShake(0.2f, 0.3f); }

    public void SyncRotation(float x, float y) { currentX = x; currentY = y; }

    // Returns the terrain whose footprint contains the given world XZ
    // position. Lets scenes have multiple disjoint terrains (main world
    // + a PauseLocation prefab in another spot) without
    // Terrain.activeTerrain dragging the camera onto the wrong one.
    private static Terrain GetTerrainAt(Vector3 worldPos)
    {
        Terrain[] all = Terrain.activeTerrains;
        if (all == null || all.Length == 0) return Terrain.activeTerrain;
        for (int i = 0; i < all.Length; i++)
        {
            Terrain t = all[i];
            if (t == null || t.terrainData == null) continue;
            Vector3 origin = t.transform.position;
            Vector3 size = t.terrainData.size;
            if (worldPos.x >= origin.x && worldPos.x <= origin.x + size.x &&
                worldPos.z >= origin.z && worldPos.z <= origin.z + size.z)
                return t;
        }
        return Terrain.activeTerrain;
    }

    public void SnapToTarget()
    {
        if (target != null)
        {
            currentTargetPos = target.position;
            lastTargetPos = target.position;
            currentDistance = targetDistance;
            actualCollisionDistance = targetDistance;
        }
    }

    // ---- Cinematic handoff blend ----
    // BeginHandoffBlend captures the camera's current world position + rotation
    // (e.g. wherever Cinemachine left them) and smoothly interpolates the final
    // transform.position/rotation toward whatever CameraFollow computes over the
    // given duration. While the blend is running, every other system in
    // LateUpdate works as normal, and the snap that used to happen on the first
    // post-cutscene frame is gone.
    private bool isHandoffBlending = false;
    private float handoffBlendT;
    private float handoffBlendDuration;
    private Vector3 handoffStartPos;
    private Quaternion handoffStartRot;

    public bool IsHandoffBlending => isHandoffBlending;

    public void BeginHandoffBlend(float duration = 0.6f)
    {
        if (duration <= 0f) { isHandoffBlending = false; return; }
        handoffStartPos = transform.position;
        handoffStartRot = transform.rotation;
        handoffBlendDuration = duration;
        handoffBlendT = 0f;
        isHandoffBlending = true;

        // SnapToTarget so the target-follow numbers (currentTargetPos, distance)
        // start from the player's current state — otherwise the destination
        // we're blending toward would itself be drifting.
        SnapToTarget();
    }

    private void ApplyHandoffBlendIfActive(ref Vector3 finalPosition, ref Quaternion finalRotation, float dt)
    {
        if (!isHandoffBlending) return;

        handoffBlendT += dt;
        float k = Mathf.Clamp01(handoffBlendT / handoffBlendDuration);
        // ease-in-out cubic so the blend feels organic, not linear
        float ease = k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) / 2f;

        finalPosition = Vector3.Lerp(handoffStartPos, finalPosition, ease);
        finalRotation = Quaternion.Slerp(handoffStartRot, finalRotation, ease);

        if (k >= 1f) isHandoffBlending = false;
    }
}