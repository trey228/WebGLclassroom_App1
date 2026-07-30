using UnityEngine;
using System.Runtime.InteropServices;

/// <summary>
/// MILESTONE 5 (AGORA) — Attach to the WebXRCameraSet root.
/// C# face of the Agora Web SDK voice bridge (AgoraVoiceBridge.jslib).
/// Voice-only, WebGL-only: in the Editor all calls are logged no-ops, so
/// Play mode works normally. Multiplayer remains 100% Normcore — Agora
/// carries audio and nothing else.
///
/// SETUP: paste your Agora App ID into the Inspector field. Channel name
/// is fixed per scene (default "ClassroomTest"). VoiceControls calls
/// Join() once Normcore has spawned the local player, and drives SetMute.
///
/// States: 0 idle · 1 joining · 2 LIVE (mic publishing) · 3 listen-only
/// (mic denied — user still hears everyone) · 4 error.
/// </summary>
public class AgoraVoice : MonoBehaviour
{
    [Tooltip("Agora App ID from console.agora.io (project in testing mode).")]
    [SerializeField] private string appId = "";

    [Tooltip("Voice channel name. Everyone in the same channel hears each other.")]
    [SerializeField] private string channelName = "ClassroomTest";

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void AgoraVoice_Init(string appId);
    [DllImport("__Internal")] private static extern void AgoraVoice_Join(string channel);
    [DllImport("__Internal")] private static extern void AgoraVoice_SetMute(bool muted);
    [DllImport("__Internal")] private static extern int  AgoraVoice_GetState();
    [DllImport("__Internal")] private static extern void AgoraVoice_Leave();
#else
    private static void AgoraVoice_Init(string appId) { Debug.Log("[Agora] (editor stub) Init"); }
    private static void AgoraVoice_Join(string channel) { Debug.Log("[Agora] (editor stub) Join " + channel); }
    private static void AgoraVoice_SetMute(bool muted) { Debug.Log("[Agora] (editor stub) SetMute " + muted); }
    private static int  AgoraVoice_GetState() { return 0; }
    private static void AgoraVoice_Leave() { }
#endif

    private int lastState = -1;
    private bool initialized;
    private bool joinRequested;

    /// <summary>2 = mic live, 3 = joined listen-only.</summary>
    public bool IsJoined { get { int s = AgoraVoice_GetState(); return s == 2 || s == 3; } }
    public bool IsListenOnly { get { return AgoraVoice_GetState() == 3; } }

    private void Start()
    {
        if (string.IsNullOrEmpty(appId))
        {
            Debug.LogError("[Agora] App ID is EMPTY - paste it into the AgoraVoice component (from console.agora.io).");
            return;
        }
        AgoraVoice_Init(appId);
        initialized = true;
    }

    /// <summary>Idempotent — safe to call every frame until joined.</summary>
    public void Join()
    {
        if (!initialized || joinRequested) return;
        joinRequested = true;
        AgoraVoice_Join(channelName);
    }

    public void SetMute(bool muted)
    {
        AgoraVoice_SetMute(muted);
    }

    private void Update()
    {
        int s = AgoraVoice_GetState();
        if (s != lastState)
        {
            lastState = s;
            switch (s)
            {
                case 1: Debug.Log("[Agora] Joining voice channel..."); break;
                case 2: Debug.Log("[Agora] VOICE LIVE - mic publishing"); break;
                case 3: Debug.LogWarning("[Agora] Listen-only (mic denied or unavailable)"); break;
                case 4: Debug.LogError("[Agora] Voice error - check SDK script tag / App ID / console"); break;
            }
        }
    }

    private void OnDestroy()
    {
        if (initialized) AgoraVoice_Leave();
    }
}
