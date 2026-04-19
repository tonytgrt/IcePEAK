using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to an ice cube GameObject (alongside SurfaceTag set to Ice).
/// When an IcePickController embeds into this cube, spawns a thin QUAD hovering
/// just above the surface, textured with a crack sprite sheet. Cycles through
/// sprite frames over breakTime seconds, then shatters the cube.
///
/// Uses a world-space quad (not URP DecalProjector) so it works on TRANSPARENT
/// materials like ice, which URP decals cannot project onto.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BreakableIce : MonoBehaviour
{
    [Header("Break Settings")]
    [Tooltip("Seconds between embed and shatter.")]
    [SerializeField] private float breakTime = 2f;

    [Header("Crack Quad")]
    [Tooltip("Material for the crack overlay. Use a Universal Render Pipeline/Unlit shader " +
             "set to Surface Type = Transparent, with the crack sprite sheet as Base Map.")]
    [SerializeField] private Material crackMaterial;

    [Tooltip("Size of the crack quad in world units (X = width, Y = height).")]
    [SerializeField] private Vector2 crackSize = new Vector2(0.3f, 0.3f);

    [Tooltip("How far above the ice surface the quad hovers, in meters. " +
             "Small positive value prevents z-fighting with the ice face.")]
    [SerializeField] private float surfaceOffset = 0.005f;

    [Header("Sprite Sheet")]
    [Tooltip("Number of columns in the crack sprite sheet.")]
    [SerializeField] private int sheetColumns = 2;
    [Tooltip("Number of rows in the crack sprite sheet.")]
    [SerializeField] private int sheetRows = 2;
    [Tooltip("Play cells in this order (index = col + row*columns). Leave empty to walk 0..N-1.")]
    [SerializeField] private int[] frameOrder;

    [Header("Shatter")]
    [Tooltip("Optional prefab spawned on shatter (VFX / shattered mesh).")]
    [SerializeField] private GameObject shatterPrefab;

    private readonly List<CrackInstance> _cracks = new();
    private bool _isBroken;

    private class CrackInstance
    {
        public GameObject quad;
        public Material materialInstance;
        public float timer;
        public IcePickController pick;
    }

    private void OnEnable()
    {
        foreach (var pick in FindObjectsByType<IcePickController>(FindObjectsSortMode.None))
        {
            pick.OnEmbedded += HandlePickEmbedded;
        }
    }

    private void OnDisable()
    {
        foreach (var pick in FindObjectsByType<IcePickController>(FindObjectsSortMode.None))
        {
            pick.OnEmbedded -= HandlePickEmbedded;
        }
    }

    private void HandlePickEmbedded(IcePickController pick, SurfaceTag surface)
    {
        if (_isBroken) return;

        if (surface == null || surface.gameObject != gameObject &&
            surface.transform.root != transform.root)
            return;

        // Snap the embed point to the nearest face of the cube's bounds.
        // This works reliably regardless of pick orientation or embed depth.
        SnapToNearestFace(pick.EmbedWorldPosition, out Vector3 surfacePoint, out Vector3 surfaceNormal);
        SpawnCrackQuad(surfacePoint, surfaceNormal, pick);
    }

    /// <summary>
    /// Given a point (typically inside or near the cube), finds the closest face of
    /// the collider bounds, returns the projected point on that face and its outward normal.
    /// </summary>
    private void SnapToNearestFace(Vector3 worldPoint, out Vector3 surfacePoint, out Vector3 outwardNormal)
    {
        var col = GetComponent<Collider>();
        Bounds b = col != null ? col.bounds : new Bounds(transform.position, Vector3.one);

        Vector3 local = worldPoint - b.center;
        Vector3 ext = b.extents;

        // Normalized distance toward each face (-1..1 inside the box). Largest abs value
        // indicates the dominant axis — i.e., the face the point is closest to.
        float fx = ext.x > 0 ? local.x / ext.x : 0f;
        float fy = ext.y > 0 ? local.y / ext.y : 0f;
        float fz = ext.z > 0 ? local.z / ext.z : 0f;

        float ax = Mathf.Abs(fx);
        float ay = Mathf.Abs(fy);
        float az = Mathf.Abs(fz);

        outwardNormal = Vector3.forward;
        surfacePoint = worldPoint;

        if (ax >= ay && ax >= az)
        {
            outwardNormal = new Vector3(Mathf.Sign(fx == 0 ? 1 : fx), 0, 0);
            surfacePoint = new Vector3(b.center.x + outwardNormal.x * ext.x, worldPoint.y, worldPoint.z);
        }
        else if (ay >= ax && ay >= az)
        {
            outwardNormal = new Vector3(0, Mathf.Sign(fy == 0 ? 1 : fy), 0);
            surfacePoint = new Vector3(worldPoint.x, b.center.y + outwardNormal.y * ext.y, worldPoint.z);
        }
        else
        {
            outwardNormal = new Vector3(0, 0, Mathf.Sign(fz == 0 ? 1 : fz));
            surfacePoint = new Vector3(worldPoint.x, worldPoint.y, b.center.z + outwardNormal.z * ext.z);
        }
    }

    /// <summary>
    /// Spawns a textured quad facing along surfaceNormal, hovering just above the hit point.
    /// </summary>
    private void SpawnCrackQuad(Vector3 worldPos, Vector3 surfaceNormal, IcePickController pick = null)
    {
        if (crackMaterial == null)
        {
            Debug.LogWarning("[BreakableIce] No crackMaterial assigned.");
            return;
        }

        // Unity's built-in Quad faces +Z. Orient it so +Z aligns with the surface normal
        // (pointing OUTWARD), so the textured front faces the viewer.
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "CrackQuad";
        Destroy(go.GetComponent<Collider>()); // don't interfere with physics

        go.transform.SetParent(transform, worldPositionStays: true);
        go.transform.position = worldPos + surfaceNormal.normalized * surfaceOffset;
        go.transform.rotation = Quaternion.LookRotation(-surfaceNormal); // quad's -Z faces surface, +Z faces viewer
        go.transform.localScale = new Vector3(crackSize.x, crackSize.y, 1f);

        var renderer = go.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var matInstance = new Material(crackMaterial);
        renderer.material = matInstance;

        _cracks.Add(new CrackInstance
        {
            quad = go,
            materialInstance = matInstance,
            timer = 0f,
            pick = pick,
        });

        SetFrame(matInstance, 0);
    }

    private void Update()
    {
        if (!_isBroken && _cracks.Count > 0)
        {
            int totalFrames = frameOrder != null && frameOrder.Length > 0
                ? frameOrder.Length
                : sheetColumns * sheetRows;

            bool anyReady = false;

            foreach (var crack in _cracks)
            {
                // Only advance the timer while a pick is still embedded in this
                // cube. Releasing the pick pauses cracking at its current frame.
                if (crack.pick != null && !crack.pick.IsEmbedded)
                    continue;

                crack.timer += Time.deltaTime;
                float t = Mathf.Clamp01(crack.timer / breakTime);

                int step = Mathf.Min(totalFrames - 1, Mathf.FloorToInt(t * totalFrames));
                int cellIndex = frameOrder != null && frameOrder.Length > 0
                    ? frameOrder[step]
                    : step;

                SetFrame(crack.materialInstance, cellIndex);

                if (crack.timer >= breakTime) anyReady = true;
            }

            if (anyReady) Shatter();
        }

        Update_DebugKeys();
    }

    private void SetFrame(Material mat, int cellIndex)
    {
        int col = cellIndex % sheetColumns;
        int row = cellIndex / sheetColumns;

        Vector2 tiling = new Vector2(1f / sheetColumns, 1f / sheetRows);
        // Unity UVs originate at bottom-left; flip row so cell (0,0) is the top-left of the sheet
        // (more intuitive for authoring). Remove the flip if your sheet is already bottom-up.
        Vector2 offset = new Vector2(col * tiling.x, (sheetRows - 1 - row) * tiling.y);

        mat.mainTextureScale = tiling;
        mat.mainTextureOffset = offset;

        // Also set URP's named property in case the shader doesn't auto-map mainTexture.
        if (mat.HasProperty("_BaseMap_ST"))
            mat.SetVector("_BaseMap_ST", new Vector4(tiling.x, tiling.y, offset.x, offset.y));
    }

    // --- Debug / Editor testing ---

    [Header("Debug")]
    [Tooltip("Simulated hit point used by the context-menu test (local space).")]
    [SerializeField] private Vector3 debugHitLocalPoint = Vector3.zero;
    [Tooltip("Surface normal for debug strikes (local space). Default = +Z face.")]
    [SerializeField] private Vector3 debugSurfaceNormal = Vector3.forward;

    [ContextMenu("TEST: Simulate Strike")]
    private void DebugSimulateStrike()
    {
        Vector3 surfaceNormal = transform.TransformDirection(debugSurfaceNormal).normalized;

        // Push from the cube center outward along the surface normal until we hit
        // the face — use the collider's world-space bounds for an accurate distance.
        var col = GetComponent<Collider>();
        Vector3 center = col != null ? col.bounds.center : transform.position;
        float pushOut = col != null
            ? Vector3.Dot(col.bounds.extents,
                new Vector3(Mathf.Abs(surfaceNormal.x), Mathf.Abs(surfaceNormal.y), Mathf.Abs(surfaceNormal.z)))
            : 0.5f;

        Vector3 worldPos = center + surfaceNormal * pushOut;
        SpawnCrackQuad(worldPos, surfaceNormal);
    }

    [ContextMenu("TEST: Shatter Now")]
    private void DebugShatterNow()
    {
        Shatter();
    }

    private void Update_DebugKeys()
    {
        if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
        {
            DebugSimulateStrike();
        }
    }

    private void Shatter()
    {
        if (_isBroken) return;
        _isBroken = true;

        // Release any picks still embedded in this cube so they return to the
        // player's hand instead of being stranded in mid-air when we destroy it.
        foreach (var crack in _cracks)
        {
            if (crack.pick != null && crack.pick.IsEmbedded)
                crack.pick.Release();
        }

        if (shatterPrefab != null)
            Instantiate(shatterPrefab, transform.position, transform.rotation);

        Destroy(gameObject);
    }
}
