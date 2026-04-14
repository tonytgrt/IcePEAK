using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Attach to an ice cube GameObject (alongside SurfaceTag set to Ice).
/// When an IcePickController embeds into this cube, spawns a URP DecalProjector
/// showing a crack that progresses through a sprite-sheet atlas over breakTime,
/// then shatters the cube.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BreakableIce : MonoBehaviour
{
    [Header("Break Settings")]
    [Tooltip("Seconds between embed and shatter.")]
    [SerializeField] private float breakTime = 2f;

    [Header("Decal")]
    [Tooltip("Material using Shader Graphs/Decal, with the crack sprite sheet in Base Map.")]
    [SerializeField] private Material crackDecalMaterial;

    [Tooltip("Size of the decal projector box (X, Y = footprint, Z = projection depth).")]
    [SerializeField] private Vector3 decalSize = new Vector3(0.3f, 0.3f, 0.5f);

    [Header("Sprite Sheet")]
    [Tooltip("Number of columns in the crack sprite sheet.")]
    [SerializeField] private int sheetColumns = 4;
    [Tooltip("Number of rows in the crack sprite sheet.")]
    [SerializeField] private int sheetRows = 4;
    [Tooltip("Play cells in this order (index = col + row*columns). Leave empty to walk 0..N-1.")]
    [SerializeField] private int[] frameOrder;

    [Header("Shatter")]
    [Tooltip("Optional prefab spawned on shatter (VFX / shattered mesh).")]
    [SerializeField] private GameObject shatterPrefab;

    private readonly List<CrackInstance> _cracks = new();
    private bool _isBroken;

    private class CrackInstance
    {
        public DecalProjector projector;
        public Material materialInstance;
        public float timer;
    }

    private void OnEnable()
    {
        // Subscribe to every pick controller in the scene.
        // (For scenes where picks are spawned later, consider a static event bus.)
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

        // Only react if the embed was on THIS cube.
        if (surface == null || surface.gameObject != gameObject &&
            surface.transform.root != transform.root)
            return;

        SpawnCrackDecal(pick.EmbedWorldPosition, -pick.transform.forward);
    }

    private void SpawnCrackDecal(Vector3 worldPos, Vector3 projectDir)
    {
        if (crackDecalMaterial == null)
        {
            Debug.LogWarning("[BreakableIce] No crackDecalMaterial assigned.");
            return;
        }

        // Create a child GameObject with a DecalProjector that points INTO the surface.
        var go = new GameObject("CrackDecal");
        go.transform.SetParent(transform, worldPositionStays: true);
        go.transform.position = worldPos - projectDir * (decalSize.z * 0.5f);
        go.transform.rotation = Quaternion.LookRotation(projectDir);

        var projector = go.AddComponent<DecalProjector>();
        projector.material = new Material(crackDecalMaterial); // instance so we can tweak UV/alpha per-decal
        projector.size = decalSize;
        projector.pivot = Vector3.zero;

        _cracks.Add(new CrackInstance
        {
            projector = projector,
            materialInstance = projector.material,
            timer = 0f,
        });

        // Set initial frame (frame 0).
        SetFrame(projector.material, 0);
    }

    private void Update()
    {
        if (_isBroken || _cracks.Count == 0) return;

        int totalFrames = frameOrder != null && frameOrder.Length > 0
            ? frameOrder.Length
            : sheetColumns * sheetRows;

        bool anyReady = false;

        foreach (var crack in _cracks)
        {
            crack.timer += Time.deltaTime;
            float t = Mathf.Clamp01(crack.timer / breakTime);

            // Pick the sprite cell based on progress.
            int step = Mathf.Min(totalFrames - 1, Mathf.FloorToInt(t * totalFrames));
            int cellIndex = frameOrder != null && frameOrder.Length > 0
                ? frameOrder[step]
                : step;

            SetFrame(crack.materialInstance, cellIndex);

            if (crack.timer >= breakTime) anyReady = true;
        }

        if (anyReady) Shatter();
    }

    private void SetFrame(Material mat, int cellIndex)
    {
        int col = cellIndex % sheetColumns;
        int row = cellIndex / sheetColumns;

        Vector2 tiling = new Vector2(1f / sheetColumns, 1f / sheetRows);
        // URP Decal Shader Graph samples from the bottom-left, so flip row.
        Vector2 offset = new Vector2(col * tiling.x, (sheetRows - 1 - row) * tiling.y);

        mat.SetVector("_BaseMap_ST", new Vector4(tiling.x, tiling.y, offset.x, offset.y));
        mat.mainTextureScale = tiling;
        mat.mainTextureOffset = offset;
    }

    private void Shatter()
    {
        if (_isBroken) return;
        _isBroken = true;

        if (shatterPrefab != null)
            Instantiate(shatterPrefab, transform.position, transform.rotation);

        // Release any picks that were embedded in us (optional — requires tracking).
        Destroy(gameObject);
    }
}
