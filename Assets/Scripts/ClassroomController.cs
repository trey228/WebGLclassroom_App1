using UnityEngine;

/// <summary>
/// First-person walkthrough controller for the classroom.
/// - WASD / arrow keys to move (horizontal only, stays at eye height)
/// - Click the game view to capture the mouse, then move mouse to look around
/// - Esc releases the mouse
/// - Uses a CharacterController for collision (walls, desks, seats, floor)
/// - Uses Unity's legacy Input (no Input System package needed) for max
///   browser compatibility on both PC and Mac.
/// Attach this to the Main Camera. The Main Camera must also have a
/// CharacterController component.
///
/// v2 (Milestone 6 lean fix): the root transform is now YAW-ONLY. Mouse
/// pitch is kept in a private field and exposed via LookPitch; the rig
/// camera (WebXRRigFollower) applies it. This keeps the networked capsule
/// root upright so remote avatars no longer lean when a desktop player
/// looks up or down.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ClassroomController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Walking speed in units (meters) per second.")]
    public float moveSpeed = 4f;

    [Tooltip("Optional faster speed while holding Left Shift.")]
    public float runSpeed = 7f;

    [Tooltip("Downward acceleration so the player stays on the floor.")]
    public float gravity = 20f;

    [Header("Mouse Look")]
    [Tooltip("How fast the view turns with the mouse.")]
    public float mouseSensitivity = 2f;

    [Tooltip("How far up/down you can look, in degrees.")]
    public float maxLookAngle = 85f;

    [Tooltip("Require a click on the game view before the mouse controls the camera.")]
    public bool clickToLook = true;

    CharacterController controller;
    float verticalVelocity = 0f;
    float pitch = 0f;   // up/down rotation (camera)
    float yaw = 0f;     // left/right rotation (body)
    bool looking = false;

    /// <summary>Mouse-look pitch in degrees (-maxLookAngle..+maxLookAngle).
    /// Applied to the rig camera by WebXRRigFollower, NOT to this transform,
    /// so the networked capsule root stays upright.</summary>
    public float LookPitch { get { return pitch; } }

    void OnEnable()
    {
        controller = GetComponent<CharacterController>();

        // Initialize yaw/pitch from the camera's current orientation so we
        // don't snap when the game starts.
        Vector3 e = transform.eulerAngles;
        yaw = e.y;
        pitch = e.x;
        // Convert pitch from 0..360 to -180..180 so clamping behaves.
        if (pitch > 180f) pitch -= 360f;

        if (!clickToLook)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            looking = true;
        }
    }

    void Update()
    {
        HandleCursorCapture();
        HandleLook();
        HandleMovement();
    }

    void HandleCursorCapture()
    {
        if (!clickToLook) return;

        // Click the view to capture the mouse.
        if (Input.GetMouseButtonDown(0) && !looking)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            looking = true;
        }

        // Esc releases the mouse.
        if (Input.GetKeyDown(KeyCode.Escape) && looking)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            looking = false;
        }

        // If the browser/OS forcibly unlocks the cursor, sync our state.
        if (looking && Cursor.lockState != CursorLockMode.Locked)
        {
            looking = false;
            Cursor.visible = true;
        }
    }

    void HandleLook()
    {
        if (clickToLook && !looking) return;

        float mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        yaw += mouseX;
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);

        // Yaw-only on the root: pitch stays local and is applied to the
        // rig camera by WebXRRigFollower. Keeps the networked body upright.
        transform.localEulerAngles = new Vector3(0f, yaw, 0f);
    }

    void HandleMovement()
    {
        // Read WASD / arrow keys.
        float h = Input.GetAxisRaw("Horizontal"); // A/D, Left/Right
        float v = Input.GetAxisRaw("Vertical");   // W/S, Up/Down

        // Move relative to where we're facing, but flatten so we don't fly
        // when looking up/down. (Root is yaw-only now, so the flatten is
        // just a safety net.)
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 move = (forward * v + right * h);
        if (move.sqrMagnitude > 1f) move.Normalize();

        float speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : moveSpeed;
        Vector3 horizontalMove = move * speed;

        // Gravity keeps us grounded and lets us follow floor height.
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f; // small constant to keep grounded
        verticalVelocity -= gravity * Time.deltaTime;

        Vector3 velocity = horizontalMove + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }
}
