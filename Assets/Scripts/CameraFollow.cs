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

    private static bool IsGroundCollider(Collider col)
    {
        if (col == null) return false;
        if (col.GetComponentInParent<Terrain>() != null) return true;
        string n = col.name.ToLowerInvariant();
        return n.Contains("terrain") || n.Contains("ground") || n.Contains("floor");
    }

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
        // ДОДАНО: PauseSceneController.IsPauseActive, щоб скрипт відключався під час кінематографічної паузи
        // Also freeze camera whenever a modal UI panel is up — otherwise the
        // mouse the player is using to click buttons keeps rotating the camera
        // and the scroll wheel changes distance while they're scrolling the panel.
        if (isCinematicMode || Time.timeScale == 0f || target == null || TutorialPanelUI.IsTutorialActive || PauseSceneController.IsPauseActive) return;
        if (BarracksUpgradePanel.IsOpen) return;
        if (NoticeBoardManager.IsAnyBoardOpen) return;

        float dt = Time.deltaTime;
        if (dt < 0.0001f) return;

        float currentFrameSpeed = (target.position - lastTargetPos).magnitude / dt;
        smoothedPlayerSpeed = Mathf.Lerp(smoothedPlayerSpeed, currentFrameSpeed, dt * 5f);
        lastTargetPos = target.position;

        currentTargetPos = Vector3.SmoothDamp(currentTargetPos, target.position, ref targetPosVelocity, positionSmoothTime);

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

        float hitDistance = currentDistance;
        if (Physics.SphereCast(lookAtPos, 0.1f, direction, out RaycastHit hit, currentDistance, collisionLayers))
        {
            // IGNORE the ground/terrain for the boom — the terrain Y-clamp below
            // keeps the camera above the floor. Letting the ground pull the boom
            // in made the camera bob/jitter when tilted down (pull in -> rise ->
            // miss -> extend -> hit again). REAL obstacles (walls) still pull in
            // instantly so the camera never clips through them.
            if (!IsGroundCollider(hit.collider))
                hitDistance = Mathf.Clamp(hit.distance, minDistance, currentDistance);
        }

        // Instant pull-in (never clip a wall), slow push-out (no popping).
        if (hitDistance < actualCollisionDistance) actualCollisionDistance = hitDistance;
        else actualCollisionDistance = Mathf.Lerp(actualCollisionDistance, hitDistance, dt * 4f);

        Vector3 finalPosition = lookAtPos + direction * actualCollisionDistance;

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

        Quaternion finalRotation = Quaternion.LookRotation(lookAtPos - finalPosition);
        ApplyHandoffBlendIfActive(ref finalPosition, ref finalRotation, dt);

        transform.position = finalPosition;
        transform.rotation = finalRotation;

        // Виправлено баг з Terrain: тепер захищає тільки якщо ми реально над терейном
        Terrain relevant = GetTerrainAt(transform.position);
        if (relevant != null)
        {
            float terrainHeight = relevant.SampleHeight(transform.position) + relevant.transform.position.y;

            float minY = terrainHeight + 0.35f;
            if (transform.position.y < minY)
            {
                Vector3 safePos = transform.position;
                // Ease the camera up to the floor instead of hard-snapping every
                // frame (frame-rate independent). The hard snap fought the boom
                // and made the camera shake near the ground.
                safePos.y = Mathf.Lerp(transform.position.y, minY, 1f - Mathf.Exp(-14f * dt));
                // Absolute floor so it can still never clip under the ground.
                if (safePos.y < terrainHeight + 0.2f) safePos.y = terrainHeight + 0.2f;
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
        if (PlayerPrefs.GetInt("Settings_ScreenShake", 1) != 1) return;
        // Reduce Motion accessibility: scale shake amplitude way down
        // (not fully off — a hint of feedback remains) so the screen
        // doesn't lurch for motion-sensitive players.
        float m = GameplaySettings.MotionScale;
        shakeTimer = duration;
        currentShakeIntensity = intensity * m;
        directionalShakeForce = 0f;
    }

    public void TriggerDirectionalShake(Vector3 direction, float force, float duration, float randomIntensity)
    {
        if (PlayerPrefs.GetInt("Settings_ScreenShake", 1) != 1) return;
        float m = GameplaySettings.MotionScale;
        shakeTimer = duration;
        shakeDirection = direction.normalized;
        directionalShakeForce = force * m;
        currentShakeIntensity = randomIntensity * m;
    }

    public void StartShake() { TriggerShake(0.2f, 0.3f); }

    public void SyncRotation(float x, float y) { currentX = x; currentY = y; }

    private static Terrain GetTerrainAt(Vector3 worldPos)
    {
        Terrain[] all = Terrain.activeTerrains;
        if (all == null || all.Length == 0) return null; // ФІКС: Більше не беремо рандомний Terrain.activeTerrain
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
        return null; // ФІКС: Повертаємо null, якщо камера поза межами всіх терейнів
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
        SnapToTarget();
    }

    private void ApplyHandoffBlendIfActive(ref Vector3 finalPosition, ref Quaternion finalRotation, float dt)
    {
        if (!isHandoffBlending) return;

        handoffBlendT += dt;
        float k = Mathf.Clamp01(handoffBlendT / handoffBlendDuration);
        float ease = k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) / 2f;

        finalPosition = Vector3.Lerp(handoffStartPos, finalPosition, ease);
        finalRotation = Quaternion.Slerp(handoffStartRot, finalRotation, ease);

        if (k >= 1f) isHandoffBlending = false;
    }
}