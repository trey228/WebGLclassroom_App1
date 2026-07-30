#if NORMCORE

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Normal.Realtime;

/// <summary>
/// Attach to the WebXRCameraSet root, alongside FloorTrackingFixer.
///
/// v2 (milestone 4): owns FLAT mode only. While an XR session is running,
/// this script does NOTHING - VRLocomotion (same GameObject) owns both the
/// capsule and the rig in VR. Also exposes the local player references it
/// finds, so VRLocomotion doesn't duplicate the search.
///
/// v3 (milestone 6 lean fix): the capsule root is now YAW-ONLY (see
/// ClassroomController v2), so mouse pitch can no longer be read off the
/// capsule transform. It is read from ClassroomController.LookPitch instead
/// and applied to the rig camera here.
///
/// Flat mode, every LateUpdate:
///   - rig root = local capsule's FEET, rig yaw = capsule yaw
///   - rig camera local pose = (0, flatEyeHeight, 0) + controller LookPitch
/// </summary>
public class WebXRRigFollower : MonoBehaviour
{
    [Tooltip("Desktop (non-VR) eye height above the capsule's feet, in meters.")]
    [SerializeField] private float flatEyeHeight = 1.6f;

    [Tooltip("Rig camera transform. Auto-found in children if left empty.")]
    [SerializeField] private Transform rigCamera;

    // --- Shared with VRLocomotion ---
    public Transform LocalPlayer { get; private set; }
    public CharacterController LocalCC { get; private set; }
    public MonoBehaviour LocalMovementController { get; private set; }
    public float FeetDrop { get; private set; }
    public Transform RigCamera { get { return rigCamera; } }
    public Vector3 FixerOffset
    {
        get { return floorFixer != null ? floorFixer.AppliedOffset : Vector3.zero; }
    }

    private FloorTrackingFixer floorFixer;
    private ClassroomController localLook;   // typed ref for LookPitch
    private readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();

    private void Awake()
    {
        floorFixer = GetComponent<FloorTrackingFixer>();
        if (rigCamera == null)
        {
            Camera cam = GetComponentInChildren<Camera>(true);
            if (cam != null) rigCamera = cam.transform;
        }
    }

    private void LateUpdate()
    {
        if (LocalPlayer == null)
        {
            FindLocalPlayer();
            if (LocalPlayer == null) return;
        }

        // VR: VRLocomotion owns everything. Do not touch the rig.
        if (IsInXR()) return;

        // FLAT mode: rig root at the capsule's feet, matching yaw.
        Vector3 feet = LocalPlayer.position + Vector3.down * FeetDrop;
        float yaw = LocalPlayer.eulerAngles.y;
        transform.SetPositionAndRotation(feet + FixerOffset, Quaternion.Euler(0f, yaw, 0f));

        // Camera local height + mouse pitch from the controller (the capsule
        // root is yaw-only now, so we can't read pitch off the transform).
        if (rigCamera != null)
        {
            float pitch = localLook != null ? localLook.LookPitch : 0f;
            rigCamera.localPosition = new Vector3(0f, flatEyeHeight, 0f);
            rigCamera.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    private void FindLocalPlayer()
    {
        ClassroomPlayer[] players =
            FindObjectsByType<ClassroomPlayer>(FindObjectsSortMode.None);
        foreach (ClassroomPlayer player in players)
        {
            RealtimeView view = player.GetComponent<RealtimeView>();
            if (view == null || !view.isOwnedLocallySelf) continue;

            LocalPlayer = player.transform;
            LocalCC = player.GetComponent<CharacterController>();
            LocalMovementController = player.movementController;
            localLook = player.movementController as ClassroomController;
            FeetDrop = LocalCC != null
                ? (LocalCC.height * 0.5f) - LocalCC.center.y
                : 1f;
            Debug.Log("[RigFollower] Attached to local player '" + player.name
                + "', feetDrop=" + FeetDrop
                + ", lookPitch=" + (localLook != null ? "wired" : "NOT FOUND"));
            return;
        }
    }

    public bool IsInXR()
    {
        SubsystemManager.GetSubsystems(subsystems);
        foreach (XRInputSubsystem subsystem in subsystems)
        {
            if (subsystem != null && subsystem.running) return true;
        }
        return false;
    }
}

#endif
