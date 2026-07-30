#if NORMCORE

using UnityEngine;
using Normal.Realtime;

/// <summary>
/// Lives on the networked Player prefab. Modeled on Normcore's CubePlayer
/// example, but for a walking first-person player.
///
/// MILESTONE 3 CHANGE: the WebXRCameraSet is now the ONE viewpoint for the
/// local player (flat and VR). The capsule's child camera and AudioListener
/// are therefore kept DISABLED for local AND remote players. WebXRRigFollower
/// (on the WebXRCameraSet) parents the view to this capsule at runtime.
///
/// On the LOCAL player (the one this client owns):
///   - enables the CharacterController and ClassroomController so WASD +
///     mouse-look drive the capsule (the rig follows it)
///   - hides this player's own body mesh so you don't see your own capsule
///   - requests ownership of the RealtimeTransform so your movement syncs
///
/// On REMOTE players (other people):
///   - disables all control components (you don't drive them)
///   - leaves the body mesh visible so you SEE them
///   - RealtimeTransform keeps their position/rotation synced automatically
/// </summary>
public class ClassroomPlayer : MonoBehaviour {
    [Header("Assign these on the prefab")]
    [Tooltip("The child Camera object. Kept disabled since milestone 3 (WebXR rig is the viewpoint); retained for possible later use.")]
    public Camera playerCamera;

    [Tooltip("The visible body mesh others see (e.g. the capsule's MeshRenderer).")]
    public Renderer bodyRenderer;

    [Tooltip("The walking controller (your existing ClassroomController).")]
    public MonoBehaviour movementController;   // ClassroomController

    [Tooltip("The CharacterController used for collision.")]
    public CharacterController characterController;

    RealtimeView      _realtimeView;
    RealtimeTransform _realtimeTransform;
    AudioListener     _audioListener;

    void Awake() {
        _realtimeView      = GetComponent<RealtimeView>();
        _realtimeTransform = GetComponent<RealtimeTransform>();
        if (playerCamera != null)
            _audioListener = playerCamera.GetComponent<AudioListener>();
    }

    void Start() {
        bool isLocal = _realtimeView.isOwnedLocallySelf;

        // The capsule's own camera/listener are NEVER used anymore.
        // The WebXRCameraSet's Main Camera + AudioListener are the only
        // active ones; WebXRRigFollower moves that rig to this capsule.
        if (playerCamera != null)   playerCamera.enabled = false;
        if (_audioListener != null) _audioListener.enabled = false;

        if (isLocal) {
            // This is OUR player — enable control.
            if (characterController != null) characterController.enabled = true;
            if (movementController != null)  movementController.enabled = true;

            // Hide our own body so we don't see the capsule from inside.
            if (bodyRenderer != null) bodyRenderer.enabled = false;

            // Take ownership of the transform so our movement is what others see.
            _realtimeTransform.RequestOwnership();
        } else {
            // This is someone ELSE's player — we only display it, never drive it.
            if (characterController != null) characterController.enabled = false;
            if (movementController != null)  movementController.enabled = false;

            // Keep their body visible so we can see them.
            if (bodyRenderer != null) bodyRenderer.enabled = true;
        }
    }
}

#endif
