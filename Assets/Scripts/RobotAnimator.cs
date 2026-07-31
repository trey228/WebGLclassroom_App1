using UnityEngine;

/// <summary>
/// RobotAnimator v6.1 - procedural walk (legs) + VR arm IK with a real elbow.
///
/// v6.1 changes (axis-convention independence):
///   - REST AXES ARE MEASURED, NOT ASSUMED. v6 and earlier assumed each limb
///     hung along its local -Y (Vector3.down). That is only true when the FBX
///     is exported with Blender's experimental "Apply Transform" baking the
///     Z-up -> Y-up conversion into every object. Without it the conversion
///     lives on the model root, limbs hang along local -Z, and every arm aims
///     90 degrees wrong. The rest axis is now read from the rig itself: the
///     direction from a joint to its child joint IS "down" for that bone.
///   - BODY AXES ARE MEASURED TOO. "Right" is the vector between the two
///     shoulders; "down" is the upper arm's rest axis; "back" is their cross
///     product. Nothing reads transform.forward/up, which are meaningless on
///     the avatar root when the axis conversion sits there.
///   - CONSEQUENCE: this script now works correctly whether or not Apply
///     Transform is used, and survives future re-exports either way.
///
/// v6 changes (two-bone IK):
///   - TWO-BONE IK via law of cosines, when a Forearm/Hand chain exists.
///   - BONE LENGTHS ARE MEASURED, NOT SERIALIZED. A serialized length would
///     deserialize as 0 on existing prefab instances and collapse the arms.
///   - GRACEFUL FALLBACK to v5 single-bone aim if the chain is missing.
///   - POLE VECTOR so the elbow never flips or folds through the torso.
///   - DESKTOP TEST MODE (debugArmTest) to validate in the mirror.
///
/// v5: anti-cross clamp, pole guard, debug target logging.
/// v4: removed v3 controller-rotation stacking. Arms aim toward controller
/// POSITION only.
///
/// No RealtimeModel change - schema is unchanged. The solve runs locally on
/// every client from hand positions already on the wire, so this does NOT
/// require all clients to be on the same build.
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

    [Tooltip("How far an arm may reach across the body midline. 0 = never crosses, 1 = unrestricted. With a real elbow, 0.25-0.4 is roughly human.")]
    [Range(0f, 1f)]
    [SerializeField] private float maxCrossReach = 0.25f;

    [Tooltip("DEBUG: log shoulder and controller world positions twice a second.")]
    [SerializeField] private bool debugTargets = false;

    [Tooltip("DEBUG: drive synthetic hand targets on desktop so the elbow can be checked in the mirror without a headset. Leave OFF for builds.")]
    [SerializeField] private bool debugArmTest = false;

    // --- Code-level tunables -------------------------------------------------
    // const on purpose. New serialized fields deserialize as 0 on existing
    // component instances and silently break the thing they were added to fix.

    /// <summary>How far the elbow is pushed away from the body, relative to how
    /// far it is pushed backward. 0 = elbow points straight back.</summary>
    private const float PoleOutward = 0.35f;

    /// <summary>Keeps the solved distance clear of the fully-folded and
    /// fully-extended singularities.</summary>
    private const float ReachEpsilon = 0.001f;

    /// <summary>Below this the arm chain is treated as unmeasurable and IK is
    /// disabled rather than producing garbage.</summary>
    private const float MinBoneLength = 0.01f;

    private Transform armL, armR, legL, legR;
    private Quaternion armL0, armR0, legL0, legR0;

    private Transform leftSideArm, rightSideArm;
    private Quaternion leftSideRest, rightSideRest;

    // Measured rest axis for each bone, in that bone's LOCAL space. This is the
    // direction the bone points at rest - "down" for an arm hanging at the side.
    private Vector3 leftArmAxis   = Vector3.down;
    private Vector3 rightArmAxis  = Vector3.down;
    private Vector3 leftForeAxis  = Vector3.down;
    private Vector3 rightForeAxis = Vector3.down;

    // IK chain, resolved per side
    private Transform leftFore, rightFore;
    private Quaternion leftForeRest, rightForeRest;
    private float leftUpperLen, leftLowerLen;
    private float rightUpperLen, rightLowerLen;
    private bool hasIK;

    // Body frame, recomputed each frame from rig geometry (never from
    // transform.forward/up, which depend on the FBX axis convention).
    private Vector3 bodyRight = Vector3.right;
    private Vector3 bodyDown  = Vector3.down;
    private Vector3 bodyBack  = Vector3.back;

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

            SetupIK();
        }
        lastPos = transform.position;
    }

    /// <summary>Resolves the Forearm/Hand chain under each arm, measures bone
    /// lengths, and measures the rest axis of every bone from the rig itself.
    /// Searches by prefix from the arm, so it does not depend on L/R suffixes
    /// matching between arm and forearm.</summary>
    private void SetupIK()
    {
        leftFore  = FindDeepPrefix(leftSideArm,  "Forearm");
        rightFore = FindDeepPrefix(rightSideArm, "Forearm");

        Transform leftHand  = leftFore  != null ? FindDeepPrefix(leftFore,  "Hand") : null;
        Transform rightHand = rightFore != null ? FindDeepPrefix(rightFore, "Hand") : null;

        // Rest axes first - needed by the single-bone fallback too.
        leftArmAxis  = MeasureRestAxis(leftSideArm,  leftFore);
        rightArmAxis = MeasureRestAxis(rightSideArm, rightFore);

        if (leftFore == null || rightFore == null || leftHand == null || rightHand == null)
        {
            hasIK = false;
            Debug.LogWarning("[RobotAnimator] v6.1: no Forearm/Hand chain found under the arms - "
                + "falling back to single-bone aim. Re-export Avatar_Default.fbx with "
                + "ArmL > ForearmL > HandL (and R) to enable the elbow.");
            return;
        }

        leftForeRest  = leftFore.localRotation;
        rightForeRest = rightFore.localRotation;

        leftForeAxis  = MeasureRestAxis(leftFore,  leftHand);
        rightForeAxis = MeasureRestAxis(rightFore, rightHand);

        leftUpperLen  = Vector3.Distance(leftSideArm.position,  leftFore.position);
        leftLowerLen  = Vector3.Distance(leftFore.position,     leftHand.position);
        rightUpperLen = Vector3.Distance(rightSideArm.position, rightFore.position);
        rightLowerLen = Vector3.Distance(rightFore.position,    rightHand.position);

        hasIK = leftUpperLen  > MinBoneLength && leftLowerLen  > MinBoneLength
             && rightUpperLen > MinBoneLength && rightLowerLen > MinBoneLength;

        if (!hasIK)
        {
            Debug.LogWarning("[RobotAnimator] v6.1: arm bone lengths measured near zero "
                + "(L " + leftUpperLen.ToString("F3") + "/" + leftLowerLen.ToString("F3")
                + ", R " + rightUpperLen.ToString("F3") + "/" + rightLowerLen.ToString("F3")
                + ") - falling back to single-bone aim.");
        }
        else
        {
            Debug.Log("[RobotAnimator] v6.1 two-bone IK active. Bone lengths L "
                + leftUpperLen.ToString("F3") + "/" + leftLowerLen.ToString("F3")
                + ", R " + rightUpperLen.ToString("F3") + "/" + rightLowerLen.ToString("F3")
                + " | rest axis arm " + leftArmAxis.ToString("F2")
                + " fore " + leftForeAxis.ToString("F2"));
        }
    }

    /// <summary>Works out which way a bone points at rest, in its own local
    /// space, without assuming an FBX axis convention.
    /// 1) Direction to the child joint - exact, preferred.
    /// 2) Offset of the mesh bounds centre - works for a tip bone with no child.
    /// 3) Vector3.down - last resort.</summary>
    private static Vector3 MeasureRestAxis(Transform bone, Transform childJoint)
    {
        if (bone == null) return Vector3.down;

        if (childJoint != null)
        {
            Vector3 d = bone.InverseTransformPoint(childJoint.position);
            if (d.sqrMagnitude > 0.000001f) return d.normalized;
        }

        Renderer r = bone.GetComponent<Renderer>();
        if (r != null)
        {
            Vector3 c = r.localBounds.center;
            if (c.sqrMagnitude > 0.000001f) return c.normalized;
        }

        return Vector3.down;
    }

    /// <summary>Rebuilds the body frame from rig geometry. Shoulder-to-shoulder
    /// gives "right"; the upper arm's rest axis gives "down"; their cross gives
    /// "back". All three follow the avatar's yaw automatically and none depend
    /// on how the FBX was oriented.</summary>
    private void UpdateBodyFrame()
    {
        Vector3 shoulderSpan = rightSideArm.position - leftSideArm.position;
        bodyRight = shoulderSpan.sqrMagnitude > 0.000001f
            ? shoulderSpan.normalized
            : transform.right;

        Quaternion leftParentRot = leftSideArm.parent != null
            ? leftSideArm.parent.rotation : Quaternion.identity;
        bodyDown = (leftParentRot * leftSideRest * leftArmAxis).normalized;

        Vector3 back = Vector3.Cross(bodyRight, bodyDown);
        bodyBack = back.sqrMagnitude > 0.000001f ? back.normalized : -transform.forward;
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

        UpdateBodyFrame();

        bool driveArms = selector.InVR;
        Vector3 handLPos = Vector3.zero, handRPos = Vector3.zero;
        bool haveL = false, haveR = false;

        if (driveArms)
        {
            GetHandTargets(out handLPos, out haveL, out handRPos, out haveR);
        }
        else if (debugArmTest)
        {
            // Desktop validation: synthetic sweep so the elbow can be watched in
            // the mirror. VR-only bugs cannot be tested in the editor, but
            // mirror bugs absolutely can.
            driveArms = true;
            handLPos = DebugSweepTarget(leftSideArm,  -1f); haveL = true;
            handRPos = DebugSweepTarget(rightSideArm, +1f); haveR = true;
        }

        if (driveArms)
        {
            SolveArm(leftSideArm,  leftSideRest,  leftArmAxis,  leftFore,  leftForeRest,  leftForeAxis,
                     leftUpperLen,  leftLowerLen,  handLPos, haveL, -1f);
            SolveArm(rightSideArm, rightSideRest, rightArmAxis, rightFore, rightForeRest, rightForeAxis,
                     rightUpperLen, rightLowerLen, handRPos, haveR, +1f);

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
            leftSideArm.localRotation  = Quaternion.Slerp(leftSideArm.localRotation,  leftSideRest,  t);
            rightSideArm.localRotation = Quaternion.Slerp(rightSideArm.localRotation, rightSideRest, t);
            if (hasIK)
            {
                leftFore.localRotation  = Quaternion.Slerp(leftFore.localRotation,  leftForeRest,  t);
                rightFore.localRotation = Quaternion.Slerp(rightFore.localRotation, rightForeRest, t);
            }
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

    /// <summary>Routes to two-bone IK when the chain exists, else single-bone aim.</summary>
    private void SolveArm(Transform upper, Quaternion upperRest, Vector3 upperAxis,
                          Transform fore, Quaternion foreRest, Vector3 foreAxis,
                          float upperLen, float lowerLen,
                          Vector3 targetPos, bool haveTarget, float sideSign)
    {
        if (hasIK)
            AimArmIK(upper, upperRest, upperAxis, fore, foreRest, foreAxis,
                     upperLen, lowerLen, targetPos, haveTarget, sideSign);
        else
            AimArm(upper, upperRest, upperAxis, targetPos, haveTarget, sideSign);
    }

    /// <summary>Clamps an aim direction so the arm cannot reach across the body
    /// midline further than maxCrossReach. Evaluated in world space against the
    /// measured body frame, so no local axis convention is assumed.</summary>
    private Vector3 ApplyCrossClamp(Vector3 dir, float sideSign)
    {
        float along = Vector3.Dot(dir, bodyRight);
        float crossAmt = -sideSign * along;          // > 0 means reaching across
        if (crossAmt <= maxCrossReach) return dir;

        float allowed = -sideSign * maxCrossReach;
        Vector3 clamped = dir + bodyRight * (allowed - along);
        if (clamped.sqrMagnitude < 0.0001f) clamped = bodyDown;
        return clamped.normalized;
    }

    /// <summary>Two-bone IK. Places the elbow by law of cosines, then aims each
    /// segment's measured rest axis along its solved direction.
    /// sideSign: -1 for the left-side arm, +1 for the right-side arm.</summary>
    private void AimArmIK(Transform upper, Quaternion upperRest, Vector3 upperAxis,
                          Transform fore, Quaternion foreRest, Vector3 foreAxis,
                          float L1, float L2,
                          Vector3 targetPos, bool haveTarget, float sideSign)
    {
        float t = 1f - Mathf.Exp(-armAimSpeed * Time.deltaTime);

        Quaternion upperParentRot = upper.parent != null ? upper.parent.rotation : Quaternion.identity;
        Quaternion upperRestWorld = upperParentRot * upperRest;

        Vector3 shoulder = upper.position;
        Vector3 toTarget = haveTarget ? targetPos - shoulder : Vector3.zero;

        if (!haveTarget || toTarget.sqrMagnitude < 0.0004f)
        {
            upper.localRotation = Quaternion.Slerp(upper.localRotation, upperRest, t);
            fore.localRotation  = Quaternion.Slerp(fore.localRotation,  foreRest,  t);
            return;
        }

        Vector3 dir = ApplyCrossClamp(toTarget.normalized, sideSign);

        // Distance clamped clear of both singularities. Out-of-range targets
        // leave the arm straight and short rather than stretching to meet them.
        float d = Mathf.Clamp(toTarget.magnitude,
                              Mathf.Abs(L1 - L2) + ReachEpsilon,
                              (L1 + L2) - ReachEpsilon);
        Vector3 clampedTarget = shoulder + dir * d;

        // Law of cosines: angle at the shoulder between shoulder->target and
        // shoulder->elbow.
        float cosA = (L1 * L1 + d * d - L2 * L2) / (2f * L1 * d);
        cosA = Mathf.Clamp(cosA, -1f, 1f);
        float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));

        // Pole: elbow rides backward and outward from the body. Built as an
        // orthonormal basis rather than a cross-product rotation, so there is no
        // handedness sign to get wrong.
        Vector3 poleDir = (bodyBack + bodyRight * (sideSign * PoleOutward)).normalized;
        Vector3 pPerp = poleDir - dir * Vector3.Dot(poleDir, dir);
        if (pPerp.sqrMagnitude < 0.000001f)
        {
            pPerp = Vector3.Cross(dir, bodyRight);
            if (pPerp.sqrMagnitude < 0.000001f) pPerp = Vector3.Cross(dir, bodyBack);
        }
        Vector3 bendAxis = pPerp.normalized;

        Vector3 upperDir = (cosA * dir + sinA * bendAxis).normalized;

        // --- upper arm ---
        Vector3 restAxisU = upperRestWorld * upperAxis;
        if (Vector3.Dot(upperDir, -restAxisU) > 0.9995f)
            upperDir = (upperDir + bendAxis * 0.02f).normalized;

        Quaternion desiredUpperWorld = Quaternion.FromToRotation(restAxisU, upperDir) * upperRestWorld;
        upper.localRotation = Quaternion.Slerp(upper.localRotation,
            Quaternion.Inverse(upperParentRot) * desiredUpperWorld, t);

        // --- forearm ---
        // Measured from where the upper arm ACTUALLY ended up this frame, not
        // where the solve wanted it. While the slerp is catching up the two
        // disagree, and aiming from the real elbow keeps the hand converging on
        // the target instead of overshooting it.
        Vector3 elbow = fore.position;
        Vector3 toEnd = clampedTarget - elbow;
        if (toEnd.sqrMagnitude < 0.000001f)
        {
            fore.localRotation = Quaternion.Slerp(fore.localRotation, foreRest, t);
            return;
        }
        Vector3 lowerDir = toEnd.normalized;

        Quaternion foreParentRot = fore.parent != null ? fore.parent.rotation : Quaternion.identity;
        Quaternion foreRestWorld = foreParentRot * foreRest;
        Vector3 restAxisF = foreRestWorld * foreAxis;
        if (Vector3.Dot(lowerDir, -restAxisF) > 0.9995f)
            lowerDir = (lowerDir + bendAxis * 0.02f).normalized;

        Quaternion desiredForeWorld = Quaternion.FromToRotation(restAxisF, lowerDir) * foreRestWorld;
        fore.localRotation = Quaternion.Slerp(fore.localRotation,
            Quaternion.Inverse(foreParentRot) * desiredForeWorld, t);
    }

    /// <summary>Single-bone fallback. Aims one arm's measured rest axis at a
    /// world target. Used when no Forearm/Hand chain is present.
    /// sideSign: -1 for the left-side arm, +1 for the right-side arm.</summary>
    private void AimArm(Transform arm, Quaternion rest, Vector3 restAxis,
                        Vector3 targetPos, bool haveTarget, float sideSign)
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
                dir = ApplyCrossClamp(dir.normalized, sideSign);

                Vector3 restAxisWorld = restWorld * restAxis;

                // Pole guard: shortest-arc rotation is undefined when the target
                // sits exactly opposite the rest axis.
                if (Vector3.Dot(dir, -restAxisWorld) > 0.9995f)
                    dir = (dir + bodyBack * 0.02f).normalized;

                desiredWorld = Quaternion.FromToRotation(restAxisWorld, dir) * restWorld;
            }
        }
        else
        {
            desiredWorld = restWorld;
        }

        arm.localRotation = Quaternion.Slerp(arm.localRotation,
            Quaternion.Inverse(parentRot) * desiredWorld, t);
    }

    /// <summary>Synthetic desktop target: sweeps through most of the reachable
    /// envelope, including near-full extension, so the elbow can be checked in
    /// the mirror. Built on the measured body frame. Editor validation only.</summary>
    private Vector3 DebugSweepTarget(Transform shoulder, float sideSign)
    {
        float a = Time.time * 0.9f + (sideSign > 0f ? 1.7f : 0f);
        Vector3 p = shoulder.position;
        p += -bodyBack  * (0.30f + 0.12f * Mathf.Sin(a * 1.3f));
        p += -bodyDown  * (-0.15f + 0.30f * Mathf.Sin(a));
        p +=  bodyRight * (sideSign * (0.10f + 0.18f * Mathf.Cos(a * 0.7f)));
        return p;
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

    /// <summary>Prefix search, used to find the Forearm/Hand under a given arm
    /// without assuming the L/R suffix matches the parent's.</summary>
    private static Transform FindDeepPrefix(Transform root, string prefix)
    {
        foreach (Transform child in root)
        {
            if (child.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return child;
            Transform hit = FindDeepPrefix(child, prefix);
            if (hit != null) return hit;
        }
        return null;
    }
}
