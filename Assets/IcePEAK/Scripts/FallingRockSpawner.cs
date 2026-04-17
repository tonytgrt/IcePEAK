using UnityEngine;

/// <summary>
/// Periodically spawns rock prefabs inside a box volume above the player / cliff.
/// Each rock is given a Rigidbody (added if missing) so gravity pulls it down,
/// with optional random rotation/tangential push. Rocks self-destruct after
/// <see cref="rockLifetime"/> seconds.
///
/// Place this GameObject near the top of the cliff; use the Scene-view gizmo
/// (yellow wire cube) to size the spawn volume.
/// </summary>
public class FallingRockSpawner : MonoBehaviour
{
    [Header("Rock Prefabs")]
    [Tooltip("One is picked at random each spawn. Each should have (or will be given) a Rigidbody and Collider.")]
    [SerializeField] private GameObject[] rockPrefabs;

    [Header("Spawn Volume (local space, centered on this transform)")]
    [Tooltip("Size of the box the rocks spawn inside. Y is vertical thickness of the spawn band.")]
    [SerializeField] private Vector3 spawnAreaSize = new Vector3(10f, 1f, 10f);

    [Header("Timing")]
    [Tooltip("Minimum seconds between spawns.")]
    [SerializeField] private float minInterval = 1.5f;
    [Tooltip("Maximum seconds between spawns.")]
    [SerializeField] private float maxInterval = 4f;
    [Tooltip("Seconds before the first rock spawns after enable.")]
    [SerializeField] private float startDelay = 2f;

    [Header("Rock Physics")]
    [Tooltip("Uniform scale range applied to each spawned rock.")]
    [SerializeField] private Vector2 scaleRange = new Vector2(0.3f, 0.8f);
    [Tooltip("Extra downward speed added on spawn (m/s).")]
    [SerializeField] private float initialDownwardSpeed = 0f;
    [Tooltip("Random horizontal nudge applied on spawn (m/s).")]
    [SerializeField] private float horizontalJitter = 0.5f;
    [Tooltip("Mass assigned if the prefab has no Rigidbody.")]
    [SerializeField] private float defaultMass = 5f;

    [Header("Cleanup")]
    [Tooltip("Destroy each rock after this many seconds.")]
    [SerializeField] private float rockLifetime = 10f;

    [Header("Gizmo")]
    [SerializeField] private Color gizmoColor = new Color(1f, 0.85f, 0.1f, 0.5f);

    private float _nextSpawnTime;

    private void OnEnable()
    {
        _nextSpawnTime = Time.time + startDelay;
    }

    private void Update()
    {
        if (Time.time < _nextSpawnTime) return;
        if (rockPrefabs == null || rockPrefabs.Length == 0) return;

        SpawnRock();
        _nextSpawnTime = Time.time + Random.Range(minInterval, maxInterval);
    }

    private void SpawnRock()
    {
        GameObject prefab = rockPrefabs[Random.Range(0, rockPrefabs.Length)];
        if (prefab == null) return;

        Vector3 localPos = new Vector3(
            Random.Range(-spawnAreaSize.x, spawnAreaSize.x) * 0.5f,
            Random.Range(-spawnAreaSize.y, spawnAreaSize.y) * 0.5f,
            Random.Range(-spawnAreaSize.z, spawnAreaSize.z) * 0.5f);

        Vector3 worldPos = transform.TransformPoint(localPos);
        Quaternion randomRot = Random.rotation;

        GameObject rock = Instantiate(prefab, worldPos, randomRot);

        float scale = Random.Range(scaleRange.x, scaleRange.y);
        rock.transform.localScale *= scale;

        if (!rock.TryGetComponent<Rigidbody>(out var rb))
        {
            rb = rock.AddComponent<Rigidbody>();
            rb.mass = defaultMass;
        }
        rb.useGravity = true;
        rb.isKinematic = false;

        Vector3 velocity = Vector3.down * initialDownwardSpeed;
        velocity += new Vector3(
            Random.Range(-horizontalJitter, horizontalJitter),
            0f,
            Random.Range(-horizontalJitter, horizontalJitter));
        rb.linearVelocity = velocity;
        rb.angularVelocity = Random.insideUnitSphere * 2f;

        Destroy(rock, rockLifetime);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = gizmoColor;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, spawnAreaSize);
        Gizmos.DrawLine(Vector3.zero, Vector3.down * 5f);
    }
}
