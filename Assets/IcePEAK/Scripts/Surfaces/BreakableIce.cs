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

    [Tooltip("Grid subdivisions per axis used when conforming the crack mesh to the " +
             "collider surface. Higher = smoother wrap around edges/corners.")]
    [SerializeField] private int tessellation = 12;

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
    private static Dictionary<IcePickController, BreakableIce> _pickEmbedMap = new();

    private class CrackInstance
    {
        public GameObject quad;
        public Material materialInstance;
        public float timer;
        public IcePickController pick;
        public bool isActive;
    }

    private void OnEnable()
    {
        foreach (var pick in FindObjectsByType<IcePickController>(FindObjectsSortMode.None))
        {
            pick.OnEmbedded += HandlePickEmbedded;
            pick.OnReleased += HandlePickReleased;
        }
    }

    private void OnDisable()
    {
        foreach (var pick in FindObjectsByType<IcePickController>(FindObjectsSortMode.None))
        {
            pick.OnEmbedded -= HandlePickEmbedded;
            pick.OnReleased -= HandlePickReleased;
        }
    }

    private void HandlePickEmbedded(IcePickController pick, SurfaceTag surface)
    {
        if (_isBroken) return;

        if (surface == null || surface.gameObject != gameObject)
            return;

        _pickEmbedMap[pick] = this;

        // Snap the embed point to the nearest face of the cube's bounds.
        // This works reliably regardless of pick orientation or embed depth.
        SnapToNearestFace(pick.EmbedWorldPosition, out Vector3 surfacePoint, out Vector3 surfaceNormal);
        SpawnCrackQuad(surfacePoint, surfaceNormal, pick);
    }

    private void HandlePickReleased(IcePickController pick)
    {
        _pickEmbedMap.Remove(pick);
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
    /// Spawns a crack mesh that conforms to the collider surface. Vertices are projected
    /// onto the collider via Collider.ClosestPoint, so the mesh folds around edges/corners
    /// instead of extending into empty space past the face.
    /// </summary>
    private void SpawnCrackQuad(Vector3 worldPos, Vector3 surfaceNormal, IcePickController pick = null)
    {
        if (crackMaterial == null)
        {
            Debug.LogWarning("[BreakableIce] No crackMaterial assigned.");
            return;
        }

        var go = new GameObject("CrackMesh");
        go.transform.SetParent(transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildConformingMesh(worldPos, surfaceNormal.normalized);

        var renderer = go.AddComponent<MeshRenderer>();
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
            isActive = true,
        });

        SetFrame(matInstance, 0);
    }

    /// <summary>
    /// Builds a tessellated grid in the surface plane, then snaps each vertex onto the
    /// collider surface. Points inside the face bounds land flat on the face; points
    /// past an edge get wrapped onto the neighbouring face via a raycast toward the
    /// collider centre. Works for any collider whose geometry supports Raycast
    /// (Box/Sphere/Capsule/Mesh); falls back to ClosestPoint clamp if both probes miss.
    /// </summary>
    private Mesh BuildConformingMesh(Vector3 hitCenterWorld, Vector3 surfaceNormal)
    {
        var col = GetComponent<Collider>();
        if (col == null) return null;

        Vector3 tangent = Vector3.Cross(surfaceNormal, Vector3.up);
        if (tangent.sqrMagnitude < 1e-4f) tangent = Vector3.Cross(surfaceNormal, Vector3.right);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(surfaceNormal, tangent).normalized;

        Vector3 colliderCenter = col.bounds.center;
        float probeHeight = Mathf.Max(surfaceOffset, 0.005f);
        float maxRayDist = col.bounds.size.magnitude * 3f + 1f;

        int n = Mathf.Max(1, tessellation);
        int sideVerts = n + 1;
        var verts = new Vector3[sideVerts * sideVerts];
        var uvs = new Vector2[sideVerts * sideVerts];
        var tris = new int[n * n * 6];

        for (int y = 0; y < sideVerts; y++)
        {
            for (int x = 0; x < sideVerts; x++)
            {
                float u = x / (float)n;
                float v = y / (float)n;
                Vector3 flatCenter = hitCenterWorld
                                     + tangent * ((u - 0.5f) * crackSize.x)
                                     + bitangent * ((v - 0.5f) * crackSize.y);

                SnapToSurface(col, colliderCenter, flatCenter, surfaceNormal,
                              probeHeight, maxRayDist,
                              out Vector3 snapped, out Vector3 outward);

                Vector3 world = snapped + outward * surfaceOffset;
                verts[y * sideVerts + x] = transform.InverseTransformPoint(world);
                uvs[y * sideVerts + x] = new Vector2(u, v);
            }
        }

        int ti = 0;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int i0 = y * sideVerts + x;
                int i1 = i0 + 1;
                int i2 = i0 + sideVerts;
                int i3 = i2 + 1;
                tris[ti++] = i0; tris[ti++] = i1; tris[ti++] = i2;
                tris[ti++] = i1; tris[ti++] = i3; tris[ti++] = i2;
            }
        }

        var m = new Mesh { name = "CrackConformingMesh" };
        m.vertices = verts;
        m.uv = uvs;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>
    /// Two-stage projection. Stage 1: cast straight down onto the original face plane —
    /// gives an exact landing on flat regions. Stage 2: if (1) misses, the grid point is
    /// past an edge, so cast from the same origin toward the collider centre; the ray
    /// enters the solid through the neighbouring face, which is the wrapped position.
    /// </summary>
    private static void SnapToSurface(Collider col, Vector3 colliderCenter, Vector3 flatCenter,
        Vector3 surfaceNormal, float probeHeight, float maxRayDist,
        out Vector3 point, out Vector3 normal)
    {
        Vector3 probeOrigin = flatCenter + surfaceNormal * probeHeight;

        if (col.Raycast(new Ray(probeOrigin, -surfaceNormal), out RaycastHit downHit,
                        probeHeight * 2f + 1f))
        {
            point = downHit.point;
            normal = downHit.normal;
            return;
        }

        Vector3 toCenter = colliderCenter - probeOrigin;
        if (toCenter.sqrMagnitude > 1e-6f &&
            col.Raycast(new Ray(probeOrigin, toCenter.normalized), out RaycastHit wrapHit,
                        maxRayDist))
        {
            point = wrapHit.point;
            normal = wrapHit.normal;
            return;
        }

        // Degenerate fallback: clamp to nearest surface point (old non-wrapping behaviour).
        Vector3 farProbe = flatCenter + surfaceNormal * (col.bounds.size.magnitude * 2f + 1f);
        point = col.ClosestPoint(farProbe);
        normal = point - colliderCenter;
        if (normal.sqrMagnitude < 1e-6f) normal = surfaceNormal;
        else normal.Normalize();
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
                // Only advance timer if this pick is currently embedded in THIS cube
                if (crack.pick == null || !_pickEmbedMap.TryGetValue(crack.pick, out var embeddedIn) || embeddedIn != this)
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
        {
            var debris = Instantiate(shatterPrefab, transform.position, transform.rotation);
            debris.AddComponent<ShatterDebris>();
        }

        Destroy(gameObject);
    }
}
