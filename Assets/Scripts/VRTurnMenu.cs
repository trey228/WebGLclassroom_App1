#if NORMCORE

using UnityEngine;
using WebXR;

/// <summary>
/// v5 - Attach to the WebXRCameraSet root, alongside VRLocomotion.
/// VR options menu using Trey's Photoshop design
/// (Assets/Resources/UI/Menu1_VR.png, 1024x640).
///
/// v5 changes:
///   - XR-entry grace period: entering/leaving VR fires spurious controller
///     button events (the press used to enter the session registers as
///     ButtonA down), which opened the menu in the user's face at spawn.
///     Toggle input is now ignored for xrEntryGrace seconds after any XR
///     state change, and the menu force-closes on the transition itself.
///   - Placement: panel opens BELOW eye line (menuHeightOffset) instead of
///     dead center, and tilts up toward the head so it stays readable.
///
/// v4: color wheel, allowDesktopMenu, debugButtons (see history).
///
/// USAGE:
///   VR:      LEFT X opens/closes; RIGHT controller laser; hold trigger to pick.
///   Desktop: M opens/closes; click or drag on the wheel to pick.
/// </summary>
[RequireComponent(typeof(VRLocomotion))]
public class VRTurnMenu : MonoBehaviour
{
    [Tooltip("How far in front of the head the menu opens, meters.")]
    [SerializeField] private float menuDistance = 1.4f;

    [Tooltip("How far BELOW eye level the panel center sits, meters. 0 = old dead-center behavior.")]
    [SerializeField] private float menuHeightOffset = 0.28f;

    [Tooltip("Seconds after entering/leaving VR during which menu toggle input is ignored (spurious WebXR button events).")]
    [SerializeField] private float xrEntryGrace = 1.5f;

    [Tooltip("Panel width in meters (height follows the 1024x640 aspect).")]
    [SerializeField] private float panelWidth = 0.55f;

    [Tooltip("Max laser length, meters.")]
    [SerializeField] private float laserLength = 5f;

    [Tooltip("Desktop (flat mode) menu via M key + mouse. Leave ON - this is a feature, not debug.")]
    [SerializeField] private bool allowDesktopMenu = true;

    [Tooltip("DEBUG ONLY: log every controller button press with its enum name.")]
    [SerializeField] private bool debugButtons = false;

    [Tooltip("Min seconds between network color updates while dragging on the wheel.")]
    [SerializeField] private float colorSendInterval = 0.15f;

    // --- Measured from Menu1_VR.png (1024x640), origin top-left ---
    private const float ZoneCenterU = 0.4907f;
    private const float ZoneCenterV = 0.5310f;
    private const float ZoneWidthU  = 0.4150f;
    private const float ZoneHeightV = 0.1625f;
    private const float BoxCenterU  = 0.3145f;
    private const float BoxCenterV  = 0.5340f;
    private const float BoxWidthU   = 0.0450f;
    private const float BoxHeightV  = 0.0650f;
    private const float WheelCenterU = 0.20f;
    private const float WheelCenterV = 0.200f;
    private const int   WheelTexSize = 256;

    private VRLocomotion locomotion;
    private WebXRRigFollower follower;
    private AvatarSelector localSelector;

    private GameObject menuRoot;
    private Collider toggleZone;
    private GameObject checkFill;
    private Renderer hoverHighlight;
    private LineRenderer laser;

    private Collider wheelZone;
    private Transform wheelQuad;
    private Transform colorMarker;
    private Renderer colorMarkerFill;
    private Transform hoverDot;
    private float lastColorSend;

    // v5: XR transition tracking for the input grace period.
    private bool wasInXR;
    private float xrStateChangeTime = -999f;

    private float PanelW { get { return panelWidth; } }
    private float PanelH { get { return panelWidth * 640f / 1024f; } }
    private float WheelRadius { get { return panelWidth * 0.095f; } }

    private void Awake()
    {
        locomotion = GetComponent<VRLocomotion>();
        follower = GetComponent<WebXRRigFollower>();
    }

    private void LateUpdate()
    {
        bool inXR = follower != null && follower.IsInXR();

        // v5: on any XR state change, close the menu and start the input
        // grace window. WebXR fires phantom button-downs during session
        // entry (the click that starts VR lands on the controller), which
        // used to open the menu directly in the user's face at spawn.
        if (inXR != wasInXR)
        {
            wasInXR = inXR;
            xrStateChangeTime = Time.time;
            if (menuRoot != null && menuRoot.activeSelf) SetMenuVisible(false);
        }
        bool inGrace = Time.time - xrStateChangeTime < xrEntryGrace;

        // Grab the local player's AvatarSelector once it exists.
        if (localSelector == null && follower != null && follower.LocalPlayer != null)
            localSelector = follower.LocalPlayer.GetComponent<AvatarSelector>();

        // Desktop: M key toggles the menu in flat mode.
        if (allowDesktopMenu && !inXR && Input.GetKeyDown(KeyCode.M))
        {
            SetMenuVisible(menuRoot == null || !menuRoot.activeSelf);
        }

        // Outside VR the menu only exists if desktop support is on.
        if (!inXR && !allowDesktopMenu)
        {
            if (menuRoot != null && menuRoot.activeSelf) SetMenuVisible(false);
            return;
        }

        WebXRController left = locomotion.LeftController;
        WebXRController right = locomotion.RightController;

        if (debugButtons)
        {
            SpyButtons(left, "LEFT");
            SpyButtons(right, "RIGHT");
        }

        // Toggle menu with the LEFT controller's X button ONLY (ButtonA on
        // that hand). Y (ButtonB) belongs to VoiceControls for mute.
        // v5: ignored during the XR-entry grace window.
        if (inXR && !inGrace && left != null && left.isActiveAndEnabled
            && left.GetButtonDown(WebXRController.ButtonTypes.ButtonA))
        {
            SetMenuVisible(menuRoot == null || !menuRoot.activeSelf);
        }

        if (menuRoot == null || !menuRoot.activeSelf) return;

        // Pointer source: VR = right controller ray + trigger (held for drag).
        // Desktop = camera ray + mouse button (held for drag).
        Vector3 origin;
        Vector3 dir;
        bool selectDown;
        bool selectHeld;
        if (inXR)
        {
            if (right == null || !right.isActiveAndEnabled) return;
            origin = right.transform.position;
            dir = right.transform.forward;
            selectDown = right.GetButtonDown(WebXRController.ButtonTypes.Trigger);
            selectHeld = right.GetButton(WebXRController.ButtonTypes.Trigger);
            laser.enabled = true;
        }
        else
        {
            Camera cam = follower.RigCamera != null
                ? follower.RigCamera.GetComponent<Camera>() : null;
            if (cam == null) return;
            Ray camRay = Cursor.lockState == CursorLockMode.Locked
                ? new Ray(cam.transform.position, cam.transform.forward)
                : cam.ScreenPointToRay(Input.mousePosition);
            origin = camRay.origin;
            dir = camRay.direction;
            selectDown = Input.GetMouseButtonDown(0);
            selectHeld = Input.GetMouseButton(0);
            laser.enabled = false;
        }

        Vector3 laserEnd = origin + dir * laserLength;

        bool hoverToggle = false;
        bool hoverWheel = false;
        Vector3 wheelHitLocal = Vector3.zero;
        if (Physics.Raycast(origin, dir, out RaycastHit hit, laserLength,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (hit.collider == toggleZone)
            {
                hoverToggle = true;
                laserEnd = hit.point;
            }
            else if (hit.collider == wheelZone)
            {
                Vector3 local = menuRoot.transform.InverseTransformPoint(hit.point);
                Vector3 center = wheelQuad.localPosition;
                Vector2 d = new Vector2(local.x - center.x, local.y - center.y);
                if (d.magnitude <= WheelRadius)
                {
                    hoverWheel = true;
                    wheelHitLocal = local;
                    laserEnd = hit.point;
                }
            }
        }

        if (laser.enabled)
        {
            laser.SetPosition(0, origin);
            laser.SetPosition(1, laserEnd);
        }

        hoverHighlight.enabled = hoverToggle;

        if (hoverWheel && hoverDot != null)
        {
            hoverDot.gameObject.SetActive(true);
            hoverDot.localPosition = new Vector3(
                wheelHitLocal.x, wheelHitLocal.y, -0.0028f);
        }
        else if (hoverDot != null)
        {
            hoverDot.gameObject.SetActive(false);
        }

        // --- Turn mode toggle ---
        if (selectDown && hoverToggle)
        {
            bool nowSmooth =
                locomotion.CurrentTurnMode != VRLocomotion.TurnMode.Smooth;
            locomotion.CurrentTurnMode = nowSmooth
                ? VRLocomotion.TurnMode.Smooth
                : VRLocomotion.TurnMode.Snap;
            checkFill.SetActive(nowSmooth);
        }

        // --- Color wheel pick (click or drag) ---
        if (hoverWheel && selectHeld && localSelector != null)
        {
            Vector3 center = wheelQuad.localPosition;
            Vector2 d = new Vector2(wheelHitLocal.x - center.x, wheelHitLocal.y - center.y);
            float sat = Mathf.Clamp01(d.magnitude / WheelRadius);
            float hue = Mathf.Atan2(d.y, d.x) / (2f * Mathf.PI);
            if (hue < 0f) hue += 1f;
            Color picked = Color.HSVToRGB(hue, sat, 1f);

            PlaceColorMarker(hue, sat, picked);
            if (selectDown || Time.time - lastColorSend >= colorSendInterval)
            {
                localSelector.SetColor(picked);
                lastColorSend = Time.time;
            }
        }
    }

    /// <summary>DEBUG: logs any button that went down this frame on a controller.</summary>
    private static void SpyButtons(WebXRController controller, string label)
    {
        if (controller == null || !controller.isActiveAndEnabled) return;
        foreach (WebXRController.ButtonTypes type in
                 System.Enum.GetValues(typeof(WebXRController.ButtonTypes)))
        {
            if (controller.GetButtonDown(type))
                Debug.Log("[ButtonSpy] " + label + " pressed: " + type);
        }
    }

    private void SetMenuVisible(bool visible)
    {
        if (menuRoot == null)
        {
            if (!visible) return;
            BuildMenu();
        }

        if (visible)
        {
            Transform head = follower.RigCamera;
            Vector3 fwd = head.forward; fwd.y = 0f;
            // Guard: looking straight up/down makes the flattened forward
            // zero-length; fall back to rig forward.
            if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
            fwd.Normalize();

            // v5: panel center sits BELOW eye line, tilted up toward the head,
            // instead of parked dead-center in the view.
            Vector3 panelPos = head.position
                + fwd * menuDistance
                + Vector3.down * menuHeightOffset;
            menuRoot.transform.position = panelPos;
            Vector3 toPanel = panelPos - head.position;
            menuRoot.transform.rotation = Quaternion.LookRotation(toPanel.normalized, Vector3.up);

            checkFill.SetActive(
                locomotion.CurrentTurnMode == VRLocomotion.TurnMode.Smooth);
            hoverHighlight.enabled = false;

            if (localSelector != null)
            {
                Color c = localSelector.CurrentColor;
                Color.RGBToHSV(c, out float h, out float s, out float _);
                PlaceColorMarker(h, s, c);
            }
        }

        menuRoot.SetActive(visible);
        laser.enabled = visible;
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    private void BuildMenu()
    {
        float w = PanelW;
        float h = PanelH;

        menuRoot = new GameObject("TurnMenu (local only)");

        GameObject panel = MakeQuad("Panel", new Vector3(0f, 0f, 0.004f),
            new Vector2(w, h));
        Renderer panelRenderer = panel.GetComponent<Renderer>();
        Material template = Resources.Load<Material>("UI/MenuPanelMat");
        if (template != null)
        {
            panelRenderer.material = new Material(template);
        }
        else
        {
            Debug.LogError("[TurnMenu] UI/MenuPanelMat not found - fallback path (may be invisible in builds)");
            Texture2D art = Resources.Load<Texture2D>("UI/Menu1_VR");
            if (art != null) panelRenderer.material.mainTexture = art;
            panelRenderer.material.color = Color.white;
            TryMakeTransparent(panelRenderer.material);
        }
        panelRenderer.material.renderQueue = 3000;

        GameObject zone = new GameObject("ToggleZone");
        zone.transform.SetParent(menuRoot.transform, false);
        zone.transform.localPosition = UvToLocal(ZoneCenterU, ZoneCenterV, w, h);
        BoxCollider box = zone.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(ZoneWidthU * w, ZoneHeightV * h, 0.03f);
        toggleZone = box;

        GameObject highlight = MakeQuad("HoverHighlight",
            UvToLocal(ZoneCenterU, ZoneCenterV, w, h) + new Vector3(0f, 0f, -0.001f),
            new Vector2(ZoneWidthU * w, ZoneHeightV * h));
        hoverHighlight = highlight.GetComponent<Renderer>();
        if (template != null)
        {
            Material hm = new Material(template);
            hm.mainTexture = null;
            hoverHighlight.material = hm;
        }
        hoverHighlight.material.color = new Color(1f, 1f, 1f, 0.18f);
        if (template == null) TryMakeTransparent(hoverHighlight.material);
        hoverHighlight.material.renderQueue = 3005;
        hoverHighlight.enabled = false;

        checkFill = MakeQuad("CheckFill",
            UvToLocal(BoxCenterU, BoxCenterV, w, h) + new Vector3(0f, 0f, -0.002f),
            new Vector2(BoxWidthU * w, BoxHeightV * h));
        Renderer fillRenderer = checkFill.GetComponent<Renderer>();
        if (template != null)
        {
            Material fm = new Material(template);
            fm.mainTexture = null;
            fillRenderer.material = fm;
        }
        fillRenderer.material.color = new Color(0.05f, 0.35f, 0.65f, 1f);
        fillRenderer.material.renderQueue = 3001;
        checkFill.SetActive(false);

        // --- Color wheel ---
        float r = WheelRadius;
        Vector3 wheelPos = UvToLocal(WheelCenterU, WheelCenterV, w, h)
            + new Vector3(0f, 0f, -0.0012f);

        GameObject wheel = MakeQuad("ColorWheel", wheelPos, new Vector2(r * 2f, r * 2f));
        Renderer wheelRenderer = wheel.GetComponent<Renderer>();
        if (template != null)
        {
            Material wm = new Material(template);
            wm.mainTexture = MakeWheelTexture(WheelTexSize);
            wm.color = Color.white;
            if (wm.HasProperty("_BaseMap"))
                wm.SetTexture("_BaseMap", wm.mainTexture);
            wheelRenderer.material = wm;
        }
        else
        {
            wheelRenderer.material.mainTexture = MakeWheelTexture(WheelTexSize);
            wheelRenderer.material.color = Color.white;
            TryMakeTransparent(wheelRenderer.material);
        }
        wheelRenderer.material.renderQueue = 3001;
        wheelQuad = wheel.transform;

        GameObject wzone = new GameObject("WheelZone");
        wzone.transform.SetParent(menuRoot.transform, false);
        wzone.transform.localPosition = wheelPos;
        BoxCollider wbox = wzone.AddComponent<BoxCollider>();
        wbox.isTrigger = true;
        wbox.size = new Vector3(r * 2f, r * 2f, 0.03f);
        wheelZone = wbox;

        GameObject markerOuter = MakeQuad("ColorMarker", wheelPos,
            new Vector2(r * 0.22f, r * 0.22f));
        Renderer mo = markerOuter.GetComponent<Renderer>();
        if (template != null)
        {
            Material mm = new Material(template);
            mm.mainTexture = null;
            mo.material = mm;
        }
        mo.material.color = Color.white;
        if (template == null) TryMakeTransparent(mo.material);
        mo.material.renderQueue = 3002;
        colorMarker = markerOuter.transform;

        GameObject markerInner = MakeQuad("ColorMarkerFill", Vector3.zero,
            new Vector2(0.6f, 0.6f));
        markerInner.transform.SetParent(markerOuter.transform, false);
        markerInner.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        colorMarkerFill = markerInner.GetComponent<Renderer>();
        if (template != null)
        {
            Material im = new Material(template);
            im.mainTexture = null;
            colorMarkerFill.material = im;
        }
        colorMarkerFill.material.color = Color.white;
        if (template == null) TryMakeTransparent(colorMarkerFill.material);
        colorMarkerFill.material.renderQueue = 3003;

        GameObject dot = MakeQuad("WheelHoverDot", wheelPos,
            new Vector2(r * 0.12f, r * 0.12f));
        Renderer dr = dot.GetComponent<Renderer>();
        if (template != null)
        {
            Material dm = new Material(template);
            dm.mainTexture = null;
            dr.material = dm;
        }
        dr.material.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        if (template == null) TryMakeTransparent(dr.material);
        dr.material.renderQueue = 3004;
        hoverDot = dot.transform;
        dot.SetActive(false);

        GameObject laserGo = new GameObject("MenuLaser");
        laserGo.transform.SetParent(transform, false);
        laser = laserGo.AddComponent<LineRenderer>();
        laser.positionCount = 2;
        laser.startWidth = 0.006f;
        laser.endWidth = 0.002f;
        laser.material = new Material(
            checkFill.GetComponent<Renderer>().sharedMaterial);
        laser.material.color = Color.white;
        laser.enabled = false;
    }

    /// <summary>HSV disc: hue by angle, saturation by distance from center,
    /// transparent outside the circle, thin white rim.</summary>
    private static Texture2D MakeWheelTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float half = size * 0.5f;
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float rad = Mathf.Sqrt(dx * dx + dy * dy);
                Color c;
                if (rad > 1f)
                {
                    c = new Color(0, 0, 0, 0);
                }
                else if (rad > 0.965f)
                {
                    c = Color.white;
                }
                else
                {
                    float hue = Mathf.Atan2(dy, dx) / (2f * Mathf.PI);
                    if (hue < 0f) hue += 1f;
                    c = Color.HSVToRGB(hue, Mathf.Clamp01(rad / 0.965f), 1f);
                }
                px[y * size + x] = c;
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>Moves the marker dot to a hue/sat position on the wheel and
    /// fills it with the picked color.</summary>
    private void PlaceColorMarker(float hue, float sat, Color color)
    {
        if (colorMarker == null || wheelQuad == null) return;
        float angle = hue * 2f * Mathf.PI;
        Vector3 center = wheelQuad.localPosition;
        Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f)
            * (sat * WheelRadius);
        colorMarker.localPosition = new Vector3(
            center.x + offset.x, center.y + offset.y, -0.0024f);
        if (colorMarkerFill != null)
        {
            colorMarkerFill.material.color = color;
            if (colorMarkerFill.material.HasProperty("_BaseColor"))
                colorMarkerFill.material.SetColor("_BaseColor", color);
        }
    }

    /// <summary>Converts design UVs (center) to local position on the panel.</summary>
    private static Vector3 UvToLocal(float u, float v, float w, float h)
    {
        return new Vector3((u - 0.5f) * w, (v - 0.5f) * h, 0f);
    }

    private GameObject MakeQuad(string name, Vector3 localPos, Vector2 size)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(menuRoot.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        Destroy(go.GetComponent<Collider>());
        Renderer r = go.GetComponent<Renderer>();
        r.material = new Material(r.sharedMaterial);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    /// <summary>Best-effort switch of a URP Lit material to transparent.</summary>
    private static void TryMakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }
}

#endif