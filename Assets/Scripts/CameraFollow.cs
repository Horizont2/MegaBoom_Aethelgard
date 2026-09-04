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
    [Tooltip("Layers the camera boom collides with. Leave as Nothing to use everything except the player, enemies and Ignore Raycast — safer than an inspector mask that happens to omit the layer a location's houses sit on.")]
    public LayerMask collisionLayers;
    public float positionSmoothTime = 0.1f;

    [Header("Camera body")]
    [Tooltip("Radius of the camera's collision probe. Must be big enough to cover the near-clip plane's corners, or walls and floors show through even though the camera's centre is outside them.")]
    public float cameraRadius = 0.34f;
    [Tooltip("Clearance kept above ANY ground — terrain, a location's own floor mesh, a road. Measured with a real raycast, so it works where Terrain.SampleHeight cannot: over a punched terrain hole, on a location's own ground, and outside the terrain bounds entirely.")]
    public float groundClearance = 0.5f;

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
        // A fat probe, sized to the camera body, and ALL hits rather than the
        // first: the old 0.1 probe let the near-clip plane sit inside walls, and
        // taking only the first hit meant a trigger volume or the player's own
        // collider could shadow the wall behind it.
        int n = Physics.SphereCastNonAlloc(lookAtPos, cameraRadius, direction, s_boomHits,
                                           currentDistance, ResolvedCollisionMask(), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = s_boomHits[i].collider;
            if (c == null || IsSelfCollider(c)) continue;
            // IGNORE the ground/terrain for the boom — the ground clamp below
            // keeps the camera above the floor. Letting the ground pull the boom
            // in made the camera bob/jitter when tilted down (pull in -> rise ->
            // miss -> extend -> hit again). REAL obstacles (walls) still pull in
            // instantly so the camera never clips through them.
            if (IsGroundCollider(c)) continue;
            // distance 0 means the probe STARTED inside this collider — the sweep
            // gives no usable distance, so fall back to the minimum boom.
            float d = s_boomHits[i].distance <= 0.0001f ? minDistance : s_boomHits[i].distance;
            hitDistance = Mathf.Min(hitDistance, Mathf.Clamp(d, minDistance, currentDistance));
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

        ClampAboveGround(dt);
        PushOutOfGeometry(lookAtPos);
    }

    // Keep the camera above whatever the ground actually IS at this spot.
    //
    // The old version only used Terrain.SampleHeight, which meant no protection
    // at all wherever there is no terrain under the camera: outside the terrain
    // bounds, over a punched terrain hole, and on any location standing on its
    // own floor mesh — exactly where tilting down put the camera underground.
    // A real downward raycast covers all of those, and the terrain sample stays
    // as a backstop for when the ray finds nothing.
    private void ClampAboveGround(float dt)
    {
        float groundY = float.NegativeInfinity;

        int n = Physics.RaycastNonAlloc(transform.position + Vector3.up * 6f, Vector3.down, s_groundHits,
                                        14f, ResolvedCollisionMask(), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = s_groundHits[i].collider;
            if (c == null || IsSelfCollider(c)) continue;
            if (!IsGroundCollider(c)) continue;
            if (s_groundHits[i].point.y > groundY) groundY = s_groundHits[i].point.y;
        }

        if (float.IsNegativeInfinity(groundY))
        {
            Terrain relevant = GetTerrainAt(transform.position);
            if (relevant == null) return;
            groundY = relevant.SampleHeight(transform.position) + relevant.transform.position.y;
        }

        // Clear the near-clip plane too, or the camera body is above the floor
        // while the plane it renders through is below it — which is how you end
        // up seeing what is under the ground.
        float clearance = groundClearance + (camComponent != null ? camComponent.nearClipPlane : 0.3f);
        float minY = groundY + clearance;
        if (transform.position.y >= minY) return;

        Vector3 safePos = transform.position;
        // Ease up to the floor rather than hard-snapping every frame — the hard
        // snap fought the boom and made the camera shake near the ground.
        safePos.y = Mathf.Lerp(transform.position.y, minY, 1f - Mathf.Exp(-14f * dt));
        // Absolute floor, so it can never be under the ground even mid-ease.
        float hard = groundY + groundClearance * 0.5f;
        if (safePos.y < hard) safePos.y = hard;
        transform.position = safePos;
    }

    // Last line of defence against ending up INSIDE a house. The boom sweep can
    // miss when it starts inside geometry or when the clamp above has just
    // pushed the camera up through a floor, so if the camera body is overlapping
    // anything solid, walk it back along the boom until it is clear.
    private void PushOutOfGeometry(Vector3 lookAtPos)
    {
        Vector3 toCam = transform.position - lookAtPos;
        float dist = toCam.magnitude;
        if (dist < 0.01f) return;
        Vector3 dir = toCam / dist;

        for (int step = 0; step < 6; step++)
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position, cameraRadius, s_overlap,
                                                  ResolvedCollisionMask(), QueryTriggerInteraction.Ignore);
            bool blocked = false;
            for (int i = 0; i < n; i++)
            {
                var c = s_overlap[i];
                if (c == null || IsSelfCollider(c) || IsGroundCollider(c)) continue;
                blocked = true; break;
            }
            if (!blocked) return;

            dist = Mathf.Max(minDistance, dist - cameraRadius);
            transform.position = lookAtPos + dir * dist;
            if (dist <= minDistance) return;
        }
    }

    private static readonly RaycastHit[] s_boomHits = new RaycastHit[12];
    private static readonly RaycastHit[] s_groundHits = new RaycastHit[12];
    private static readonly Collider[] s_overlap = new Collider[12];

    private bool IsSelfCollider(Collider c)
    {
        if (c == null) return true;
        if (target != null && (c.transform == target || c.transform.IsChildOf(target))) return true;
        return c.transform == transform || c.transform.IsChildOf(transform);
    }

    // An inspector LayerMask that happens to omit the layer a location's houses
    // sit on is why the camera could walk straight into them. Nothing = probe
    // everything except the player, enemies and Ignore Raycast.
    private int ResolvedCollisionMask()
    {
        if (collisionLayers.value != 0) return collisionLayers.value;
        int m = ~0;
        m &= ~(1 << 2);   // Ignore Raycast
        m &= ~(1 << 9);   // enemies
        if (target != null) m &= ~(1 << target.gameObject.layer);
        return m;
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