using UnityEngine;

public enum NoteType { Tap, Hold }

public class NoteObject : MonoBehaviour
{
    [HideInInspector] public int midiNote; // MIDI note number
    [HideInInspector] public NoteType noteType; // note type (tap or hold)
    [HideInInspector] public float holdDuration;  // in seconds
    [HideInInspector] public float targetHitTime; // world clock time

    private Vector3 targetPosition; // position where the note should be hit
    private float noteSpeed; // speed at which the note moves towards the target position
    private bool isActive; // whether the note is active and should be moving
    private bool wasHit; // whether the note was successfully hit
    private MeshRenderer meshRenderer; // reference to the MeshRenderer component for visual feedback
    private TrailRenderer trailRenderer; // reference to the TrailRenderer component for visual feedback

    // Visual state
    private static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");

    public void Initialise(KeyLane lane, float speed, float spawnDepth,
                       float hitTime, NoteType type, float holdSec, Material mat)
    {
        midiNote = lane.midiNote;
        noteType = type;
        holdDuration = holdSec;
        targetHitTime = hitTime;
        noteSpeed = speed;
        targetPosition = lane.worldPosition;
        isActive = true;
        wasHit = false;

        // Spawn BEHIND the keyboard, travelling forward (toward player)
        transform.position = lane.worldPosition - Vector3.forward * spawnDepth;

        // Scale: width stays narrow, depth (Z) stretches for hold notes
        float holdLength = type == NoteType.Hold ? holdSec * speed : 0f;
        float noteDepth = Mathf.Max(0.02f, holdLength);

        transform.localScale = new Vector3(
            lane.isBlackKey ? 0.012f : 0.020f,  // X — key width
            0.008f,                              // Y — flat, sits just above keys
            noteDepth                            // Z — length along travel direction
        );

        // Assign material (instanced so each note can have unique color)
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.material = new Material(mat);
        SetNeonColor(lane.isBlackKey);

        trailRenderer = GetComponent<TrailRenderer>();
        if (trailRenderer != null)
        {
            trailRenderer.material = meshRenderer.material;
            trailRenderer.time = 0.08f;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (!isActive) return;

        // Move toward player (positive Z = forward)
        transform.position += Vector3.forward * noteSpeed * Time.deltaTime;

        // Miss: note has passed the keyboard surface (5cm overshoot tolerance)
        if (transform.position.z > targetPosition.z + 0.05f)
        {
            OnMissed();
        }
    }

    public void OnHit(bool correct)
    {
        wasHit = true;

        // If correct, we can add visual feedback here (e.g., flash color, play sound)
        if (correct)
        {
            FeedbackController.Instance?.TriggerHit(midiNote);
            ReturnToPool();
        }
        else
        {
            SetMissColor();
            Invoke(nameof(ReturnToPool), 0.3f); // delay to show miss color
        }
    }

    public void OnMissed()
    {
        if (!wasHit)
        {
            FeedbackController.Instance?.TriggerHit(midiNote);
        }
        ReturnToPool();
    }

    private void SetNeonColor(bool isBlack)
    {
        Color[] palette = {
            new Color(0f, 1f, 1f),      // Cyan
            new Color(0.8f, 0f, 1f),    // Magenta
            new Color(0f, 0.5f, 1f),    // Electric Blue
            new Color(1f, 0f, 0.8f),    // Hot Pink
        };

        int paletteIdx = (midiNote / 12) % palette.Length;
        Color c = palette[paletteIdx];

        // These two lines are what most people miss:
        meshRenderer.material.EnableKeyword("_EMISSION");
        meshRenderer.material.globalIlluminationFlags =
            MaterialGlobalIlluminationFlags.RealtimeEmissive;

        meshRenderer.material.SetColor(EmissiveColor, c * 3f);
        meshRenderer.material.color = c * 0.6f;
    }

    private void SetMissColor()
    {
        Color red = new Color(1f, 0.2f, 0f);
        meshRenderer.material.SetColor(EmissiveColor, red * 2f);
        meshRenderer.material.color = red;
    }

    private void ReturnToPool()
    {
        isActive = false;
        NotePool.Instance?.Return(this);
    }
}
