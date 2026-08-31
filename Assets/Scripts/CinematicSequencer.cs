using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Authored, self-running cinematic shots for the trailer. Each sequence is a
// list of timed shots that drive Camera.main automatically (orbit / dolly /
// crane / low push), relative to a focus (the player) so they work in any
// scene without hand-placed cameras. Shots can stage "actors" on start (spawn a
// ring of enemies, freeze them for a pose, slow time). Fire a sequence and it
// films itself; the game camera is restored at the end.
//
// Triggered from TrailerToolkit. Kept general (not editor-gated) so the moves
// could be reused for real in-game cutscenes later.
public class CinematicSequencer : MonoBehaviour
{
    public static bool IsPlaying { get; private set; }

    private struct Shot
    {
        public float dur;
        public System.Action onStart;
        public System.Action<Camera, Transform, float> apply;   // camera, focus, t 0..1
    }

    // Fire an authored sequence by name. No-op if one is already running.
    public static void Play(string sequence)
    {
        if (IsPlaying) return;
        var go = new GameObject("CinematicSequencer");
        go.AddComponent<CinematicSequencer>().StartCoroutine(go.GetComponent<CinematicSequencer>().Run(sequence));
    }

    private IEnumerator Run(string sequence)
    {
        IsPlaying = true;

        Camera cam = Camera.main;
        Transform focus = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (cam == null || focus == null) { IsPlaying = false; Destroy(gameObject); yield break; }

        // Hand the camera over from its follow rig.
        var follow = cam.GetComponent("CameraFollow") as MonoBehaviour;
        bool followWas = follow != null && follow.enabled;
        if (follow != null) follow.enabled = false;
        float fov0 = cam.fieldOfView;

        var shots = Build(sequence, focus);

        foreach (var shot in shots)
        {
            shot.onStart?.Invoke();
            float t = 0f;
            while (t < shot.dur)
            {
                t += Time.unscaledDeltaTime;
                shot.apply?.Invoke(cam, focus, Mathf.Clamp01(t / shot.dur));
                yield return null;
            }
        }

        // Restore.
        Time.timeScale = 1f;
        if (EnemyAI.GlobalFreeze) EnemyAI.GlobalFreeze = false;
        cam.fieldOfView = fov0;
        if (follow != null) follow.enabled = followWas;
        IsPlaying = false;
        Destroy(gameObject);
    }

    // -------- Authored sequences --------
    private List<Shot> Build(string name, Transform focus)
    {
        switch (name)
        {
            case "encircle": return Encircle();
            case "crane":    return CraneUp();
            case "orbit":    return Orbit();
            default:          return HeroReveal();
        }
    }

    // Slow low push-in on the player, then a rising orbit. A clean "meet the
    // hero" beat.
    private List<Shot> HeroReveal()
    {
        return new List<Shot>
        {
            Push(dur: 3.0f, dir: 0f,  startDist: 7f, endDist: 3.2f, startH: 1.0f, endH: 1.6f, fov: 42f),
            OrbitShot(dur: 5.0f, radius: 4.5f, startAngle: 20f, endAngle: 160f, height: 2.4f, lookH: 1.4f, fov: 46f),
        };
    }

    // Stage a ring of enemies around the player, freeze them for a beat, orbit
    // low through the crowd, then unfreeze so they surge — the "become the storm"
    // set-up. (Trigger STACK yourself while capturing.)
    private List<Shot> Encircle()
    {
        return new List<Shot>
        {
            new Shot {
                dur = 1.0f,
                onStart = () => { SpawnRing(28, 5f, 8f); EnemyAI.GlobalFreeze = true; },
                apply = (c, f, t) => FrameOrbit(c, f, 6.5f, 40f, 3.0f, 1.4f, 50f)   // hold on the frozen crowd
            },
            OrbitShot(dur: 4.5f, radius: 6.5f, startAngle: 40f, endAngle: 300f, height: 3.0f, lookH: 1.4f, fov: 52f),
            new Shot {
                dur = 0.2f,
                onStart = () => EnemyAI.GlobalFreeze = false,                        // release — they surge
                apply = (c, f, t) => FrameOrbit(c, f, 6.5f, 300f, 3.0f, 1.4f, 52f)
            },
            OrbitShot(dur: 3.0f, radius: 5.5f, startAngle: 300f, endAngle: 380f, height: 2.6f, lookH: 1.3f, fov: 55f),
        };
    }

    private List<Shot> Orbit()
    {
        return new List<Shot> { OrbitShot(dur: 8f, radius: 5.5f, startAngle: 0f, endAngle: 360f, height: 2.8f, lookH: 1.4f, fov: 50f) };
    }

    private List<Shot> CraneUp()
    {
        return new List<Shot>
        {
            new Shot { dur = 5f, apply = (c, f, t) => {
                float h = Mathf.Lerp(2f, 34f, t * t * (3f - 2f * t));
                float back = Mathf.Lerp(4f, 16f, t);
                Vector3 pos = f.position + new Vector3(0f, h, -back);
                c.transform.position = pos;
                c.transform.rotation = Quaternion.LookRotation((f.position + Vector3.up * 1.2f) - pos, Vector3.up);
                c.fieldOfView = Mathf.Lerp(48f, 62f, t);
            }}
        };
    }

    // -------- Shot builders / helpers --------
    private Shot Push(float dur, float dir, float startDist, float endDist, float startH, float endH, float fov)
    {
        return new Shot { dur = dur, apply = (c, f, t) =>
        {
            float k = t * t * (3f - 2f * t);
            Vector3 flatDir = Quaternion.Euler(0f, dir, 0f) * f.forward; flatDir.y = 0f; flatDir.Normalize();
            float dist = Mathf.Lerp(startDist, endDist, k);
            float h = Mathf.Lerp(startH, endH, k);
            Vector3 pos = f.position + flatDir * dist + Vector3.up * h;
            c.transform.position = pos;
            c.transform.rotation = Quaternion.LookRotation((f.position + Vector3.up * 1.3f) - pos, Vector3.up);
            c.fieldOfView = fov;
        }};
    }

    private Shot OrbitShot(float dur, float radius, float startAngle, float endAngle, float height, float lookH, float fov)
    {
        return new Shot { dur = dur, apply = (c, f, t) =>
        {
            float k = t * t * (3f - 2f * t);
            FrameOrbit(c, f, radius, Mathf.Lerp(startAngle, endAngle, k), height, lookH, fov);
        }};
    }

    private void FrameOrbit(Camera c, Transform f, float radius, float angle, float height, float lookH, float fov)
    {
        float rad = angle * Mathf.Deg2Rad;
        Vector3 pos = f.position + new Vector3(Mathf.Cos(rad) * radius, height, Mathf.Sin(rad) * radius);
        c.transform.position = pos;
        c.transform.rotation = Quaternion.LookRotation((f.position + Vector3.up * lookH) - pos, Vector3.up);
        c.fieldOfView = fov;
    }

    private void SpawnRing(int count, float minR, float maxR)
    {
        var spawner = FindFirstObjectByType<EnemySpawner>();
        Transform player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (player == null) return;
        var prefabs = new List<GameObject>();
        if (spawner != null && spawner.enemyPool != null)
            foreach (var e in spawner.enemyPool) if (e != null && e.enemyPrefab != null) prefabs.Add(e.enemyPrefab);
        if (prefabs.Count == 0) return;

        for (int i = 0; i < count; i++)
        {
            float ang = (360f / count) * i + Random.Range(-6f, 6f);
            float r = Random.Range(minR, maxR);
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            Vector3 p = player.position + dir * r;
            if (Physics.Raycast(p + Vector3.up * 14f, Vector3.down, out var hit, 40f)) p.y = hit.point.y;
            else if (Terrain.activeTerrain != null) p.y = Terrain.activeTerrain.SampleHeight(p) + Terrain.activeTerrain.transform.position.y;
            var go = Instantiate(prefabs[Random.Range(0, prefabs.Count)], p, Quaternion.LookRotation(-dir));
        }
    }
}
