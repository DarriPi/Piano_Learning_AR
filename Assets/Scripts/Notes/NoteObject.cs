using UnityEngine;

public enum NoteType { Tap, Hold }

public class NoteObject : MonoBehaviour
{
    [HideInInspector] public int midiNote;
    [HideInInspector] public NoteType noteType;
    [HideInInspector] public float holdDuration;    // seconds
    [HideInInspector] public float targetHitTime;   // world-clock time

    private Vector3 targetPosition;
    private Vector3 moveDirection;
    private float moveSpeed;
    private bool isActive;
    private bool wasHit;

    private MeshRenderer meshRenderer;
    private TrailRenderer trailRenderer;

    private static readonly int EmissiveColor = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock _mpb;

    public void Initialise(KeyLane lane, Vector3 direction, float speed,
        float spawnDistance, float hitTime, NoteType type, float holdSec,
        Material mat)
    {
        midiNote = lane.midiNote;
        noteType = type;
        holdDuration = holdSec;
        targetHitTime = hitTime;

        moveDirection = direction.normalized;
        moveSpeed = speed;

        // Target is the lane’s world position (where the note must be at hitTime)
        targetPosition = lane.worldPosition;

        // Spawn behind the keyboard: target - direction * spawnDistance
        transform.position = targetPosition - moveDirection * spawnDistance;

        // Rotate the note so its forward (Z) faces the movement direction
        if (moveDirection != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(moveDirection, Vector3.up);

        // Scale: tap = small cube; hold = stretched bar along movement direction
        float holdLength = type == NoteType.Hold ? holdSec * speed : 0f;
        float noteLength = Mathf.Max(0.02f, holdLength);
        transform.localScale = new Vector3(
            lane.isBlackKey ? 0.012f : 0.020f,   // lateral width
            0.02f,                                // thin vertical height
            noteLength                            // length along forward (Z)
        );

        // Material setup with instanced property block for performance
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = mat;          // use shared for GPU instancing
        SetNeonColor(lane.isBlackKey);

        trailRenderer = GetComponent<TrailRenderer>();
        if (trailRenderer != null)
        {
            trailRenderer.material = mat;
            trailRenderer.time = 0.08f;
        }

        isActive = true;
        wasHit = false;
    }

    void Update()
    {
        if (!isActive) return;

        // Move toward the target
        transform.position += moveDirection * moveSpeed * Time.deltaTime;

        // Check if the note has passed the target (overshot)
        if (Vector3.Dot(transform.position - targetPosition, moveDirection) >= 0)
        {
            // Note reached or passed the target without being hit -> miss
            OnMissed();
        }
    }

    public void OnHit(bool correct)
    {
        wasHit = true;
        if (correct)
        {
            FeedbackController.Instance?.TriggerHit(midiNote);
            ReturnToPool();
        }
        else
        {
            SetMissColor();
            Invoke(nameof(ReturnToPool), 0.3f);
        }
    }

    private void OnMissed()
    {
        if (!wasHit)
            FeedbackController.Instance?.TriggerMiss(midiNote);
        ReturnToPool();
    }

    private void SetNeonColor(bool isBlack)
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        Color[] palette = {
            new Color(0f, 1f, 1f),        // Cyan
            new Color(0.8f, 0f, 1f),      // Magenta/Purple
            new Color(0f, 0.5f, 1f),      // Electric Blue
            new Color(1f, 0f, 0.8f)       // Hot Pink
        };
        Color c = palette[(midiNote / 12) % palette.Length];
        _mpb.SetColor(EmissiveColor, c * 3f);
        _mpb.SetColor(BaseColor, c * 0.6f);
        meshRenderer.SetPropertyBlock(_mpb);
    }

    private void SetMissColor()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        Color red = new Color(1f, 0.2f, 0f);
        _mpb.SetColor(EmissiveColor, red * 2f);
        _mpb.SetColor(BaseColor, red);
        meshRenderer.SetPropertyBlock(_mpb);
    }

    private void ReturnToPool()
    {
        isActive = false;
        NotePool.Instance?.Return(this);
    }
}