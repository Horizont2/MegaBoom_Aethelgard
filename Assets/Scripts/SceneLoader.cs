using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

// Single choke-point for scene transitions. Every call goes through here
// so:
//   * the player always sees a loading overlay (never a frozen frame),
//   * the same fade-out / fade-in cadence is used everywhere,
//   * we can add pre/post hooks (save flush, analytics, resource cleanup)
//     in one place.
//
// If the scene has a wired LoadingManager (with authored art + hints)
// we defer to it — its polished path is preferred. Otherwise we build a
// throw-away black overlay so the transition still hides the seam.
public static class SceneLoader
{
    public static void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[SceneLoader] Empty scene name — ignored.");
            return;
        }

        if (LoadingManager.Instance != null)
        {
            LoadingManager.Instance.LoadScene(sceneName);
            return;
        }

        // Anchor to a persistent GO so the coroutine survives the unload
        // of the current scene.
        FallbackRunner.Run(sceneName);
    }

    private class FallbackRunner : MonoBehaviour
    {
        private static FallbackRunner s_instance;
        private static bool s_running;

        public static void Run(string sceneName)
        {
            if (s_running) return;
            if (s_instance == null)
            {
                var go = new GameObject("[SceneLoader.Fallback]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<FallbackRunner>();
            }
            s_instance.StartCoroutine(s_instance.LoadRoutine(sceneName));
        }

        private IEnumerator LoadRoutine(string sceneName)
        {
            s_running = true;

            var (canvas, fader) = BuildOverlay();

            // Fade to black over ~0.4s (unscaled — timeScale can be 0).
            const float fadeDur = 0.4f;
            for (float t = 0f; t < fadeDur; t += Time.unscaledDeltaTime)
            {
                fader.color = new Color(0f, 0f, 0f, Mathf.Clamp01(t / fadeDur));
                yield return null;
            }
            fader.color = Color.black;

            // Flush the save AFTER the screen is fully black. SaveLifecycle does a
            // synchronous SaveSystem.Save + PlayerPrefs.Save (a disk hitch); doing
            // it before the fade froze the first frame of every transition. Hidden
            // behind the opaque overlay now, plus one frame to let it present.
            yield return null;
            SaveLifecycle();

            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
            if (op == null)
            {
                Debug.LogError($"[SceneLoader] LoadSceneAsync returned null for '{sceneName}'. Is it in Build Settings?");
                Destroy(canvas);
                s_running = false;
                yield break;
            }
            op.allowSceneActivation = false;

            while (op.progress < 0.9f) yield return null;
            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;

            // Give the new scene one frame to settle its cameras / UI,
            // then fade back in.
            yield return null;
            for (float t = 0f; t < fadeDur; t += Time.unscaledDeltaTime)
            {
                fader.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(t / fadeDur));
                yield return null;
            }

            Destroy(canvas);
            s_running = false;
        }

        private static (GameObject, Image) BuildOverlay()
        {
            var go = new GameObject("[SceneLoader.Overlay]");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;
            go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            var img = new GameObject("Fader", typeof(Image));
            img.transform.SetParent(go.transform, false);
            var rt = img.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var image = img.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
            return (go, image);
        }

        private static void SaveLifecycle()
        {
            // Flush before every hop so a mid-load crash doesn't erase
            // the last minute of play.
            try { SaveSystem.Save(); } catch (System.Exception e) { Debug.LogWarning($"[SceneLoader] SaveSystem flush failed: {e.Message}"); }
            try { PlayerPrefs.Save(); } catch (System.Exception e) { Debug.LogWarning($"[SceneLoader] PlayerPrefs flush failed: {e.Message}"); }
        }
    }
}
