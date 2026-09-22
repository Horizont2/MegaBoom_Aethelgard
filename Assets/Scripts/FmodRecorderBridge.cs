using System;
using System.Runtime.InteropServices;
using System.Threading;
using AOT;
using UnityEngine;

// Puts FMOD's master mix into Unity's audio output, so Unity Recorder can hear it.
//
// ==== WHY THE RECORDER RECORDS SILENCE ====
//
// Unity Recorder's Audio input records the output of UNITY's audio engine — the
// AudioListener bus. FMOD never goes near it: RuntimeManager opens its own
// output device and mixes straight to the speakers. So the Recorder faithfully
// captures the Unity bus, which in this project carries nothing, and writes a
// movie with no sound. Nothing is misconfigured; the two engines simply never
// meet.
//
// The WAVWRITER tool next door works around that by making FMOD write its mix
// to a file instead, and it does work — but it hands you two files to line up by
// hand, which is not what "record the trailer" should mean.
//
// This makes the two engines meet. A custom DSP is hung at the head of FMOD's
// master channel group, which is the last point before the sound would leave for
// the device. It copies every block into a ring buffer and then writes silence
// where the block was, so FMOD stops driving the speakers. A filter on the
// AudioListener drains that ring into Unity's own output every audio frame. From
// there everything downstream — the speakers, and Unity Recorder — is hearing
// FMOD.
//
// ==== THE PARTS THAT ARE NOT NEGOTIABLE ====
//
// The read callback runs on FMOD's MIXER THREAD. It may not allocate, may not
// touch a single Unity API, and may not take a lock — any of those stall the
// mixer, and a stalled mixer is a crackle at best. So it does nothing but a
// Marshal.Copy into arrays that were allocated once, and everything it shares
// with the audio thread is a single writer and a single reader with volatile
// counters between them.
//
// The delegate is held in a static field. A delegate handed to native code and
// then collected is a crash, and it is the classic way this kind of bridge
// fails — days later, in someone else's session.
//
// ==== WHAT IT COSTS ====
//
// One buffer of cushion between the two engines, which is how the recorded sound
// ends up slightly behind the recorded picture. CushionFrames is that number; at
// the default it is about forty milliseconds, which is under two frames at 24fps
// and invisible in a trailer. Raise it if you hear crackle, lower it if you can
// see the lip-sync.
public static class FmodRecorderBridge
{
    public const string SessionKey = "Trailer.FmodBridge";

    // FMOD 2.0x's plugin ABI. It is checked by createDSP and a mismatch is
    // refused outright rather than silently misread, which is why this is a
    // constant and not a guess: if a future FMOD moves to a new ABI the call
    // fails loudly and the log below says exactly what to change.
    private const uint PluginSdkVersion = 110;

    // Index 0 in a channel group's DSP chain is the HEAD — the end closest to
    // the output, after the group's own fader. Anything added there sees the
    // finished mix, which is the only thing worth recording.
    private const int HeadOfChain = 0;

    // Power of two, so the wrap is a mask rather than a modulo in a callback
    // that must not do division. 16384 frames is a third of a second at 48 kHz,
    // far more than the cushion needs, and it costs 128 KB.
    private const int RingFrames = 1 << 14;
    private const int RingMask = RingFrames - 1;

    // How far the reader deliberately stays behind the writer. This IS the
    // latency of the bridge, and the number the recorded audio sits behind the
    // recorded picture by.
    public static int CushionFrames = 2048;

    private static readonly float[] s_ring = new float[RingFrames * 2];

    // Written by the mixer thread, read by the audio thread.
    private static long s_write;
    // Touched only by the audio thread. Fractional, because FMOD and Unity do
    // not have to agree on a sample rate.
    private static double s_read;
    private static double s_step = 1.0;

    // Allocated once, used only on the mixer thread. FMOD's block is normally
    // 512 or 1024 frames; this is room for eight times that at eight channels.
    private static float[] s_scratch;
    private static float[] s_silence;

    private static FMOD.DSP_READ_CALLBACK s_readCallback;   // must outlive native
    private static FMOD.DSP s_dsp;
    private static FMOD.ChannelGroup s_master;
    private static bool s_installed;

    // Set once the tap has been refused for a reason that will not change this
    // session. Without it Keeper retries every frame and the console fills with
    // the same line sixty times a second, which is how a clear message about a
    // plugin ABI becomes noise nobody reads.
    private static bool s_gaveUp;

    /// <summary>True once the tap is actually running on FMOD's master bus.</summary>
    public static bool Running { get { return s_installed; } }

    private static bool Wanted()
    {
#if UNITY_EDITOR
        return UnityEditor.SessionState.GetBool(SessionKey, false);
#else
        return false;
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        // Statics do not necessarily start clean. With Enter Play Mode Options
        // set to skip the domain reload — which anyone iterating on a shot turns
        // on — everything above survives from the last run while the FMOD system
        // it refers to does not. So the state is put back by hand on every play
        // rather than trusted to the editor.
        s_installed = false;
        s_gaveUp = false;
        s_write = 0;
        s_read = 0;

        if (!Wanted()) return;

        var go = new GameObject("FmodRecorderBridge");
        go.hideFlags = HideFlags.DontSave;
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<Keeper>();
    }

    // ── installing the tap ───────────────────────────────────────────────

    private static void Install()
    {
        if (s_installed || s_gaveUp) return;

        FMOD.System core;
        try
        {
            if (!FMODUnity.RuntimeManager.IsInitialized) return;   // not up yet; try again next frame
            core = FMODUnity.RuntimeManager.CoreSystem;
        }
        catch (Exception e)
        {
            s_gaveUp = true;
            Debug.LogWarning("[FmodBridge] FMOD is not available: " + e.Message);
            return;
        }

        int fmodRate;
        FMOD.SPEAKERMODE mode;
        int raw;
        if (core.getSoftwareFormat(out fmodRate, out mode, out raw) != FMOD.RESULT.OK || fmodRate <= 0)
            fmodRate = 48000;

        int unityRate = AudioSettings.outputSampleRate;
        if (unityRate <= 0) unityRate = 48000;
        s_step = (double)fmodRate / unityRate;

        s_scratch = new float[8192 * 8];
        s_silence = new float[8192 * 8];
        s_write = 0;
        s_read = 0;

        // Held in a static so the garbage collector cannot take it out from
        // under native code. This is not belt and braces; it is the bug.
        s_readCallback = Read;

        var desc = new FMOD.DSP_DESCRIPTION
        {
            pluginsdkversion = PluginSdkVersion,
            name = Name32("HollowSiegeTap"),
            version = 1,
            numinputbuffers = 1,
            numoutputbuffers = 1,
            read = s_readCallback,
        };

        FMOD.RESULT res = core.createDSP(ref desc, out s_dsp);
        if (res != FMOD.RESULT.OK)
        {
            Debug.LogWarning("[FmodBridge] createDSP failed: " + res + ". If this says ERR_PLUGIN_VERSION, FMOD has " +
                             "moved to a new plugin ABI and PluginSdkVersion in FmodRecorderBridge needs the new " +
                             "number from fmod_dsp.h. Use Tools > Trailer > Audio for Recorder > To a WAV file " +
                             "until then.");
            s_gaveUp = true;
            return;
        }

        if (core.getMasterChannelGroup(out s_master) != FMOD.RESULT.OK)
        {
            s_dsp.release();
            Debug.LogWarning("[FmodBridge] Could not reach FMOD's master channel group.");
            s_gaveUp = true;
            return;
        }

        res = s_master.addDSP(HeadOfChain, s_dsp);
        if (res != FMOD.RESULT.OK)
        {
            s_dsp.release();
            Debug.LogWarning("[FmodBridge] Could not attach the tap to the master bus: " + res);
            s_gaveUp = true;
            return;
        }

        s_installed = true;
        Debug.Log($"[FmodBridge] FMOD's mix is now going through Unity's audio output, so Unity Recorder records it. " +
                  $"FMOD {fmodRate} Hz into Unity {unityRate} Hz. The sound lands about " +
                  $"{CushionFrames * 1000f / fmodRate:0} ms behind the picture — nudge the audio earlier by that much " +
                  $"in your editor if you can see it, or change CushionFrames.");
    }

    private static void Uninstall()
    {
        if (!s_installed) return;
        s_installed = false;
        s_gaveUp = false;

        try
        {
            s_master.removeDSP(s_dsp);
            s_dsp.release();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[FmodBridge] Could not detach cleanly: " + e.Message);
        }
    }

    private static byte[] Name32(string s)
    {
        var bytes = new byte[32];
        byte[] raw = System.Text.Encoding.ASCII.GetBytes(s);
        Array.Copy(raw, bytes, Mathf.Min(raw.Length, 31));
        return bytes;
    }

    // ── FMOD's mixer thread ──────────────────────────────────────────────

    [MonoPInvokeCallback(typeof(FMOD.DSP_READ_CALLBACK))]
    private static FMOD.RESULT Read(ref FMOD.DSP_STATE state, IntPtr inbuffer, IntPtr outbuffer,
                                    uint length, int inchannels, ref int outchannels)
    {
        int frames = (int)length;
        int inTotal = frames * inchannels;

        float[] scratch = s_scratch;
        float[] silence = s_silence;
        if (scratch == null || silence == null || inTotal <= 0 || inTotal > scratch.Length)
            return FMOD.RESULT.OK;          // never allocate here, and never write past an array

        Marshal.Copy(inbuffer, scratch, 0, inTotal);

        long w = s_write;
        for (int f = 0; f < frames; f++)
        {
            int b = f * inchannels;
            float l = scratch[b];
            float r = inchannels > 1 ? scratch[b + 1] : l;

            int i = (int)((w + f) & RingMask) * 2;
            s_ring[i] = l;
            s_ring[i + 1] = r;
        }
        // Published last, so the reader never sees a frame count that is ahead
        // of the samples it counts.
        Volatile.Write(ref s_write, w + frames);

        // And FMOD stops driving the speakers. Without this the mix is heard
        // twice, a few milliseconds apart, which is a comb filter and sounds
        // like the game is underwater.
        int outTotal = frames * outchannels;
        if (outTotal > 0 && outTotal <= silence.Length)
            Marshal.Copy(silence, 0, outbuffer, outTotal);

        return FMOD.RESULT.OK;
    }

    // ── Unity's audio thread ─────────────────────────────────────────────

    private static void Drain(float[] data, int channels)
    {
        if (!s_installed || channels <= 0) return;

        long w = Volatile.Read(ref s_write);
        double r = s_read;
        double available = w - r;

        // Starting up, or the mixer fell behind: sit back down at the cushion
        // and let this buffer through silent. Stretching a starved ring is a
        // much louder fault than one quiet buffer.
        if (available < 64 || available > RingFrames - 256)
        {
            s_read = w - CushionFrames;
            return;
        }

        // The two engines run off clocks that are usually but not always the
        // same, so the gap between them drifts. Corrected by reading a fraction
        // of a percent faster or slower rather than by jumping, which would be
        // an audible click every time it happened.
        double target = CushionFrames;
        double error = Mathf.Clamp((float)((available - target) / target), -1f, 1f);
        double step = s_step * (1.0 + error * 0.002);

        int frames = data.Length / channels;
        double limit = w - 2;

        for (int f = 0; f < frames; f++)
        {
            if (r > limit) r = limit;

            long i0 = (long)r;
            float t = (float)(r - i0);
            int a = (int)(i0 & RingMask) * 2;
            int b = (int)((i0 + 1) & RingMask) * 2;

            float l = s_ring[a] + (s_ring[b] - s_ring[a]) * t;
            float rr = s_ring[a + 1] + (s_ring[b + 1] - s_ring[a + 1]) * t;

            int o = f * channels;
            if (channels >= 2)
            {
                // Added, not assigned: anything Unity's own audio is playing
                // stays in the mix alongside it.
                data[o] += l;
                data[o + 1] += rr;
                float mid = (l + rr) * 0.5f;
                for (int c = 2; c < channels; c++) data[o + c] += mid;
            }
            else data[o] += (l + rr) * 0.5f;

            r += step;
        }

        s_read = r;
    }

    // ── the two components ───────────────────────────────────────────────

    // Installs the tap once FMOD is up, and keeps the listener filter attached
    // to whichever AudioListener is currently the live one. The trailer shots
    // hand the listener from camera to camera, and a filter left behind on a
    // switched-off listener is never called again — silence, with nothing in
    // the log to say why.
    private sealed class Keeper : MonoBehaviour
    {
        private Tap tap;
        private float next;

        // ==== UNITY TURNS ITS OUTPUT OFF WHEN NOTHING IS PLAYING ====
        //
        // Enable Output Suspension is on in this project's audio settings, and
        // in a project where every sound comes from FMOD, nothing is EVER
        // playing on the Unity side. So the output sleeps, the listener's filter
        // is never called, and the bridge would be handing its samples to a bus
        // that is not running — silence, with everything apparently set up
        // correctly, which is the worst kind of fault to be handed.
        //
        // A loop of pure silence keeps it awake. As far as the voice manager is
        // concerned it is an audible 2D source at full volume, so it is never
        // virtualised; as far as the mix is concerned it is zeroes.
        private void Awake()
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            var clip = AudioClip.Create("BridgeKeepAlive", 4096, 1, rate, false);
            clip.SetData(new float[4096], 0);

            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.priority = 0;
            src.Play();
        }

        private void Update()
        {
            if (!s_installed) Install();
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;

            if (tap != null && tap.isActiveAndEnabled) return;

            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (l == null || !l.enabled || !l.gameObject.activeInHierarchy) continue;
                tap = l.gameObject.GetComponent<Tap>();
                if (tap == null) tap = l.gameObject.AddComponent<Tap>();
                return;
            }
        }

        private void OnApplicationQuit() { Uninstall(); }
        private void OnDestroy() { Uninstall(); }
    }

    // Unity only calls OnAudioFilterRead on a GameObject that carries an
    // AudioListener or an AudioSource, which is why this rides on the listener
    // rather than on an object of its own.
    private sealed class Tap : MonoBehaviour
    {
        private void OnAudioFilterRead(float[] data, int channels) { Drain(data, channels); }
    }
}
