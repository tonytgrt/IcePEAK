using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Snapshots every BreakableIce / BreakableIceDecal at scene start (by
/// cloning each into a disabled template container), and restores fresh
/// copies on <see cref="RespawnAll"/>. Also clears any lingering
/// <see cref="ShatterDebris"/> spawned by shatter prefabs.
/// </summary>
public class IceRespawner : MonoBehaviour
{
    private class Entry
    {
        public GameObject template;
        public Transform parent;
        public int siblingIndex;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 localScale;
        public string originalName;
    }

    private readonly List<Entry> _entries = new();
    private Transform _templateRoot;

    private void Awake()
    {
        var rootGO = new GameObject("[IceTemplates]");
        rootGO.transform.SetParent(transform, worldPositionStays: false);
        rootGO.SetActive(false); // keep templates dormant
        _templateRoot = rootGO.transform;

        foreach (var go in CollectBreakableIceInScene())
            Snapshot(go);
    }

    public void RespawnAll()
    {
        // Destroy lingering live ice blocks (skip templates under our root).
        foreach (var bi in FindObjectsByType<BreakableIce>(FindObjectsSortMode.None))
            if (!bi.transform.IsChildOf(_templateRoot))
                DestroyImmediate(bi.gameObject);

        foreach (var bid in FindObjectsByType<BreakableIceDecal>(FindObjectsSortMode.None))
            if (!bid.transform.IsChildOf(_templateRoot))
                DestroyImmediate(bid.gameObject);

        // Clear shatter debris.
        foreach (var d in FindObjectsByType<ShatterDebris>(FindObjectsSortMode.None))
            DestroyImmediate(d.gameObject);

        // Reinstate from templates.
        foreach (var e in _entries)
        {
            if (e.template == null) continue;
            // e.parent may be null for scene-root objects — Instantiate
            // accepts a null parent and places the new instance at scene root.
            var fresh = Instantiate(e.template, e.position, e.rotation, e.parent);
            fresh.transform.localScale = e.localScale;
            fresh.transform.SetSiblingIndex(e.siblingIndex);
            fresh.name = e.originalName;
            fresh.SetActive(true);
        }
    }

    private void Snapshot(GameObject original)
    {
        var t = original.transform;
        var copy = Instantiate(original, _templateRoot);
        copy.name = original.name;

        _entries.Add(new Entry
        {
            template = copy,
            parent = t.parent,
            siblingIndex = t.GetSiblingIndex(),
            position = t.position,
            rotation = t.rotation,
            localScale = t.localScale,
            originalName = original.name,
        });
    }

    private static IEnumerable<GameObject> CollectBreakableIceInScene()
    {
        var seen = new HashSet<GameObject>();
        foreach (var bi in FindObjectsByType<BreakableIce>(FindObjectsSortMode.None))
            if (seen.Add(bi.gameObject)) yield return bi.gameObject;
        foreach (var bid in FindObjectsByType<BreakableIceDecal>(FindObjectsSortMode.None))
            if (seen.Add(bid.gameObject)) yield return bid.gameObject;
    }
}
