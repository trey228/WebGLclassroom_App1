using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using Normal.Realtime;
using Normal.Realtime.Serialization;

// ============================================================
// AvatarSelector.cs — v8 (ClassroomXR milestone 6, Phase B)
// On the Player prefab ROOT next to RealtimeView.
//
// v8 change: FIRST-PERSON ARMS — the local player's ArmL/ArmR stay on
//   the Default layer so you see your own robot arms tracking your
//   controllers in VR. The rest of the local avatar (Body incl. head,
//   ball, legs) stays on LocalMirror (mirror-only). showLegsToSelf
//   optionally shows your legs when looking down (default OFF).
//
// v7: synced HAND POSITIONS ([RealtimeProperty] 3 & 4,
//   UNRELIABLE stream) in player-local space. The local client writes
//   its controller positions every frame while in VR; RobotAnimator v2
//   aims remote robots' arms at them. Vector3.zero = "no target".
//   SCHEMA ADDITION again: all clients must run the same build.
//
// v6: synced _inVR flag ([RealtimeProperty] id 2) so REMOTE
//   clients know whether this player is in VR. RobotAnimator uses it
//   to swing arms for VR players only (PC = legs only, per design).
//   The local client polls its XR state every 0.25s and updates the
//   model on change. SCHEMA ADDITION: all clients must run the same
//   build (don't mix with <=721 clients).
//
// v5: local avatar moved to "LocalMirror" layer instead of hidden
//   (own reflection in ClassroomMirror). Falls back to hiding if the
//   layer is missing.
// v4: synced int = packed 24-bit RGB color (color wheel support).
//   API: SetColor(Color), CurrentColor. Keys 1-5 = presets.
// ============================================================

[RealtimeModel]
public partial class AvatarModel {
    [RealtimeProperty(1, true, true)]
    private int _avatarId;   // packed RGB since v4 (name kept for schema stability)

    [RealtimeProperty(2, true, true)]
    private bool _inVR;      // v6: is this player in an XR session?

    [RealtimeProperty(3, false)]
    private Vector3 _handL;  // v7: left controller pos, player-local (zero = none)

    [RealtimeProperty(4, false)]
    private Vector3 _handR;  // v7: right controller pos, player-local (zero = none)
}

public class AvatarSelector : RealtimeComponent<AvatarModel> {
    [Header("Avatar child (drag from prefab hierarchy)")]
    public GameObject avatarDefault;   // the robot

    [Header("Show my own avatar to myself (leave OFF until Phase C)")]
    public bool showLocalAvatar = false;

    [Header("First-person body parts (local player, VR embodiment)")]
    [Tooltip("Show your own robot arms in first person (they track your controllers).")]
    public bool showArmsToSelf = true;
    [Tooltip("Show your own legs when looking down. Off = mirror-only.")]
    public bool showLegsToSelf = false;

    [Header("Capsule visual to hide (empty = root MeshRenderer)")]
    public MeshRenderer capsuleRenderer;

    /// <summary>Preset palette for the 1-5 keyboard shortcuts.</summary>
    public static readonly Color[] PresetColors = {
        new Color(0.05f, 0.75f, 0.95f), // 1 cyan
        new Color(0.90f, 0.20f, 0.15f), // 2 red
        new Color(0.15f, 0.80f, 0.30f), // 3 green
        new Color(0.95f, 0.70f, 0.10f), // 4 gold
        new Color(0.60f, 0.25f, 0.90f), // 5 purple
    };

    bool _localIdChosen;
    float _nextXRCheck;
    static readonly List<XRInputSubsystem> _subsystems = new List<XRInputSubsystem>();

    /// <summary>True when this Player belongs to the local client.</summary>
    public bool IsLocallyOwned {
        get { return realtimeView != null && realtimeView.isOwnedLocallySelf; }
    }

    /// <summary>This player's current accent color (white before the model syncs).</summary>
    public Color CurrentColor {
        get { return model != null ? UnpackColor(model.avatarId) : Color.white; }
    }

    /// <summary>Is this player (local OR remote) in a VR session? Synced.</summary>
    public bool InVR {
        get { return model != null && model.inVR; }
    }

    /// <summary>Synced hand positions in PLAYER-LOCAL space (zero = untracked).</summary>
    public Vector3 HandLLocal { get { return model != null ? model.handL : Vector3.zero; } }
    public Vector3 HandRLocal { get { return model != null ? model.handR : Vector3.zero; } }

    VRLocomotion _locomotion;   // cached rig ref for local hand sampling

    /// <summary>Sets the local player's robot color and syncs it. Called by
    /// the color wheel and the 1-5 keys. No-op on remote players.</summary>
    public void SetColor(Color color) {
        if (!IsLocallyOwned || model == null) return;
        model.avatarId = PackColor(color);
    }

    // ---------- Color packing (24-bit RGB in the synced int) ----------
    public static int PackColor(Color c) {
        int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
        int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
        int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
        return (r << 16) | (g << 8) | b;
    }

    public static Color UnpackColor(int v) {
        return new Color(
            ((v >> 16) & 255) / 255f,
            ((v >> 8)  & 255) / 255f,
            ( v        & 255) / 255f);
    }

    void Start() {
        if (capsuleRenderer == null) capsuleRenderer = GetComponent<MeshRenderer>();
        if (capsuleRenderer != null) capsuleRenderer.enabled = false;
    }

    void Update() {
        if (!_localIdChosen && IsLocallyOwned && model != null) {
            // Spawn color: random hue, vivid.
            Color spawn = Color.HSVToRGB(Random.value, 0.85f, 1f);
            model.avatarId = PackColor(spawn);
            _localIdChosen = true;
            ApplyAvatar(model.avatarId); // re-apply now that we know we're local
        }

        if (IsLocallyOwned && model != null) {
            // Desktop shortcut: number keys 1-5 jump to preset colors.
            for (int i = 0; i < PresetColors.Length; i++) {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SetColor(PresetColors[i]);
            }

            // Keep the synced VR flag current (handles entering/leaving VR).
            if (Time.time >= _nextXRCheck) {
                _nextXRCheck = Time.time + 0.25f;
                bool xr = IsInXRNow();
                if (model.inVR != xr) model.inVR = xr;
            }

            // v7: stream controller positions (player-local) while in VR.
            if (model.inVR) {
                if (_locomotion == null)
                    _locomotion = Object.FindFirstObjectByType<VRLocomotion>();
                if (_locomotion != null) {
                    var lc = _locomotion.LeftController;
                    var rc = _locomotion.RightController;
                    model.handL = (lc != null && lc.isActiveAndEnabled)
                        ? transform.InverseTransformPoint(lc.transform.position)
                        : Vector3.zero;
                    model.handR = (rc != null && rc.isActiveAndEnabled)
                        ? transform.InverseTransformPoint(rc.transform.position)
                        : Vector3.zero;
                }
            } else if (model.handL != Vector3.zero || model.handR != Vector3.zero) {
                model.handL = Vector3.zero;
                model.handR = Vector3.zero;
            }
        }
    }

    static bool IsInXRNow() {
        SubsystemManager.GetSubsystems(_subsystems);
        foreach (XRInputSubsystem s in _subsystems)
            if (s != null && s.running) return true;
        return false;
    }

    // ---------- Normcore plumbing ----------
    protected override void OnRealtimeModelReplaced(AvatarModel previousModel, AvatarModel currentModel) {
        if (previousModel != null)
            previousModel.avatarIdDidChange -= AvatarIdDidChange;

        if (currentModel != null) {
            currentModel.avatarIdDidChange += AvatarIdDidChange;
            ApplyAvatar(currentModel.avatarId);
        }
    }

    void AvatarIdDidChange(AvatarModel m, int value) {
        ApplyAvatar(value);
    }

    // ---------- Visuals ----------
    void ApplyAvatar(int packedColor) {
        if (avatarDefault == null) return;

        bool isLocal = IsLocallyOwned;
        if (isLocal && !showLocalAvatar) {
            int mirrorLayer = LayerMask.NameToLayer("LocalMirror");
            if (mirrorLayer >= 0) {
                // Mirror trick: keep the avatar ACTIVE but on a layer the
                // main camera culls. ClassroomMirror's camera still sees it,
                // so you get your own reflection without seeing your body.
                avatarDefault.SetActive(true);
                SetLayerRecursive(avatarDefault, mirrorLayer);
                // v8: selected parts back to Default so the local player
                // sees them first-person (they also still show in the mirror).
                if (showArmsToSelf) {
                    SetPartLayer("ArmL", 0);
                    SetPartLayer("ArmR", 0);
                }
                if (showLegsToSelf) {
                    SetPartLayer("LegL", 0);
                    SetPartLayer("LegR", 0);
                }
                HideLayerFromMainCamera(mirrorLayer);
                TintAccent(avatarDefault, UnpackColor(packedColor));
            } else {
                // Layer not set up in this project: old behavior.
                Debug.LogWarning("[AvatarSelector] Layer 'LocalMirror' missing (Project Settings > Tags and Layers) - own reflection disabled.");
                avatarDefault.SetActive(false);
            }
            return;
        }

        avatarDefault.SetActive(true);
        TintAccent(avatarDefault, UnpackColor(packedColor));
    }

    void SetPartLayer(string partName, int layer) {
        Transform part = FindDeep(avatarDefault.transform, partName);
        if (part != null) SetLayerRecursive(part.gameObject, layer);
    }

    static Transform FindDeep(Transform root, string name) {
        if (root.name == name) return root;
        foreach (Transform child in root) {
            Transform hit = FindDeep(child, name);
            if (hit != null) return hit;
        }
        return null;
    }

    static void SetLayerRecursive(GameObject go, int layer) {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    static void HideLayerFromMainCamera(int layer) {
        Camera cam = Camera.main;
        if (cam != null) cam.cullingMask &= ~(1 << layer);
    }

    void TintAccent(GameObject robot, Color color) {
        foreach (var r in robot.GetComponentsInChildren<MeshRenderer>(true)) {
            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++) {
                if (mats[i] != null && mats[i].name.Contains("RobotAccent")) {
                    mats[i].color = color;
                    if (mats[i].HasProperty("_BaseColor"))
                        mats[i].SetColor("_BaseColor", color);
                }
            }
        }
    }
}
