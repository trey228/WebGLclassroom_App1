#if NORMCORE

using UnityEngine;
using WebXR;

/// <summary>
/// v3 (AGORA) — Attach to the WebXRCameraSet root, alongside AgoraVoice.
/// Mute/unmute control for AGORA voice chat. Normcore handles multiplayer
/// only; Agora carries audio (see AgoraVoice.cs + AgoraVoiceBridge.jslib).
///
/// CONTROLS:
///   VR:      tap Y on the LEFT controller (ButtonB) — XR-session-gated.
///   Desktop: tap V.
///
/// JOIN TIMING: waits for Normcore to spawn the local player (via
/// WebXRRigFollower.LocalPlayer) before joining the Agora channel, so both
/// systems come up together and the mic prompt appears after the user has
/// already interacted with the page.
///
/// INDICATOR: while muted, a marker pinned bottom-center of view (parented
/// to the rig camera, flat AND VR, local-only). Placeholder red square;
/// auto-upgrades to Assets/Resources/UI/MuteIcon.png if present.
/// </summary>
[RequireComponent(typeof(WebXRRigFollower))]
[RequireComponent(typeof(AgoraVoice))]
public class VoiceControls : MonoBehaviour
{
    [Tooltip("Desktop mute toggle key.")]
    [SerializeField] private KeyCode desktopMuteKey = KeyCode.V;

    [Tooltip("Start sessions muted? (Privacy-first classrooms may want true.)")]
    [SerializeField] private bool startMuted = false;

    [Tooltip("Indicator distance in front of the eyes, meters.")]
    [SerializeField] private float indicatorDistance = 0.8f;

    [Tooltip("Indicator size in meters at that distance.")]
    [SerializeField] private float indicatorSize = 0.09f;

    private WebXRRigFollower follower;
    private VRLocomotion locomotion;
    private AgoraVoice agora;
    private GameObject muteIndicator;
    private bool muted;
    private bool muteApplied;

    private void Awake()
    {
        follower = GetComponent<WebXRRigFollower>();
        locomotion = GetComponent<VRLocomotion>();
        agora = GetComponent<AgoraVoice>();
        muted = startMuted;
    }

    private void LateUpdate()
    {
        // Join Agora once Normcore has us in the room (local player exists).
        if (follower.LocalPlayer != null)
        {
            agora.Join(); // idempotent
            if (!muteApplied)
            {
                agora.SetMute(muted);
                muteApplied = true;
            }
        }

        // Toggle: desktop V key, or left controller Y (ButtonB) in VR.
        // Controller read is gated on an ACTIVE XR session — without one,
        // WebXRController button state flaps (the v714 lesson).
        bool togglePressed = Input.GetKeyDown(desktopMuteKey);
        if (follower.IsInXR())
        {
            WebXRController left = locomotion != null ? locomotion.LeftController : null;
            if (left != null && left.isActiveAndEnabled
                && left.GetButtonDown(WebXRController.ButtonTypes.ButtonB))
            {
                togglePressed = true;
            }
        }

        if (togglePressed)
        {
            muted = !muted;
            agora.SetMute(muted);
            Debug.Log("[Voice] Muted: " + muted);
        }

        if (muteIndicator == null) BuildIndicator();
        if (muteIndicator != null && muteIndicator.activeSelf != muted)
            muteIndicator.SetActive(muted);
    }

    private void BuildIndicator()
    {
        Transform cam = follower.RigCamera;
        if (cam == null) return;

        muteIndicator = GameObject.CreatePrimitive(PrimitiveType.Quad);
        muteIndicator.name = "MuteIndicator (local only)";
        Destroy(muteIndicator.GetComponent<Collider>());
        muteIndicator.transform.SetParent(cam, false);
        muteIndicator.transform.localPosition =
            new Vector3(0f, -0.28f, indicatorDistance);
        muteIndicator.transform.localRotation = Quaternion.identity;
        muteIndicator.transform.localScale =
            new Vector3(indicatorSize, indicatorSize, 1f);

        Renderer r = muteIndicator.GetComponent<Renderer>();
        Material template = Resources.Load<Material>("UI/MenuPanelMat");
        Material m = template != null
            ? new Material(template)
            : new Material(r.sharedMaterial);
        m.mainTexture = null;

        Texture2D icon = Resources.Load<Texture2D>("UI/MuteIcon");
        if (icon != null)
        {
            m.mainTexture = icon;
            m.color = Color.white;
        }
        else
        {
            m.color = new Color(0.85f, 0.10f, 0.10f, 0.85f); // placeholder red
        }

        r.material = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        muteIndicator.SetActive(false);
    }
}

#endif
