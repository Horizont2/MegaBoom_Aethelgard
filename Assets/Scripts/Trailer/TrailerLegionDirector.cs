using UnityEngine;
using System.Collections;

public class TrailerLegionDirector : MonoBehaviour
{
    [Header("Налаштування сцени")]
    public Transform mainCamera;
    public Transform bossTransform;
    public Animator bossAnimator;
    public Transform bossWeapon;

    [Header("Кінематографічні Криві (Smoothness)")]
    public AnimationCurve introTiltCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public AnimationCurve zoomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public AnimationCurve weaponFlightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public AnimationCurve impactPanCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Камера (Позиція та Рейки)")]
    public float initialCameraDistance = 12f;
    public float cameraHeight = 1.2f;
    public float cameraDollySpeed = 1f;
    public Vector3 bossLookOffset = new Vector3(0f, 3f, 0f);

    [Header("Камера (Ефекти)")]
    public float tiltUpDuration = 2.5f;
    public float cinematicZoomFOV = 30f;
    public float stepShakeIntensity = 0.3f;
    public float stepFrequency = 1.5f;

    [Header("Атмосфера")]
    public Light mainDirectionalLight;
    public Color mainLightColor = new Color(0.1f, 0.12f, 0.2f);
    public float mainLightIntensity = 0.15f;
    public Color lightningColor = new Color(0.8f, 0.9f, 1f);

    [Header("Натовп (Скелети)")]
    public GameObject skeletonPrefab;
    public int skeletonCount = 80;
    public float spawnWidth = 7f;
    public float spawnDistanceMin = -3f;
    public float spawnDistanceMax = -40f;
    public float marchSpeed = 2.5f;

    [Header("Таймінги та Фізика Кидка")]
    public float timeUntilThrow = 4.5f;
    public float animationWindupTime = 0.8f;
    public float weaponFlightDuration = 0.4f;
    public float cameraImpactDuration = 0.6f;
    [Tooltip("Висота дуги польоту. Робить кидок важким і фізичним.")]
    public float weaponArcHeight = 1.5f;
    [Tooltip("У скільки разів зброя збільшиться перед ударом в екран (Штучна перспектива)")]
    public float weaponScaleMultiplier = 1.8f;

    private Transform[] skeletons;
    private bool isBossMarching = true;
    private bool isArmyMarching = true;
    private bool introFinished = false;
    private Vector3 currentCameraPos;
    private float stepTimer = 0f;
    private Vector3 handheldDrift;

    private void Start()
    {
        SetupStaticCamera();
        SpawnLegion();
        StartCoroutine(CinematicRoutine());
    }

    private void SetupStaticCamera()
    {
        if (mainCamera != null && bossTransform != null)
        {
            currentCameraPos = bossTransform.position + (bossTransform.forward * initialCameraDistance);
            currentCameraPos.y = GetTerrainHeight(currentCameraPos) + cameraHeight;
            mainCamera.position = currentCameraPos;
        }
    }

    private void SpawnLegion()
    {
        skeletons = new Transform[skeletonCount];
        for (int i = 0; i < skeletonCount; i++)
        {
            Vector3 randomOffset = new Vector3(
                Random.Range(-spawnWidth, spawnWidth), 0, Random.Range(spawnDistanceMin, spawnDistanceMax)
            );
            Vector3 spawnPos = bossTransform.TransformPoint(randomOffset);
            spawnPos.y = GetTerrainHeight(spawnPos);

            GameObject skel = Instantiate(skeletonPrefab, spawnPos, bossTransform.rotation);
            Animator skelAnim = skel.GetComponent<Animator>();
            if (skelAnim != null) skelAnim.Play("Walk", 0, Random.value);
            skeletons[i] = skel.transform;
        }
    }

    private void Update()
    {
        if (isBossMarching)
        {
            bossTransform.Translate(Vector3.forward * marchSpeed * Time.deltaTime);
            SnapToGround(bossTransform);
        }

        if (isArmyMarching)
        {
            foreach (var skel in skeletons)
            {
                if (skel != null)
                {
                    skel.Translate(Vector3.forward * marchSpeed * Time.deltaTime);
                    SnapToGround(skel);
                }
            }
        }

        if (mainCamera != null && introFinished && isBossMarching)
        {
            currentCameraPos += bossTransform.forward * cameraDollySpeed * Time.deltaTime;
            float targetHeight = GetTerrainHeight(currentCameraPos) + cameraHeight;
            currentCameraPos.y = Mathf.Lerp(currentCameraPos.y, targetHeight, Time.deltaTime * 2f);

            stepTimer += Time.deltaTime * marchSpeed;
            float currentShake = 0f;
            if (stepTimer >= stepFrequency)
            {
                currentShake = stepShakeIntensity;
                stepTimer = 0f;
            }

            handheldDrift = new Vector3(
                Mathf.PerlinNoise(Time.time, 0) - 0.5f,
                Mathf.PerlinNoise(0, Time.time) - 0.5f,
                0) * 0.15f;

            Vector3 finalPos = currentCameraPos + handheldDrift + new Vector3(0, currentShake, 0);
            mainCamera.position = Vector3.Lerp(mainCamera.position, finalPos, Time.deltaTime * 5f);

            Quaternion targetRotation = Quaternion.LookRotation((bossTransform.position + bossLookOffset) - mainCamera.position);
            mainCamera.rotation = Quaternion.Slerp(mainCamera.rotation, targetRotation, Time.deltaTime * 4f);
        }
    }

    private void SnapToGround(Transform obj)
    {
        Vector3 pos = obj.position;
        pos.y = GetTerrainHeight(pos);
        obj.position = pos;
    }

    private float GetTerrainHeight(Vector3 position)
    {
        if (Terrain.activeTerrain != null)
        {
            return Terrain.activeTerrain.SampleHeight(position) + Terrain.activeTerrain.transform.position.y;
        }
        return position.y;
    }

    private IEnumerator CinematicRoutine()
    {
        if (mainCamera != null)
        {
            Vector3 targetLookPos = bossTransform.position + bossLookOffset;
            Quaternion finalRotation = Quaternion.LookRotation(targetLookPos - mainCamera.position);
            Quaternion startRotation = Quaternion.Euler(60f, finalRotation.eulerAngles.y, 0f);

            float t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime / tiltUpDuration;
                float curveVal = introTiltCurve.Evaluate(t);
                mainCamera.rotation = Quaternion.SlerpUnclamped(startRotation, finalRotation, curveVal);
                yield return null;
            }
        }
        introFinished = true;

        yield return new WaitForSeconds(timeUntilThrow - tiltUpDuration);

        // Бос зупиняється, але армія за інерцією йде ще 0.3 секунди
        isBossMarching = false;
        if (bossAnimator != null) bossAnimator.SetTrigger("Throw");

        StartCoroutine(StopArmyWithInertia());

        Time.timeScale = 0.15f;
        StartCoroutine(SmoothZoomRoutine(cinematicZoomFOV, animationWindupTime));
        yield return new WaitForSecondsRealtime(animationWindupTime);

        // Мікро-шок від випуску зброї (Release Flinch)
        mainCamera.position -= mainCamera.forward * 0.2f;
        mainCamera.position += new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f), 0);

        Time.timeScale = 1f;
        bossWeapon.parent = null;
        StartCoroutine(WeaponFlightRoutine());
    }

    private IEnumerator StopArmyWithInertia()
    {
        yield return new WaitForSecondsRealtime(0.3f); // Інерція натовпу
        isArmyMarching = false;

        // Зупиняємо анімації скелетів
        foreach (var skel in skeletons)
        {
            if (skel != null)
            {
                Animator anim = skel.GetComponent<Animator>();
                if (anim != null) anim.speed = 0f;
            }
        }
    }

    private IEnumerator SmoothZoomRoutine(float targetFOV, float duration)
    {
        Camera cam = mainCamera.GetComponent<Camera>();
        float startFOV = cam.fieldOfView;
        float t = 0;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duration;
            cam.fieldOfView = Mathf.LerpUnclamped(startFOV, targetFOV, zoomCurve.Evaluate(t));
            yield return null;
        }
    }

    private IEnumerator WeaponFlightRoutine()
    {
        Vector3 startPos = bossWeapon.position;
        Vector3 originalScale = bossWeapon.localScale;
        float t = 0;

        while (t < 1f)
        {
            t += Time.deltaTime / weaponFlightDuration;
            float curveVal = weaponFlightCurve.Evaluate(t);

            // 1. Плавний рух вперед
            Vector3 targetPos = mainCamera.position + (mainCamera.forward * 0.2f);
            Vector3 currentPos = Vector3.LerpUnclamped(startPos, targetPos, curveVal);

            // 2. Додавання параболічної дуги (Синус)
            float arc = Mathf.Sin(curveVal * Mathf.PI) * weaponArcHeight;
            currentPos.y += arc;

            bossWeapon.position = currentPos;

            // 3. Штучна перспектива (збільшення розміру перед ударом)
            bossWeapon.localScale = Vector3.Lerp(originalScale, originalScale * weaponScaleMultiplier, curveVal);

            float spinSpeed = Mathf.Lerp(500f, 3500f, curveVal);
            bossWeapon.Rotate(Vector3.right * spinSpeed * Time.deltaTime);

            yield return null;
        }

        StartCoroutine(CameraImpactRoutine());
    }

    private IEnumerator CameraImpactRoutine()
    {
        Time.timeScale = 0f;
        mainCamera.position -= mainCamera.forward * 0.6f;

        if (mainDirectionalLight != null)
        {
            mainDirectionalLight.color = lightningColor;
            mainDirectionalLight.intensity = 20f;
        }

        yield return new WaitForSecondsRealtime(0.08f);
        Time.timeScale = 1f;

        Quaternion startRot = mainCamera.rotation;
        Quaternion skyRot = Quaternion.Euler(-90f, startRot.eulerAngles.y, 0f);

        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime / cameraImpactDuration;
            float curveVal = impactPanCurve.Evaluate(t);
            mainCamera.rotation = Quaternion.SlerpUnclamped(startRot, skyRot, curveVal);
            yield return null;
        }
    }
}