using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// DECAL-BASED variant of BreakableIce. Spawns a URP DecalProjector at the strike
/// point, which projects the crack sprite onto any opaque surface within its box.
///
/// Use this variant on cubes whose material is OPAQUE and whose URP renderer has
/// the Decal Renderer Feature enabled (Screen Space technique for Quest).
/// Compare visually against BreakableIce (quad overlay) by putting one variant on
/// each test cube.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BreakableIceDecal : MonoBehaviour
{
    [Header("Break Settings")]
    [Tooltip("Seconds between embed and shatter.")]
    [SerializeField] private float breakTime = 2f;

    [Header("Decal")]
    [Tooltip("Material using Shader Graphs/Decal, with the crack sprite sheet in Base Map.")]
    [SerializeField] private Material crackDecalMaterial;

    [Tooltip("Size of the decal projector box (X, Y = footprint on surface, Z = projection depth).")]
    [SerializeField] private Vector3 decalSize = new Vector3(0.3f, 0.3f, 0.5f);

    [Header("Sprite Sheet")]
    [SerializeField] private int sheetColumns = 2;
    [SerializeField] private int sheetRows = 2;
    [Tooltip("Play cells in this order. Leave empty to walk 0..N-1.")]
    [SerializeField] private int[] frameOrder;

    [Header("Shatter")]
    [SerializeField] private GameObject shatterPrefab;

    private readonly List<CrackInstance> _cracks = new();
    private bool _isBroken;

    private class CrackInstance
    {
        public DecalProjector projector;
        public Material materialInstance;
        public float timer;
        public int lastCellIndex;
    }

    private void OnEnable()
    {
        foreach (var pick in FindObjectsByType<IcePickController>(FindObjectsSortMode.None))
            pick.OnEmbedded += HandlePickEmbedded;
    }

    private void OnDisable()
    {
        foreach (var pick in FindObjectsByType<IcePickController>(FindObjectsSortMode.None))
            pick.OnEmbedded -= HandlePickEmbedded;
    }

    private void HandlePickEmbedded(IcePickController pick, SurfaceTag surface)
    {
        if (_isBroken) return;

        if (surface == null || surface.gameObject != gameObject &&
            surface.transform.root != transform.root)
            return;

        SnapToNearestFace(pick.EmbedWorldPosition, out Vector3 surfacePoint, out Vector3 surfaceNormal);
        SpawnCrackDecal(surfacePoint, surfaceNormal);
    }

    private void SnapToNearestFace(Vector3 worldPoint, out Vector3 surfacePoint, out Vector3 outwardNormal)
    {
        var col = GetComponent<Collider>();
        Bounds b = col != null ? col.bounds : new Bounds(transform.position, Vector3.one);

        Vector3 local = worldPoint - b.center;
        Vector3 ext = b.extents;

        float fx = ext.x > 0 ? local.x / ext.x : 0f;
        float fy = ext.y > 0 ? local.y / ext.y : 0f;
        float fz = ext.z > 0 ? local.z / ext.z : 0f;

        float ax = Mathf.Abs(fx);
        float ay = Mathf.Abs(fy);
        float az = Mathf.Abs(fz);

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

    private void SpawnCrackDecal(Vector3 worldPos, Vector3 surfaceNormal)
    {
        if (crackDecalMaterial == null)
        {
            Debug.LogWarning("[BreakableIceDecal] No crackDecalMaterial assigned.");
            return;
        }

        // Projector position = hit point on surface. Local +Z aligns with the INWARD
        // normal, so the decal projects INTO the cube. With pivot=zero the projection
        // box is centered here; the surface crosses the middle of the box so fragments
        // on the cube's face receive the decal.
        var go = new GameObject("CrackDecal");
        go.transform.SetParent(transform, worldPositionStays: true);
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.LookRotation(-surfaceNormal);

        var projector = go.AddComponent<DecalProjector>();
        projector.material = new Material(crackDecalMaterial);
        projector.size = decalSize;
        projector.pivot = Vector3.zero;

        var instance = new CrackInstance
        {
            projector = projector,
            materialInstance = projector.material,
            timer = 0f,
            lastCellIndex = -1,
        };
        _cracks.Add(instance);

        SetFrame(instance, 0);
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
                crack.timer += Time.deltaTime;
                float t = Mathf.Clamp01(crack.timer / breakTime);

                int step = Mathf.Min(totalFrames - 1, Mathf.FloorToInt(t * totalFrames));
                int cellIndex = frameOrder != null && frameOrder.Length > 0
                    ? frameOrder[step]
                    : step;

                SetFrame(crack, cellIndex);

                if (crack.timer >= breakTime) anyReady = true;
            }

            if (anyReady) Shatter();
        }

        Update_DebugKeys();
    }

    private void SetFrame(CrackInstance crack, int cellIndex)
    {
        if (crack.lastCellIndex == cellIndex) return; // skip redundant writes
        crack.lastCellIndex = cellIndex;

        int col = cellIndex % sheetColumns;
        int row = cellIndex / sheetColumns;

        Vector2 tiling = new Vector2(1f / sheetColumns, 1f / sheetRows);
        // Unity UVs originate at bottom-left; flip row so cell (0,0) is top-left of the sheet.
        Vector2 offset = new Vector2(col * tiling.x, (sheetRows - 1 - row) * tiling.y);

        // URP's Shader Graphs/Decal ignores material _BaseMap_ST — the DecalProjector
        // component itself controls which sub-rect of the texture is projected.
        crack.projector.uvScale = tiling;
        crack.projector.uvBias = offset;

        // Also set material properties as a fallback for custom decal shaders.
        crack.materialInstance.mainTextureScale = tiling;
        crack.materialInstance.mainTextureOffset = offset;
        if (crack.materialInstance.HasProperty("_BaseMap_ST"))
            crack.materialInstance.SetVector("_BaseMap_ST", new Vector4(tiling.x, tiling.y, offset.x, offset.y));
    }

    // --- Debug / Editor testing ---

    [Header("Debug")]
    [SerializeField] private Vector3 debugHitLocalPoint = Vector3.zero;
    [SerializeField] private Vector3 debugSurfaceNormal = Vector3.forward;

    [ContextMenu("TEST: Simulate Strike")]
    private void DebugSimulateStrike()
    {
        Vector3 surfaceNormal = transform.TransformDirection(debugSurfaceNormal).normalized;

        var col = GetComponent<Collider>();
        Vector3 center = col != null ? col.bounds.center : transform.position;
        float pushOut = col != null
            ? Vector3.Dot(col.bounds.extents,
                new Vector3(Mathf.Abs(surfaceNormal.x), Mathf.Abs(surfaceNormal.y), Mathf.Abs(surfaceNormal.z)))
            : 0.5f;

        Vector3 worldPos = center + surfaceNormal * pushOut;
        SpawnCrackDecal(worldPos, surfaceNormal);
    }

    [ContextMenu("TEST: Shatter Now")]
    private void DebugShatterNow() => Shatter();

    private void Update_DebugKeys()
    {
        if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
        {
            DebugSimulateStrike();
        }
    }

    private void Shatter()
    {
        if (_isBroken) return;
        _isBroken = true;

        if (shatterPrefab != null)
            Instantiate(shatterPrefab, transform.position, transform.rotation);

        Destroy(gameObject);
    }
}
