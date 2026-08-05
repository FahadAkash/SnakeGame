using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening; // Added DOTween namespace

public class Spwanner : MonoBehaviour
{
    public enum SpawnMode { FloorSnap, FullVolume }

    [Header("Food Setup")]
    [Tooltip("Prefab spawned as normal food")]
    public GameObject foodPrefab;

    [Tooltip("Only one item (food or poison) at a time (destroys old one before spawning new)")]
    public bool singleFoodAtATime = true;

    [Header("Poison / Difficulty")]
    [Tooltip("Prefab spawned instead of food when 'poison' is rolled. Tag this differently (e.g. 'Poison') so your snake script can react to it.")]
    public GameObject poisonPrefab;

    [Tooltip("Chance (0-1) that a spawn cycle spawns poison instead of food, at the START of the game")]
    [Range(0f, 1f)] public float startPoisonChance = 0.1f;

    [Tooltip("Chance (0-1) that a spawn cycle spawns poison instead of food, once max difficulty is reached")]
    [Range(0f, 1f)] public float maxPoisonChance = 0.5f;

    [Tooltip("If true, poison chance and spawn speed both ramp up over time to make the game harder")]
    public bool enableDifficultyRamp = true;

    [Tooltip("Seconds from game start until difficulty reaches its maximum")]
    public float difficultyRampDuration = 120f;

    [Tooltip("Spawn interval bounds once max difficulty is reached (spawns get faster as this approaches)")]
    public float minSpawnTimeAtMaxDifficulty = 0.75f;
    public float maxSpawnTimeAtMaxDifficulty = 2f;

    [Header("Spawn Timing (seconds, at game start)")]
    public float minSpawnTime = 2f;
    public float maxSpawnTime = 6f;

    [Header("Spawn Mode")]
    public SpawnMode spawnMode = SpawnMode.FloorSnap;

    [Header("Spawn Area (Box)")]
    [Tooltip("Width (X), Height (Y), Depth (Z) of the search box. In FloorSnap mode, X/Z define the play area and Y defines how far down the raycast can search.")]
    public Vector3 spawnAreaSize = new Vector3(10f, 10f, 10f);

    [Tooltip("If true, box is centered on this GameObject's position")]
    public bool useLocalPosition = true;

    [Header("Floor Snap Settings")]
    [Tooltip("Layer(s) considered 'floor' for the raycast to hit")]
    public LayerMask floorLayerMask;

    [Tooltip("How high above the floor surface the item should sit (e.g. half its height)")]
    public float floorYOffset = 0.5f;

    [Tooltip("Max raycast distance when searching for the floor, starting from the top of the box")]
    public float floorRaycastDistance = 100f;

    [Header("Grid Settings (optional)")]
    [Tooltip("Snap spawned item's X/Z to a grid")]
    public bool useGrid = true;
    public float gridSize = 1f;

    [Header("Avoidance - Transform List")]
    [Tooltip("Snake body segments to avoid overlapping when spawning")]
    public List<Transform> obstaclesToAvoid = new List<Transform>();
    public float avoidRadius = 0.4f;

    [Header("Avoidance - Physics Overlap (optional)")]
    [Tooltip("Also reject spawn points that overlap colliders on this layer (walls, obstacles, snake body if it has colliders)")]
    public bool usePhysicsOverlapCheck = false;
    public LayerMask obstacleLayerMask;
    public float overlapCheckRadius = 0.4f;

    [Header("Attempts")]
    [Tooltip("How many attempts to find a free spot before giving up for this cycle")]
    public int maxSpawnAttempts = 30;

    [Header("Juicy Animations (DOTween)")]
    [Tooltip("Enable juicy DOTween spawn animations")]
    public bool useJuicySpawn = true;
    public float spawnAnimDuration = 0.5f;
    [Tooltip("Ease type for the pop-in scaling")]
    public Ease spawnScaleEase = Ease.OutBack;
    [Tooltip("How high the item bounces/jumps when it spawns (Set to 0 to disable)")]
    public float spawnJumpPower = 1.0f;
    [Tooltip("If true, the item will spin 360 degrees when spawning")]
    public bool spawnSpin = true;

    [Header("Gizmo Display")]
    public int maxGizmoGridPoints = 2000;
    public bool showFloorRaycastPreview = true;

    [Header("Debug")]
    public bool debugLogging = true;

    private GameObject currentItem;
    private Coroutine spawnRoutine;
    private float startTime;

    private Vector3 BoxCenter => useLocalPosition ? transform.position : Vector3.zero;

    /// <summary>0 at game start, 1 once difficultyRampDuration has elapsed.</summary>
    private float DifficultyProgress =>
        enableDifficultyRamp ? Mathf.Clamp01((Time.time - startTime) / Mathf.Max(0.01f, difficultyRampDuration)) : 0f;

    private float CurrentPoisonChance => Mathf.Lerp(startPoisonChance, maxPoisonChance, DifficultyProgress);
    private float CurrentMinSpawnTime => enableDifficultyRamp ? Mathf.Lerp(minSpawnTime, minSpawnTimeAtMaxDifficulty, DifficultyProgress) : minSpawnTime;
    private float CurrentMaxSpawnTime => enableDifficultyRamp ? Mathf.Lerp(maxSpawnTime, maxSpawnTimeAtMaxDifficulty, DifficultyProgress) : maxSpawnTime;

    private void OnEnable()
    {
        startTime = Time.time;

        if (debugLogging)
            Debug.Log("Spwanner: enabled. Difficulty ramp " + (enableDifficultyRamp ? "ON" : "OFF") + " over " + difficultyRampDuration + "s.");

        spawnRoutine = StartCoroutine(SpawnLoop());
    }

    private void OnDisable()
    {
        if (spawnRoutine != null)
            StopCoroutine(spawnRoutine);
    }

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            float lo = Mathf.Min(CurrentMinSpawnTime, CurrentMaxSpawnTime);
            float hi = Mathf.Max(CurrentMinSpawnTime, CurrentMaxSpawnTime);
            float waitTime = Random.Range(lo, hi);

            yield return new WaitForSeconds(waitTime);

            SpawnFood();
        }
    }

    public void SpawnFood()
    {
        if (foodPrefab == null && poisonPrefab == null)
        {
            Debug.LogWarning("Spwanner: No foodPrefab or poisonPrefab assigned.");
            return;
        }

        if (singleFoodAtATime && currentItem != null)
        {
            // If the item is currently tweening, kill its tweens before destroying it
            currentItem.transform.DOKill();
            Destroy(currentItem);
        }

        GameObject prefabToSpawn = ChooseSpawnPrefab();

        if (TryGetValidPosition(out Vector3 spawnPos))
        {
            currentItem = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);

            // Apply juicy DOTween animation if enabled
            if (useJuicySpawn)
            {
                AnimateJuicySpawn(currentItem, spawnPos);
            }

            if (debugLogging)
                Debug.Log("Spwanner: Spawned '" + prefabToSpawn.name + "' at " + spawnPos +
                    " | poisonChance=" + CurrentPoisonChance.ToString("F2") +
                    " | difficulty=" + DifficultyProgress.ToString("F2"));
        }
        else
        {
            Debug.LogWarning("Spwanner: Could not find a free spot after " + maxSpawnAttempts + " attempts.");
        }
    }

    /// <summary>
    /// Animates the spawned item with DOTween for a juicy visual effect.
    /// </summary>
    private void AnimateJuicySpawn(GameObject item, Vector3 targetPos)
    {
        Transform t = item.transform;

        // 1. Scale pop-in
        Vector3 originalScale = t.localScale;
        t.localScale = Vector3.zero;
        t.DOScale(originalScale, spawnAnimDuration).SetEase(spawnScaleEase);

        // 2. Jump/Bounce in place
        if (spawnJumpPower > 0f)
        {
            // Jumps from targetPos to targetPos, essentially doing an in-place bounce
            t.DOJump(targetPos, spawnJumpPower, 1, spawnAnimDuration);
        }

        // 3. Spin effect
        if (spawnSpin)
        {
            // Give it a random starting Y rotation for variety
            Vector3 currentEuler = t.localEulerAngles;
            t.localEulerAngles = new Vector3(currentEuler.x, Random.Range(0f, 360f), currentEuler.z);

            // Spin 360 degrees around the Y axis relatively over the duration
            t.DORotate(new Vector3(0, 360, 0), spawnAnimDuration, RotateMode.FastBeyond360)
                .SetRelative()
                .SetEase(Ease.OutCubic);
        }
    }

    /// <summary>
    /// Rolls whether this spawn cycle should be poison or food, based on
    /// the current (possibly ramped-up) poison chance. Falls back sensibly
    /// if only one of the two prefabs is assigned.
    /// </summary>
    private GameObject ChooseSpawnPrefab()
    {
        bool rollPoison = poisonPrefab != null && Random.value < CurrentPoisonChance;

        if (rollPoison)
            return poisonPrefab;

        if (foodPrefab != null)
            return foodPrefab;

        return poisonPrefab; // only poison assigned - fall back to it
    }

    private bool TryGetValidPosition(out Vector3 result)
    {
        int raycastMisses = 0;
        int obstacleBlocks = 0;

        for (int i = 0; i < maxSpawnAttempts; i++)
        {
            if (!TryGetRandomPoint(out Vector3 candidate))
            {
                raycastMisses++;
                continue;
            }

            if (!IsPositionFree(candidate))
            {
                obstacleBlocks++;
                continue;
            }

            result = candidate;
            return true;
        }

        result = Vector3.zero;

        if (debugLogging)
        {
            Debug.LogWarning("Spwanner: attempts=" + maxSpawnAttempts +
                " | raycast-missed-floor=" + raycastMisses +
                " | blocked-by-obstacle=" + obstacleBlocks);
        }

        return false;
    }

    private bool TryGetRandomPoint(out Vector3 point)
    {
        Vector3 center = BoxCenter;
        Vector3 half = spawnAreaSize / 2f;

        float x = Random.Range(center.x - half.x, center.x + half.x);
        float z = Random.Range(center.z - half.z, center.z + half.z);

        if (useGrid && gridSize > 0f)
        {
            x = Mathf.Round(x / gridSize) * gridSize;
            z = Mathf.Round(z / gridSize) * gridSize;
        }

        if (spawnMode == SpawnMode.FloorSnap)
        {
            Vector3 rayOrigin = new Vector3(x, center.y + half.y, z);

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, floorRaycastDistance, floorLayerMask))
            {
                point = hit.point + Vector3.up * floorYOffset;
                return true;
            }

            point = Vector3.zero;
            return false; // no floor found under this X/Z, caller will retry
        }
        else
        {
            float y = Random.Range(center.y - half.y, center.y + half.y);
            if (useGrid && gridSize > 0f)
                y = Mathf.Round(y / gridSize) * gridSize;

            point = new Vector3(x, y, z);
            return true;
        }
    }

    private bool IsPositionFree(Vector3 pos)
    {
        for (int i = 0; i < obstaclesToAvoid.Count; i++)
        {
            if (obstaclesToAvoid[i] == null) continue;

            if (Vector3.Distance(pos, obstaclesToAvoid[i].position) < avoidRadius)
                return false;
        }

        if (usePhysicsOverlapCheck)
        {
            if (Physics.CheckSphere(pos, overlapCheckRadius, obstacleLayerMask))
                return false;
        }

        return true;
    }

    // ---------------- Gizmos ----------------

    private void OnDrawGizmos()
    {
        Vector3 center = BoxCenter;

        // Outer wireframe box = the search volume
        Gizmos.color = new Color(0f, 1f, 0f, 0.6f);
        Gizmos.DrawWireCube(center, spawnAreaSize);
        Gizmos.color = new Color(0f, 1f, 0f, 0.04f);
        Gizmos.DrawCube(center, spawnAreaSize);

        if (spawnMode == SpawnMode.FloorSnap && showFloorRaycastPreview)
        {
            DrawFloorPreview();
        }
        else if (spawnMode == SpawnMode.FullVolume && useGrid && gridSize > 0f)
        {
            DrawGridPoints();
        }
    }

    /// <summary>
    /// In FloorSnap mode, samples a few raycasts across the box so you can
    /// see in the editor roughly where items will actually land.
    /// </summary>
    private void DrawFloorPreview()
    {
        Vector3 center = BoxCenter;
        Vector3 half = spawnAreaSize / 2f;
        int samplesPerAxis = 6;

        Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);

        for (int xi = 0; xi <= samplesPerAxis; xi++)
        {
            for (int zi = 0; zi <= samplesPerAxis; zi++)
            {
                float x = center.x - half.x + (spawnAreaSize.x / samplesPerAxis) * xi;
                float z = center.z - half.z + (spawnAreaSize.z / samplesPerAxis) * zi;
                Vector3 rayOrigin = new Vector3(x, center.y + half.y, z);

                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, floorRaycastDistance, floorLayerMask))
                {
                    Vector3 landPoint = hit.point + Vector3.up * floorYOffset;
                    Gizmos.DrawWireSphere(landPoint, Mathf.Max(gridSize * 0.1f, 0.05f));
                }
            }
        }
    }

    private void DrawGridPoints()
    {
        Vector3 center = BoxCenter;
        Vector3 half = spawnAreaSize / 2f;

        int nx = Mathf.FloorToInt(spawnAreaSize.x / gridSize) + 1;
        int ny = Mathf.FloorToInt(spawnAreaSize.y / gridSize) + 1;
        int nz = Mathf.FloorToInt(spawnAreaSize.z / gridSize) + 1;

        long totalPoints = (long)nx * ny * nz;
        if (totalPoints > maxGizmoGridPoints || totalPoints <= 0)
            return;

        Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
        float pointSize = Mathf.Max(gridSize * 0.08f, 0.02f);

        for (int xi = 0; xi < nx; xi++)
        {
            for (int yi = 0; yi < ny; yi++)
            {
                for (int zi = 0; zi < nz; zi++)
                {
                    Vector3 p = new Vector3(
                        center.x - half.x + xi * gridSize,
                        center.y - half.y + yi * gridSize,
                        center.z - half.z + zi * gridSize);

                    Gizmos.DrawWireCube(p, Vector3.one * pointSize);
                }
            }
        }
    }
}