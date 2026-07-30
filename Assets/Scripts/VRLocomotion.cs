#if NORMCORE

using UnityEngine;
using WebXR;

/// <summary>
/// v3 (TURNING BUILD) — WebXRCameraSet root, with FloorTrackingFixer,
/// WebXRRigFollower, VRTurnMenu. Owns ALL movement while in VR.
///
/// CONTROLS:
///   LEFT stick      = move (head-relative; movement is LEFT-ONLY as of v3).
///   RIGHT stick X   = turn. Mode = Snap (default) or Smooth, set by VRTurnMenu.
///   RIGHT A / B     = tap: eye height up / down (RIGHT hand only as of v3;
///                     the left controller's X/Y are reserved for the menu).
///   LEFT X (ButtonA on left controller) = handled by VRTurnMenu.
///
/// Snap: 45° per flick past 0.7, re-arms when |x| drops below 0.3.
/// Smooth: turnSpeed deg/sec at full deflection.
/// Both rotate the rig around the HEAD (pivot in place).
/// Height: raycast floor + user offset (unchanged from height build).
/// </summary>
[RequireComponent(typeof(WebXRRigFollower))]
public class VRLocomotion : MonoBehaviour
{
    public enum TurnMode { Snap, Smooth }

    [Header("Movement (left stick)")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float deadzone = 0.15f;
    [SerializeField] private float gravity = 20f;

    [Header("Turning (right stick X)")]
    [Tooltip("Snap = discrete comfort turns (default). Smooth = continuous. Users switch via the in-VR menu.")]
    [SerializeField] private TurnMode turnMode = TurnMode.Snap;
    [SerializeField] private float snapAngle = 45f;
    [Tooltip("Stick X beyond this triggers a snap turn.")]
    [SerializeField] private float snapThreshold = 0.7f;
    [Tooltip("Stick X must return below this before another snap can fire.")]
    [SerializeField] private float snapRearmThreshold = 0.3f;
    [Tooltip("Smooth-turn speed, degrees per second at full deflection.")]
    [SerializeField] private float turnSpeed = 120f;

    [Header("Height adjust (RIGHT controller A/B, tap)")]
    [SerializeField] private float heightStepPerTap = 0.05f;
    [SerializeField] private float heightAdjustLimit = 1.5f;
    [SerializeField] private float userHeightOffset = 0f;

    /// <summary>Current turn mode; VRTurnMenu reads and sets this.</summary>
    public TurnMode CurrentTurnMode
    {
        get { return turnMode; }
        set { turnMode = value; Debug.Log("[VRLoco] Turn mode set to " + value); }
    }

    public WebXRController LeftController  { get; private set; }
    public WebXRController RightController { get; private set; }

    private WebXRRigFollower follower;
    private float verticalVelocity;
    private bool wasInXR;
    private bool snapArmed = true;

    private void Awake()
    {
        follower = GetComponent<WebXRRigFollower>();

        foreach (WebXRController controller in GetComponentsInChildren<WebXRController>(true))
        {
            string n = controller.gameObject.name.ToLowerInvariant();
            if (n.Contains("handl") || n.Contains("left"))       LeftController  = controller;
            else if (n.Contains("handr") || n.Contains("right")) RightController = controller;
        }
        Debug.Log("[VRLoco] Controllers - left: "
            + (LeftController  != null ? LeftController.gameObject.name  : "NOT FOUND")
            + ", right: "
            + (RightController != null ? RightController.gameObject.name : "NOT FOUND"));
    }

    private void LateUpdate()
    {
        bool inXR = follower.IsInXR();
        Transform player = follower.LocalPlayer;

        if (player != null && inXR != wasInXR)
        {
            MonoBehaviour mouseKeyboard = follower.LocalMovementController;
            if (mouseKeyboard != null) mouseKeyboard.enabled = !inXR;
            verticalVelocity = 0f;
            wasInXR = inXR;
            Debug.Log("[VRLoco] Mode switch, inXR=" + inXR);
        }

        if (!inXR || player == null) return;

        CharacterController cc = follower.LocalCC;
        Transform head = follower.RigCamera;
        if (cc == null || head == null) return;

        // --- Turning (right stick X), pivot around the head ---
        float turnInput = ReadAxis(RightController).x;
        if (turnMode == TurnMode.Smooth)
        {
            if (Mathf.Abs(turnInput) > deadzone)
                transform.RotateAround(head.position, Vector3.up,
                    turnInput * turnSpeed * Time.deltaTime);
        }
        else // Snap
        {
            if (!snapArmed)
            {
                if (Mathf.Abs(turnInput) < snapRearmThreshold) snapArmed = true;
            }
            else if (Mathf.Abs(turnInput) > snapThreshold)
            {
                transform.RotateAround(head.position, Vector3.up,
                    snapAngle * Mathf.Sign(turnInput));
                snapArmed = false;
            }
        }

        // --- Height adjust: tap A/B on the RIGHT controller only ---
        if (RightController != null && RightController.isActiveAndEnabled)
        {
            int taps = 0;
            if (RightController.GetButtonDown(WebXRController.ButtonTypes.ButtonA)) taps += 1;
            if (RightController.GetButtonDown(WebXRController.ButtonTypes.ButtonB)) taps -= 1;
            if (taps != 0)
            {
                userHeightOffset = Mathf.Clamp(
                    userHeightOffset + taps * heightStepPerTap,
                    -heightAdjustLimit, heightAdjustLimit);
                Debug.Log("[VRLoco] Height offset now " + userHeightOffset.ToString("F2") + "m");
            }
        }

        // --- Movement (LEFT stick), head-relative, flattened ---
        Vector2 stick = ReadAxis(LeftController);
        if (stick.magnitude < deadzone) stick = Vector2.zero;
        Vector3 forward = head.forward;  forward.y = 0f; forward.Normalize();
        Vector3 right   = head.right;    right.y   = 0f; right.Normalize();
        Vector3 stickMove = forward * stick.y + right * stick.x;
        if (stickMove.sqrMagnitude > 1f) stickMove.Normalize();
        stickMove *= moveSpeed * Time.deltaTime;

        // --- Roomscale ---
        Vector3 roomscale = head.position - player.position;
        roomscale.y = 0f;

        // --- Gravity ---
        if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity -= gravity * Time.deltaTime;
        Vector3 fall = Vector3.up * verticalVelocity * Time.deltaTime;

        Vector3 headOffset = head.position - transform.position;

        cc.Move(roomscale + stickMove + fall);

        // --- Capsule yaw = head yaw ---
        player.rotation = Quaternion.Euler(0f, head.eulerAngles.y, 0f);

        // --- Re-anchor rig: XZ head-over-capsule, Y raycast floor + offset ---
        Vector3 feet = player.position + Vector3.down * follower.FeetDrop;
        float floorY = feet.y;
        if (Physics.Raycast(player.position, Vector3.down, out RaycastHit hit,
                5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            floorY = hit.point.y;
        }

        Vector3 fixerOffset = follower.FixerOffset;
        transform.position = new Vector3(
            feet.x - headOffset.x,
            floorY + fixerOffset.y + userHeightOffset,
            feet.z - headOffset.z);
    }

    private Vector2 ReadAxis(WebXRController controller)
    {
        if (controller == null || !controller.isActiveAndEnabled) return Vector2.zero;
        return controller.GetAxis2D(WebXRController.Axis2DTypes.Thumbstick);
    }
}

#endif
