using UnityEngine;

/// <summary>
/// RobotAnimator v5 - procedural walk (legs) + VR arm aim IK.
///
/// v5 changes (arm accuracy):
///   - ANTI-CROSS CLAMP: the aim direction is converted to avatar-local
///     space and prevented from reaching across the body midline, the way
///     a real shoulder can't. Kills the "crossed arms" look even when a
///     target is briefly wrong.
///   - POLE GUARD: shortest-arc rotation is undefined when the target is
///     exactly opposite the rest axis (straight up). Nudged before solving.
///   - DEBUG TARGETS: optional twice-a-second log of shoulder vs controller
///     world positions, so the real delta is measured, not guessed.
///
/// v4: removed v3 controller-rotation stacking. Arms aim toward controller
/// POSITION only.
///
/// No RealtimeModel change - schema is unchanged, so this does NOT require
/// all clients to be on the same build.
/// </summary>
public class RobotAnimator : MonoBehaviour
{
    [Header("Legs")]
    [Tooltip("Max leg swing, degrees (at fullSpeed).")]
    [SerializeField] private float legAmplitude = 32f;

    [Tooltip("Speed (m/s) at which leg swing reaches full amplitude.")]
    [SerializeField] private float fullSpeed = 2.5f;

    [Tooltip("Walk-cycle rate: phase radians advanced per meter moved.")]
    [SerializeField] private float phasePerMeter = 4.2f;

    [Tooltip("How quickly measured speed responds (higher = snappier).")]
    [SerializeField] private float speedSmoothing = 10f;

    [Header("VR arm tracking")]
    [Tooltip("How quickly arms rotate toward the hand target.")]
    [SerializeField] private float armAimSpeed = 14f;

    [Tooltip("How far an arm may reach across the body midline. 0 = never crosses, 1 = unrestricted. 0.25 is roughly human.")]
    [Range(0f, 1f)]
    [SerializeField] private float maxCrossReach = 0.25f;

    [Tooltip("DEBUG: log shoulder and controller world positions twice a second.")]
    [SerializeField] private bool debugTargets = false;

    private Transform armL, armR, legL, legR;
    private Quaternion armL0, armR0, legL0, legR0;
    private Transform leftSideArm, rightSideArm;
    private Quaternion leftSideRest, rightSideRest;
    private AvatarSelector selector;
    private Transform playerRoot;
    private VRLocomotion locomotion;

    private Vector3 lastPos;
    private float smoothedSpeed;
    private float phase;
    private float nextDebugLog;

    private void Awake()
    {
        armL = FindDeep(transform, "ArmL");
        armR = FindDeep(transform, "ArmR");
        legL = FindDeep(transform, "LegL");
        legR = FindDeep(transform, "LegR");
        selector = GetComponentInParent<AvatarSelector>();
        playerRoot = selector != null ? selector.transform : transform.parent;

        if (legL == null || legR == null)
        {
            Debug.LogError("[RobotAnimator] Limb children not found - is the segmented Avatar_Default.fbx imported? Disabling.");
            enabled = false;
            return;
        }
        legL0 = legL.localRotation;
        legR0 = legR.localRotation;

        if (armL != null && armR != null)
        {
            armL0 = armL.localRotation;
            armR0 = armR.localRotation;
            float xL = transform.InverseTransformPoint(armL.position).x;
            bool armLIsLeftSide = xL < 0f;
            leftSideArm   = armLIsLeftSide ? armL  : armR;
            rightSideArm  = armLIsLeftSide ? armR  : armL;
            leftSideRest  = armLIsLeftSide ? armL0 : armR0;
            rightSideRest = armLIsLeftSide ? armR0 : armL0;
        }
        lastPos = transform.position;
    }

    private void Update()
    {
        // ---- Legs: procedural walk from own movement ----
        Vector3 pos = transform.position;
        Vector3 delta = pos - lastPos;
        lastPos = pos;
        delta.y = 0f;
        float rawSpeed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, rawSpeed,
            1f - Mathf.Exp(-speedSmoothing * Time.deltaTime));
        phase += smoothedSpeed * phasePerMeter * Time.deltaTime;

        float strength = Mathf.Clamp01(smoothedSpeed / fullSpeed);
        float swing = Mathf.Sin(phase) * strength;
        legL.localRotation = legL0 * Quaternion.Euler(swing * legAmplitude, 0f, 0f);
        legR.localRotation = legR0 * Quaternion.Euler(-swing * legAmplitude, 0f, 0f);

        // ---- Arms ----
        if (leftSideArm == null || rightSideArm == null || selector == null) return;

        if (selector.InVR)
        {
            Vector3 handLPos, handRPos;
            bool haveL, haveR;
            GetHandTargets(out handLPos, out haveL, out handRPos, out haveR);

            AimArm(leftSideArm,  leftSideRest,  handLPos, haveL, -1f);
            AimArm(rightSideArm, rightSideRest, handRPos, haveR, +1f);

            if (debugTargets && Time.time >= nextDebugLog)
            {
                nextDebugLog = Time.time + 0.5f;
                Debug.Log("[RobotAnimator] shoulderL=" + leftSideArm.position.ToString("F2")
                    + " targetL=" + (haveL ? handLPos.ToString("F2") : "NONE")
                    + " | shoulderR=" + rightSideArm.position.ToString("F2")
                    + " targetR=" + (haveR ? handRPos.ToString("F2") : "NONE"));
            }
        }
        else
        {
            // PC players: ease arms back to rest at sides
            float t = 1f - Mathf.Exp(-armAimSpeed * Time.deltaTime);
            leftSideArm.localRotation = Quaternion.Slerp(leftSideArm.localRotation, leftSideRest, t);
            rightSideArm.localRotation = Quaternion.Slerp(rightSideArm.localRotation, rightSideRest, t);
        }
    }

    private void GetHandTargets(out Vector3 lPos, out bool haveL,
                                out Vector3 rPos, out bool haveR)
    {
        lPos = rPos = Vector3.zero; haveL = haveR = false;

        if (selector.IsLocallyOwned)
        {
            // LOCAL: read live controller transforms (zero latency)
            if (locomotion == null)
                locomotion = Object.FindFirstObjectByType<VRLocomotion>();
            if (locomotion != null)
            {
                var lc = locomotion.LeftController;
                var rc = locomotion.RightController;
                if (lc != null && lc.isActiveAndEnabled)
                {
                    lPos = lc.transform.position;
                    haveL = true;
                }
                if (rc != null && rc.isActiveAndEnabled)
                {
                    rPos = rc.transform.position;
                    haveR = true;
                }
            }
        }
        else
        {
            // REMOTE: synced player-local hand positions from AvatarSelector
            Vector3 hl = selector.HandLLocal;
            Vector3 hr = selector.HandRLocal;
            if (hl != Vector3.zero) { lPos = playerRoot.TransformPoint(hl); haveL = true; }
            if (hr != Vector3.zero) { rPos = playerRoot.TransformPoint(hr); haveR = true; }
        }
    }

    /// <summary>Aims one arm's rest-down axis at a world target.
    /// sideSign: -1 for the left-side arm, +1 for the right-side arm.</summary>
    private void AimArm(Transform arm, Quaternion rest, Vector3 targetPos,
                        bool haveTarget, float sideSign)
    {
        float t = 1f - Mathf.Exp(-armAimSpeed * Time.deltaTime);
        Quaternion parentRot = arm.parent != null ? arm.parent.rotation : Quaternion.identity;
        Quaternion restWorld = parentRot * rest;

        Quaternion desiredWorld;
        if (haveTarget)
        {
            Vector3 dir = targetPos - arm.position;
            if (dir.sqrMagnitude < 0.0004f)
            {
                desiredWorld = restWorld;
            }
            else
            {
                dir.Normalize();

                // --- v5 anti-cross clamp, evaluated in avatar-local space ---
                Vector3 dl = transform.InverseTransformDirection(dir);
                float cross = -sideSign * dl.x;      // > 0 means reaching across the body
                if (cross > maxCrossReach)
                {
                    dl.x = -sideSign * maxCrossReach;
                    if (dl.sqrMagnitude < 0.0001f) dl = Vector3.down;
                    dl.Normalize();
                    dir = transform.TransformDirection(dl);
                }

                Vector3 restDown = restWorld * Vector3.down;

                // Pole guard: shortest-arc rotation is undefined when the
                // target sits exactly opposite the rest axis.
                if (Vector3.Dot(dir, -restDown) > 0.9995f)
                    dir = (dir + restWorld * Vector3.forward * 0.02f).normalized;

                Quaternion aimRot = Quaternion.FromToRotation(restDown, dir);
                desiredWorld = aimRot * restWorld;
            }
        }
        else
        {
            desiredWorld = restWorld;
        }

        Quaternion desiredLocal = Quaternion.Inverse(parentRot) * desiredWorld;
        arm.localRotation = Quaternion.Slerp(arm.localRotation, desiredLocal, t);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform hit = FindDeep(child, name);
            if (hit != null) return hit;
        }
        return null;
    }
}