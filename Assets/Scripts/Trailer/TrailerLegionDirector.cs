using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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

    [Header("Throw polish")]
    [Tooltip("How far BEHIND the lens the weapon is aimed. The throw used to target a point 20cm in FRONT of the camera, so the axe slowed to a stop just short of it and you watched it park in mid-air. It has to pass the lens.")]
    public float throwOvershoot = 1.6f;
    [Tooltip("How much bigger the weapon gets by the time it reaches the lens. 1.8 was not enough to fill the frame, which is what sells a thrown object coming at you.")]
    public float impactScaleMultiplier = 4.2f;
    [Tooltip("Full rotations the weapon makes on its way to the camera, about one fixed axis across the flight direction.")]
    public float throwSpins = 2.25f;

    [Header("Impact transition")]
    [Tooltip("Smash to black on contact and hold, which is a cut the next episode can start from. The old ending panned the camera up to an empty sky over six tenths of a second, which reads as deliberate camera work rather than as being hit.")]
    public bool smashToBlack = true;
    public Color impactFadeColor = Color.black;
    public float impactFadeDuration = 0.14f;
    public float holdBlackDuration = 0.35f;
    [Tooltip("Raised when the episode is over and the screen is fully black - hook the next episode up to this.")]
    public UnityEngine.Events.UnityEvent onEpisodeFinished;
    [Tooltip("Keep the old pan-to-sky ending instead of the smash cut.")]
    public bool useSkyPan = false;

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

            // ==== THE FOOTFALL SHAKE LASTED ONE FRAME ====
            //
            // currentShake was set on the single frame the timer tripped and was
            // zero on every other one - and the camera position is then eased
            // toward it at deltaTime * 5, so a one-frame spike moved the camera
            // by about two per cent of the intended amount and vanished. The
            // giant's steps did not register at all.
            //
            // An impulse that DECAYS reads as weight: the hit lands, the camera
            // drops, it recovers over about a fifth of a second.
            stepTimer += Time.deltaTime * marchSpeed;
            if (stepTimer >= stepFrequency)
            {
                stepImpulse = stepShakeIntensity;
                stepTimer = 0f;
            }
            stepImpulse = Mathf.MoveTowards(stepImpulse, 0f, Time.deltaTime * stepShakeIntensity * 5f);
            float currentShake = -stepImpulse;   // the ground drops away under the step

            // Two different frequencies, so the drift wanders like a held camera
            // instead of tracing the same diagonal back and forth.
            handheldDrift = new Vector3(
                Mathf.PerlinNoise(Time.time * 0.55f, 11.3f) - 0.5f,
                Mathf.PerlinNoise(37.7f, Time.time * 0.83f) - 0.5f,
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

    private float stepImpulse;

    private void OnDisable()
    {
        // Every beat below plays with Time.timeScale. Leaving the scene, or
        // stopping play, part-way through one used to leave the whole game in
        // slow motion.
        if (Time.timeScale < 0.999f) Time.timeScale = 1f;
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

        yield return new WaitForSeconds(Mathf.Max(0f, timeUntilThrow - tiltUpDuration));

        isBossMarching = false;
        if (bossAnimator != null) bossAnimator.SetTrigger("Throw");

        StartCoroutine(StopArmyWithInertia());

        // ==== SLOW MOTION IS A RAMP, NOT A SWITCH ====
        //
        // This snapped timeScale from 1 to 0.15 and, at the end of the windup,
        // straight back to 1. Both reads as the game stuttering rather than as
        // the moment stretching: there is no acceleration, so the eye registers
        // a dropped frame. A tenth of a second of ramp at each end is all it
        // takes, and it is what makes the release feel like a release.
        StartCoroutine(RampTimeScale(0.15f, 0.12f));
        StartCoroutine(SmoothZoomRoutine(cinematicZoomFOV, animationWindupTime));
        yield return new WaitForSecondsRealtime(animationWindupTime);

        // Release flinch - the camera recoils a touch as the arm comes through.
        if (mainCamera != null)
        {
            mainCamera.position -= mainCamera.forward * 0.2f;
            mainCamera.position += new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f), 0f);
        }

        yield return StartCoroutine(RampTimeScale(1f, 0.08f));

        if (bossWeapon != null) bossWeapon.parent = null;
        StartCoroutine(WeaponFlightRoutine());
    }

    private IEnumerator RampTimeScale(float target, float duration)
    {
        float start = Time.timeScale;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duration);
            Time.timeScale = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        Time.timeScale = target;
    }

    private IEnumerator StopArmyWithInertia()
    {
        yield return new WaitForSecondsRealtime(0.3f);
        isArmyMarching = false;

        // ==== EIGHTY SKELETONS DO NOT STOP ON ONE FRAME ====
        //
        // anim.speed = 0f froze the entire legion mid-stride, every one of them
        // on the same frame, which looks like the game hitching rather than like
        // an army halting. Easing the playback to nothing over a quarter of a
        // second lets each of them finish the step they were in - and because
        // they were started at a random normalised time, they settle raggedly,
        // which is exactly how a real formation stops.
        var anims = new List<Animator>(skeletons.Length);
        foreach (var skel in skeletons)
        {
            if (skel == null) continue;
            Animator a = skel.GetComponent<Animator>();
            if (a != null) anims.Add(a);
        }

        float t = 0f;
        const float settle = 0.28f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / settle;
            float s = Mathf.Lerp(1f, 0f, Mathf.SmoothStep(0f, 1f, t));
            for (int i = 0; i < anims.Count; i++) if (anims[i] != null) anims[i].speed = s;
            yield return null;
        }
        for (int i = 0; i < anims.Count; i++) if (anims[i] != null) anims[i].speed = 0f;
    }

    private IEnumerator SmoothZoomRoutine(float targetFOV, float duration)
    {
        Camera cam = mainCamera != null ? mainCamera.GetComponent<Camera>() : null;
        if (cam == null) yield break;

        float startFOV = cam.fieldOfView;
        float t = 0;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duration);
            cam.fieldOfView = Mathf.LerpUnclamped(startFOV, targetFOV, zoomCurve.Evaluate(t));
            yield return null;
        }
    }

    private IEnumerator WeaponFlightRoutine()
    {
        if (bossWeapon == null || mainCamera == null) yield break;

        // A thrown weapon is scenery in flight - nothing on it should collide
        // with the ground or the army on the way past.
        foreach (var c in bossWeapon.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        var trails = bossWeapon.GetComponentsInChildren<TrailRenderer>(true);
        foreach (var tr in trails) { tr.Clear(); tr.enabled = true; tr.emitting = true; }

        Vector3 startPos = bossWeapon.position;
        Vector3 originalScale = bossWeapon.localScale;
        Quaternion startRot = bossWeapon.rotation;

        // ==== IT HAS TO PASS THE LENS, NOT PARK IN FRONT OF IT ====
        //
        // The target was camera.position + camera.forward * 0.2f - a point
        // twenty centimetres IN FRONT of the camera. So the axe decelerated into
        // a stop just short of the glass and sat there while the impact routine
        // ran, which is most of why the hit looked fake. Aiming BEHIND the lens
        // means the last frames are the weapon filling and leaving the frame,
        // and the eye never sees it stop.
        Vector3 aim = mainCamera.position - mainCamera.forward * throwOvershoot;

        // One fixed axis across the flight, worked out up front. Rotate() about
        // the weapon's own right accumulated a different tumble every run and
        // was frame-rate dependent into the bargain.
        Vector3 flightDir = (aim - startPos).normalized;
        Vector3 spinAxis = Vector3.Cross(flightDir, Vector3.up);
        if (spinAxis.sqrMagnitude < 0.001f) spinAxis = Vector3.right;
        spinAxis.Normalize();

        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, weaponFlightDuration);
            float k = weaponFlightCurve.Evaluate(t);

            // Re-read the camera each frame: it is still drifting, and a throw
            // aimed at where the lens WAS misses it by the drift.
            Vector3 target = mainCamera.position - mainCamera.forward * throwOvershoot;
            Vector3 pos = Vector3.LerpUnclamped(startPos, target, k);
            pos.y += Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * weaponArcHeight;
            bossWeapon.position = pos;

            // Scale on a curve rather than linearly, so it reads as perspective
            // - almost nothing for most of the flight, then everything in the
            // last few frames.
            float grow = Mathf.Lerp(1f, impactScaleMultiplier, k * k);
            bossWeapon.localScale = originalScale * grow;

            bossWeapon.rotation = Quaternion.AngleAxis(throwSpins * 360f * k, spinAxis) * startRot;

            yield return null;
        }

        foreach (var tr in trails) tr.emitting = false;
        StartCoroutine(CameraImpactRoutine());
    }

    private IEnumerator CameraImpactRoutine()
    {
        // The hit itself: a hard stop, a white blow-out from the key light, and
        // a rotational kick. A translation alone reads as the camera being
        // nudged; a camera that is STRUCK rolls.
        Time.timeScale = 0f;

        Quaternion preHit = mainCamera != null ? mainCamera.rotation : Quaternion.identity;
        if (mainCamera != null)
        {
            mainCamera.position -= mainCamera.forward * 0.6f;
            mainCamera.rotation = preHit * Quaternion.Euler(Random.Range(-9f, -4f),
                                                            Random.Range(-6f, 6f),
                                                            Random.Range(12f, 22f));
        }

        float lightIntensity = 0f;
        Color lightColor = Color.white;
        if (mainDirectionalLight != null)
        {
            lightIntensity = mainDirectionalLight.intensity;
            lightColor = mainDirectionalLight.color;
            mainDirectionalLight.color = lightningColor;
            mainDirectionalLight.intensity = 20f;
        }

        yield return new WaitForSecondsRealtime(0.08f);

        if (useSkyPan || !smashToBlack)
        {
            Time.timeScale = 1f;
            if (mainDirectionalLight != null)
            {
                mainDirectionalLight.color = lightColor;
                mainDirectionalLight.intensity = lightIntensity;
            }

            Quaternion startRot = mainCamera.rotation;
            Quaternion skyRot = Quaternion.Euler(-90f, startRot.eulerAngles.y, 0f);

            float p = 0;
            while (p < 1f)
            {
                p += Time.deltaTime / Mathf.Max(0.01f, cameraImpactDuration);
                mainCamera.rotation = Quaternion.SlerpUnclamped(startRot, skyRot, impactPanCurve.Evaluate(p));
                yield return null;
            }
            onEpisodeFinished?.Invoke();
            yield break;
        }

        // ==== SMASH TO BLACK ====
        //
        // The old ending panned up to an empty sky over six tenths of a second.
        // A slow, smooth, perfectly-controlled move immediately after a weapon
        // hits the lens tells the audience that nothing actually happened - and
        // it ends the episode on a shot of nothing, which is a hard place to cut
        // from.
        //
        // The axe filling the frame IS the wipe. Take the screen on contact and
        // hold it: the next episode starts from black, which is a cut, not a
        // transition that has to be watched.
        CanvasGroup veil = BuildImpactVeil();

        float f = 0f;
        while (f < 1f)
        {
            f += Time.unscaledDeltaTime / Mathf.Max(0.01f, impactFadeDuration);
            if (veil != null) veil.alpha = Mathf.Clamp01(f);
            yield return null;
        }
        if (veil != null) veil.alpha = 1f;

        // Only once the screen is covered does anything get put back, so the
        // audience never sees the light snap or the camera reset.
        Time.timeScale = 1f;
        if (mainDirectionalLight != null)
        {
            mainDirectionalLight.color = lightColor;
            mainDirectionalLight.intensity = lightIntensity;
        }

        yield return new WaitForSecondsRealtime(holdBlackDuration);
        onEpisodeFinished?.Invoke();
    }

    // Built in code so the episode carries its own transition and cannot be
    // broken by somebody rearranging the scene's canvases.
    private CanvasGroup BuildImpactVeil()
    {
        var go = new GameObject("[TrailerImpactVeil]");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var imgGo = new GameObject("Veil", typeof(RectTransform));
        imgGo.transform.SetParent(go.transform, false);
        var img = imgGo.AddComponent<UnityEngine.UI.Image>();
        img.color = impactFadeColor;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        return group;
    }
}
