using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Place this on the Main Camera (alongside ClassroomController).
/// On left-click, it casts a ray from the screen point under the cursor.
/// If it hits a collider that has an Interactable component, it triggers it.
///
/// Works in WebGL on PC/Mac/mobile. Uses legacy Input (project is set to
/// "Both"). For VR/WebXR you'd swap the ray origin to the controller — noted
/// in comments below.
/// </summary>
public class InteractionRaycaster : MonoBehaviour
{
    [Tooltip("Max distance the click-ray will travel, in meters.")]
    public float maxDistance = 20f;

    [Tooltip("Only objects on these layers can be clicked. Default = everything.")]
    public LayerMask interactableMask = ~0;

    [Tooltip("If true, requires the pointer to be locked (in look mode) to interact. " +
             "Set false if you want clicking to work even before mouse capture.")]
    public bool requirePointerLock = false;

    Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        if (requirePointerLock && Cursor.lockState != CursorLockMode.Locked)
            return;

        // When the cursor is locked (FPS look mode) it sits at screen center,
        // so we cast from the center. When unlocked, cast from the mouse pos.
        Vector3 screenPoint = (Cursor.lockState == CursorLockMode.Locked)
            ? new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f)
            : Input.mousePosition;

        Ray ray = cam.ScreenPointToRay(screenPoint);

        // FOR VR/WebXR LATER: replace the two lines above with a ray built from
        // the active XR controller's position/forward, e.g.:
        // Ray ray = new Ray(controllerTransform.position, controllerTransform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, interactableMask))
        {
            var interactable = hit.collider.GetComponentInParent<Interactable>();
            if (interactable != null)
            {
                interactable.Interact(hit);
            }
        }
    }
}
