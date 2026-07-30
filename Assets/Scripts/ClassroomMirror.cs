using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// ClassroomMirror.cs v2.3 - ClassroomXR milestone 6, Phase B.
/// Attach to a QUAD on a wall. Live mirror reflection.
/// Portal technique (Kooima off-axis projection), no custom shaders.
/// v2: 24-bit depth RT, explicit ARGB32 + sRGB, MSAA on RT.
/// v2.1: no URP namespace dependency.
/// v2.2: near clip floored at MinNearClip (depth precision when very close).
/// v2.3: GL.invertCulling around the mirror camera's render.
///       The view matrix built below is a MIRRORED basis - row 0 is vr while
///       the camera's actual right vector is -vr, giving a negative
///       determinant. That is deliberate (it makes a true mirror without
///       flipping the texture) but it reverses triangle winding, so without
///       inverted culling every front face is culled and back faces are
///       drawn. On box geometry the silhouette is unchanged, which is why
///       this hid for several sessions - the visible symptoms were interior
///       geometry (leg caps inside the torso) showing through, plus a flat,
///       washed-out look from seeing rear interior surfaces.
///       NOTE: UnityEngine.Rendering is CORE engine, not the Universal
///       package that caused the CS0234 in v2.0. Safe to import.
/// Setup: material asset Assets/Resources/MirrorMat (URP/Unlit),
/// layer LocalMirror for own reflection.
/// </summary>
public class ClassroomMirror : MonoBehaviour
{
    [Tooltip("Reflection texture resolution, square. 1024 sharp, 512 for Quest perf.")]
    [SerializeField] private int textureSize = 1024;

    [Tooltip("Flip image horizontally. Leave OFF - portal math already gives a true mirror.")]
    [SerializeField] private bool flipHorizontal = false;

    [Tooltip("Layers visible in the mirror.")]
    [SerializeField] private LayerMask reflectLayers = ~0;

    [Tooltip("Render every Nth frame. 1 = every frame.")]
    [SerializeField] private int updateInterval = 1;

    [Tooltip("Far clip distance for the mirror camera.")]
    [SerializeField] private float farClip = 60f;

    [Tooltip("MSAA samples on the RT: 1, 2, 4, 8. Drop to 1 for Quest perf.")]
    [SerializeField] private int antiAliasing = 4;

    // NOT serialized fields on purpose: adding one would deserialize as 0 or
    // false on the existing component instance and silently undo the fix.
    private const float MinNearClip = 0.3f;
    private const bool InvertCulling = true;

    private Camera mirrorCam;
    private RenderTexture rt;
    private Renderer quadRenderer;

    private const int WaterLayer = 4;

    private void OnEnable()
    {
        quadRenderer = GetComponent<Renderer>();
        if (quadRenderer == null)
        {
            Debug.LogError("[Mirror] Needs a Renderer (put this on a Quad).");
            enabled = false;
            return;
        }

        gameObject.layer = WaterLayer;

        rt = new RenderTexture(textureSize, textureSize, 24,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        rt.name = "MirrorRT";
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.filterMode = FilterMode.Bilinear;
        rt.antiAliasing = Mathf.Clamp(antiAliasing, 1, 8);

        Material template = Resources.Load<Material>("MirrorMat");
        Material m;
        if (template != null)
        {
            m = new Material(template);
        }
        else
        {
            Debug.LogWarning("[Mirror] Resources/MirrorMat not found - editor fallback, may strip in WebGL.");
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            m = new Material(s != null ? s : Shader.Find("Unlit/Texture"));
        }
        m.mainTexture = rt;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", rt);
        m.mainTextureScale  = flipHorizontal ? new Vector2(-1f, 1f) : Vector2.one;
        m.mainTextureOffset = flipHorizontal ? new Vector2(1f, 0f)  : Vector2.zero;
        quadRenderer.material = m;

        GameObject camGo = new GameObject("MirrorCamera (auto)");
        camGo.transform.SetParent(transform, false);
        mirrorCam = camGo.AddComponent<Camera>();
        mirrorCam.targetTexture = rt;
        mirrorCam.cullingMask = reflectLayers & ~(1 << WaterLayer);
        mirrorCam.clearFlags = CameraClearFlags.Skybox;
        mirrorCam.farClipPlane = farClip;
        mirrorCam.depth = -10f;
        mirrorCam.enabled = true;

        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
        GL.invertCulling = false;

        if (mirrorCam != null) Destroy(mirrorCam.gameObject);
        if (rt != null) { rt.Release(); Destroy(rt); }
    }

    private void HandleBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (InvertCulling && cam == mirrorCam) GL.invertCulling = true;
    }

    private void HandleEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam == mirrorCam) GL.invertCulling = false;
    }

    private void LateUpdate()
    {
        Camera eyeCam = Camera.main;
        if (eyeCam == null || mirrorCam == null) return;

        if (updateInterval > 1)
        {
            mirrorCam.enabled = (Time.frameCount % updateInterval) == 0;
            if (!mirrorCam.enabled) return;
        }

        Vector3 eye = eyeCam.transform.position;
        Vector3 pos = transform.position;

        Vector3 vr = transform.right;
        Vector3 vu = transform.up;
        Vector3 vn = -transform.forward;
        if (Vector3.Dot(eye - pos, vn) < 0f)
        {
            vn = -vn;
            vr = -vr;
        }

        float distToPlane = Vector3.Dot(eye - pos, vn);
        Vector3 pe = eye - 2f * distToPlane * vn;

        float halfW = 0.5f * transform.lossyScale.x;
        float halfH = 0.5f * transform.lossyScale.y;
        Vector3 pa = pos - vr * halfW - vu * halfH;
        Vector3 pb = pos + vr * halfW - vu * halfH;
        Vector3 pc = pos - vr * halfW + vu * halfH;

        Vector3 va = pa - pe;
        Vector3 vb = pb - pe;
        Vector3 vc = pc - pe;
        Vector3 n2 = Vector3.Cross(vr, vu).normalized;
        float d = -Vector3.Dot(n2, va);
        if (d < 0.01f) d = 0.01f;

        // v2.2: floor the near plane. Extents below are scaled by (nearClip / d),
        // so the framing is unchanged - only depth precision improves.
        float nearClip = Mathf.Max(d, MinNearClip);
        float l = Vector3.Dot(vr, va) * nearClip / d;
        float r = Vector3.Dot(vr, vb) * nearClip / d;
        float b = Vector3.Dot(vu, va) * nearClip / d;
        float t = Vector3.Dot(vu, vc) * nearClip / d;

        mirrorCam.transform.SetPositionAndRotation(
            pe, Quaternion.LookRotation(-n2, vu));
        Matrix4x4 world2cam = Matrix4x4.identity;
        world2cam.SetRow(0, new Vector4(vr.x, vr.y, vr.z, -Vector3.Dot(vr, pe)));
        world2cam.SetRow(1, new Vector4(vu.x, vu.y, vu.z, -Vector3.Dot(vu, pe)));
        world2cam.SetRow(2, new Vector4(n2.x, n2.y, n2.z, -Vector3.Dot(n2, pe)));
        world2cam.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
        mirrorCam.worldToCameraMatrix = world2cam;
        mirrorCam.projectionMatrix = Matrix4x4.Frustum(
            l, r, b, t, nearClip, farClip);
        mirrorCam.nearClipPlane = nearClip;
    }
}
