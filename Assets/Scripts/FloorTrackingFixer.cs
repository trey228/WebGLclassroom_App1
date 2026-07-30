using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Attach to the WebXRCameraSet root.
/// Requests Floor tracking origin mode when an XR session starts.
///
/// v4: AppliedOffset is now ZERO in both branches.
/// VRLocomotion anchors the rig to a raycast floor hit every frame
/// (floorY + fixerOffset.y), and WebXR Export's local-floor reference
/// space already supplies real head height. The old +1.4m fallback boost
/// stacked on top of both -> camera at ~3.1m ("spawning at the ceiling").
/// v3's floorModeDrop was also wrong (would sink the rig below the floor).
/// Both removed. The logging is kept so we can see which branch fires.
/// </summary>
public class FloorTrackingFixer : MonoBehaviour
{
    [Tooltip("Manual rig boost. Leave at 0. Only raise this if the browser is using a 'local' (not 'local-floor') reference space, which WebXR Export does not do.")]
    [SerializeField] private float fallbackHeight = 0f;

    /// <summary>Extra rig offset currently in effect. VRLocomotion adds this to floorY.</summary>
    public Vector3 AppliedOffset { get; private set; } = Vector3.zero;

    private Vector3 basePosition;
    private bool applied;
    private readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();

    private void Start()
    {
        basePosition = transform.position;
    }

    private void Update()
    {
        SubsystemManager.GetSubsystems(subsystems);

        bool anyRunning = false;
        foreach (var subsystem in subsystems)
        {
            if (subsystem == null || !subsystem.running)
            {
                continue;
            }
            anyRunning = true;

            if (applied)
            {
                break;
            }

            var supported = subsystem.GetSupportedTrackingOriginModes();
            if ((supported & TrackingOriginModeFlags.Floor) != 0
                && subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
            {
                Debug.Log("[FloorFix] FLOOR mode granted - headset supplies eye height.");
            }
            else
            {
                Debug.Log("[FloorFix] Floor mode NOT granted (modes: " + supported
                    + ") - relying on the browser's local-floor space anyway.");
            }

            // Either way: no manual offset. VRLocomotion puts the rig base
            // on the raycast floor; the reference space adds head height.
            AppliedOffset = Vector3.up * fallbackHeight;
            transform.position = basePosition + AppliedOffset;
            applied = true;
        }

        if (!anyRunning && applied)
        {
            AppliedOffset = Vector3.zero;
            transform.position = basePosition;
            applied = false;
        }
    }
}