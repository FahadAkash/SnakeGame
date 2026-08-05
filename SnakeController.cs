using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using static UnityEngine.AudioSettings;

public class SnakeController : MonoBehaviour
{
    // Settings
    public float MoveSpeed = 5;
    public float SteerSpeed = 180;
    public float BodySpeed = 5;
    public int Gap = 10;

    // References
    public GameObject BodyPrefab;
    public Joystick Joystick;

    [Header("Juice - Grow")]
    [Tooltip("How long the pop-in scale animation takes when a new body segment spawns")]
    public float GrowPopDuration = 0.25f;
    public Ease GrowPopEase = Ease.OutBack;

    [Header("Juice - Eat")]
    [Tooltip("How big the head 'chomp' punch is when eating food")]
    public float EatPunchScale = 0.2f;
    public float EatPunchDuration = 0.3f;
    public int EatPunchVibrato = 8;

    [Header("Juice - Hit / Death")]
    public float HitPunchScale = 0.35f;
    public float HitPunchDuration = 0.35f;
    public int HitPunchVibrato = 10;

    [Tooltip("Tail squash-and-shrink duration before it's actually destroyed")]
    public float TailDeathShrinkDuration = 0.25f;
    public Ease TailDeathShrinkEase = Ease.InBack;

    [Header("Juice - Camera Shake")]
    [Tooltip("Leave empty to auto-use Camera.main")]
    public Camera CameraToShake;
    public float ShakeDuration = 0.3f;
    public float ShakeStrength = 0.5f;
    public int ShakeVibrato = 20;

    [Header("Juice - Hit Stop (time slowdown)")]
    public bool UseHitStop = true;
    public float HitStopTimeScale = 0.15f;
    public float HitStopDuration = 0.08f;
    public float HitStopRecoverDuration = 0.2f;

    // Lists
    private List<GameObject> BodyParts = new List<GameObject>();
    private List<Vector3> PositionsHistory = new List<Vector3>();






    public bool IsMobile = false;
    // Start is called before the first frame update
    void Start()
    {
        GrowSnake();
        GrowSnake();
        GrowSnake();
        GrowSnake();
        GrowSnake();
    }

    // Update is called once per frame
    void Update()
    {
        // Move forward
        transform.position += transform.forward * MoveSpeed * Time.deltaTime;

        // Steer
        float steerDirection;
        if (IsMobile)
        {
            steerDirection = Joystick.Horizontal;
            
        }
        else
        {
            steerDirection = Input.GetAxis("Horizontal");
        }
            transform.Rotate(Vector3.up * steerDirection * SteerSpeed * Time.deltaTime);
        // Returns value -1, 0, or 1


        // Store position history
        PositionsHistory.Insert(0, transform.position);

        // Move body parts
        int index = 0;
        foreach (var body in BodyParts)
        {
            Vector3 point = PositionsHistory[Mathf.Clamp(index * Gap, 0, PositionsHistory.Count - 1)];

            // Move body towards the point along the snakes path
            Vector3 moveDirection = point - body.transform.position;
            body.transform.position += moveDirection * BodySpeed * Time.deltaTime;

            // Rotate body towards the point along the snakes path
            body.transform.LookAt(point);
            index++;
        }
    }

    private void GrowSnake()
    {
        // Spawn at the current tail's position (or the head, if this is
        // the first segment) instead of world origin, so the pop-in
        // animation plays in the right place instead of flashing at (0,0,0).
        Vector3 spawnPos = BodyParts.Count > 0
            ? BodyParts[BodyParts.Count - 1].transform.position
            : transform.position;

        GameObject body = Instantiate(BodyPrefab, spawnPos, Quaternion.identity);

        // Juicy pop-in: scale from 0 up to full size with an overshoot ease
        body.transform.localScale = Vector3.zero;
        body.transform.DOScale(Vector3.one, GrowPopDuration).SetEase(GrowPopEase);

        BodyParts.Add(body);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        for (int i = 0; i < PositionsHistory.Count - 1; i++)
        {
            Gizmos.DrawLine(PositionsHistory[i], PositionsHistory[i + 1]);
        }
    }

    /// <summary>
    /// Removes the tail (last body part) of the snake, playing a squash
    /// and shrink animation before actually destroying it.
    /// If that was the last body part remaining, the snake has nothing
    /// left and the game ends.
    /// </summary>
    private void DestroySnake()
    {
        if (BodyParts.Count == 0)
            return;

        int lastIndex = BodyParts.Count - 1;
        GameObject tail = BodyParts[lastIndex];

        // Remove from the list before destroying so nothing else
        // in Update() tries to reference it this frame
        BodyParts.RemoveAt(lastIndex);

        if (tail != null)
        {
            tail.transform.DOKill();
            tail.transform.DOScale(Vector3.zero, TailDeathShrinkDuration)
                .SetEase(TailDeathShrinkEase)
                .OnComplete(() => Destroy(tail));
        }

        // If that was the last segment, the snake is fully destroyed
        if (BodyParts.Count == 0)
        {
            Debug.Log("SnakeController: Last tail destroyed - Game Over.");

            // TODO: hook this up to your actual game over flow, e.g.:
            // GameManager.Instance.GameOver();

            enabled = false; // stop Update() from running
        }
    }

    /// <summary>
    /// Head punch-scale feedback when eating food.
    /// </summary>
    private void PlayEatFeedback()
    {
        transform.DOKill();
        transform.DOPunchScale(Vector3.one * EatPunchScale, EatPunchDuration, EatPunchVibrato, 0.7f);
    }

    /// <summary>
    /// Head punch, camera shake, and a brief hit-stop for impact feedback
    /// when the snake hits an obstacle.
    /// </summary>
    private void PlayHitFeedback()
    {
        transform.DOKill();
        transform.DOPunchScale(Vector3.one * HitPunchScale, HitPunchDuration, HitPunchVibrato, 1f);

        Camera cam = CameraToShake != null ? CameraToShake : Camera.main;
        if (cam != null)
        {
            cam.transform.DOKill();
            cam.transform.DOShakePosition(ShakeDuration, ShakeStrength, ShakeVibrato);
        }

        if (UseHitStop)
        {
            DOTween.Kill("HitStop");
            Time.timeScale = HitStopTimeScale;
            DOTween.To(() => Time.timeScale, x => Time.timeScale = x, 1f, HitStopRecoverDuration)
                .SetDelay(HitStopDuration)
                .SetUpdate(true) // runs on unscaled time so it isn't frozen by the slowdown itself
                .SetId("HitStop");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Food"))
        {
            GrowSnake();
            PlayEatFeedback();
            Destroy(other.gameObject);
        }

        if (other.CompareTag("Obstacle"))
        {
            PlayHitFeedback();
            DestroySnake();
            Destroy(other.gameObject);
        }
    }
}