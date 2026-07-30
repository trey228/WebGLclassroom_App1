using UnityEngine;

/// <summary>
/// HideControllerHands.cs — ClassroomXR milestone 6, Phase B.
/// Attach to the WebXRCameraSet root (next to VRLocomotion).
///
/// Hides the WebXR controller/hand visuals for the LOCAL player. These
/// meshes are rig-local (never networked — other players don't see them),
/// and with RobotAnimator v2 aiming the robot's arms at the controllers,
/// the floating hands are redundant.
///
/// Re-applies whenever an XR session (re)starts, since WebXR can enable
/// hand meshes when tracking begins. Colliders and WebXRController input
/// components are untouched — only Renderers are disabled.
/// </summary>
[RequireComponent(typeof(VRLocomotion))]
public class HideControllerHands : MonoBehaviour
{
    [Tooltip("Leave ON to hide the floating controller hands in VR.")]
    [SerializeField] private bool hideHands = true;

    private VRLocomotion locomotion;
    private WebXRRigFollower follower;
    private bool wasInXR;
    private float nextSweep;

    private void Awake()
    {
        locomotion = GetComponent<VRLocomotion>();
        follower = GetComponent<WebXRRigFollower>();
    }

    private void LateUpdate()
    {
        if (!hideHands || follower == null) return;

        bool inXR = follower.IsInXR();

        // Sweep on XR entry, then periodically for a few frames' safety
        // (WebXR may spawn/enable hand meshes slightly after session start).
        if (inXR && (!wasInXR || Time.time >= nextSweep))
        {
            HideUnder(locomotion.LeftController);
            HideUnder(locomotion.RightController);
            nextSweep = Time.time + 1.0f;   // cheap once-a-second re-sweep
        }
        wasInXR = inXR;
    }

    private static void HideUnder(WebXR.WebXRController controller)
    {
        if (controller == null) return;
        // Deactivate the OBJECTS holding renderers rather than disabling the
        // renderers: WebXR's ControllerInteraction re-enables renderer.enabled
        // when the controller reports active, but it never re-activates
        // GameObjects — so this wins the fight permanently. Renderers only
        // live on children (e.g. 'model'), never on the controller root, so
        // input/tracking components are untouched.
        foreach (Renderer r in controller.GetComponentsInChildren<Renderer>(true))
        {
            if (r.gameObject != controller.gameObject && r.gameObject.activeSelf)
                r.gameObject.SetActive(false);
        }
    }
}
