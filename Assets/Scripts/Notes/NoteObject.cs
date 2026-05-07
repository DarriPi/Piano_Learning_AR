using UnityEngine;

public enum NoteType { Tap, Hold }

/// <summary>
/// Represents a single falling note that travels along its key lane toward
/// the keyboard surface. Works for any keyboard orientation because the
/// movement direction is derived at spawn time from the lane's spawn vector.
/// </summary>
public class NoteObject : MonoBehaviour
{
    // ── Public state (set by NoteSpawner via Initialise) ─────────────────────
    [HideInInspector] public int      midiNote;
    [HideInInspector] public NoteType noteType;
    [HideInInspector] public float    holdDuration;   // seconds
    [HideInInspector] public float    targetHitTime;  // song-clock time of the hit

    // ── Private movement data ─────────────────────────────────────────────────
    private Vector3 targetPosition;    // world-space hit point (lane.worldPosition)
    private Vector3 moveDirection;     // unit vector from spawn → target
    private float   noteSpeed;         // metres / second along moveDirection
    private bool    isActive;
    private bool    wasHit;

    // ── Visual components ─────────────────────────────────────────────────────
    private MeshRenderer  meshRenderer;
    private TrailRenderer trailRenderer;

    // Cached shader property IDs (avoids string lookups every frame)
    private static readonly int EmissiveColor = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorProp  = Shader.PropertyToID("_BaseColor");

    // Optional: use a MaterialPropertyBlock so GPU instancing stays valid
    private MaterialPropertyBlock _mpb;

    // ── Synthwave colour palette (one colour per octave group) ────────────────
    private static readonly Color[] Palette =
    {
        new Color(0f,   1f,   1f),   // Cyan
        new Color(0.8f, 0f,   1f),   // Magenta/Purple
        new Color(0f,   0.5f, 1f),   // Electric Blue
        new Color(1f,   0f,   0.8f), // Hot Pink
    };

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Called by NoteSpawner to configure this note before it becomes active.
    /// </summary>
    /// <param name="lane">The key lane this note belongs to.</param>
    /// <param name="spawnPosition">World position where the note starts.</param>
    /// <param name="speed">Travel speed in metres/second.</param>
    /// <param name="hitTime">Song-clock time the note should reach the key.</param>
    /// <param name="type">Tap or Hold.</param>
    /// <param name="holdSec">Hold duration in seconds (0 for taps).</param>
    /// <param name="mat">Base material (will be instanced).</param>
    public void Initialise(
        KeyLane  lane,
        Vector3  spawnPosition,
        float    speed,
        float    hitTime,
        NoteType type,
        float    holdSec,
        Material mat)
    {
        midiNote      = lane.midiNote;
        noteType      = type;
        holdDuration  = holdSec;
        targetHitTime = hitTime;
        noteSpeed     = speed;
        targetPosition = lane.worldPosition;
        isActive      = true;
        wasHit        = false;

        // Compute the direction the note will travel (spawn → target, normalised)
        Vector3 travelVector = targetPosition - spawnPosition;
        moveDirection = travelVector.sqrMagnitude > 0.0001f
            ? travelVector.normalized
            : Vector3.forward;

        // Place note at spawn point
        transform.position = spawnPosition;

        // Match the keyboard mapper's rotation exactly.
        // This ensures our local axes line up with the keyboard:
        //   local X  = across the keys (width)
        //   local Y  = above the key surface (thickness)
        //   local Z  = along the travel direction (length)
        transform.rotation = KeyboardMapper.Instance.transform.rotation;

        // Scale is now straightforward because local Z == travel direction:
        //   X = key-matching width (slightly narrower than the physical key for a gap)
        //   Y = thin slab so the note sits just above the key surface
        //   Z = note length along the lane (short tap, longer for holds)
        float noteLength = (type == NoteType.Hold)
            ? Mathf.Max(0.02f, holdSec * speed)
            : 0.02f;  // 2 cm tap note

        transform.localScale = new Vector3(
            lane.isBlackKey ? 0.010f : 0.018f,  // width — just under real key width
            0.008f,                               // 8 mm slab, sits flush above the key
            noteLength                            // length along travel (Z / forward)
        );

        // Visuals
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.material = new Material(mat);

        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        SetNeonColor(lane.isBlackKey);

        trailRenderer = GetComponent<TrailRenderer>();
        if (trailRenderer != null)
        {
            trailRenderer.material = meshRenderer.material;
            trailRenderer.time     = 0.10f;
        }
    }

    // ── Unity callbacks ───────────────────────────────────────────────────────
    void Update()
    {
        if (!isActive) return;

        // Move along the lane direction
        transform.position += moveDirection * noteSpeed * Time.deltaTime;

        // Miss detection: once the leading edge passes the target by a small margin
        // we use the dot product to check whether we have overshot.
        Vector3 toNote    = transform.position - targetPosition;
        float   overshoot = Vector3.Dot(toNote, moveDirection);

        if (overshoot > 0.05f) // 5 cm past the hit line
            OnMissed();
    }

    // ── Public API ────────────────────────────────────────────────────────────
    /// <summary>Call from NoteSpawner when input is confirmed as a hit.</summary>
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

    // ── Private helpers ───────────────────────────────────────────────────────
    private void OnMissed()
    {
        if (!wasHit)
            FeedbackController.Instance?.TriggerMiss(midiNote);
        ReturnToPool();
    }

    private void SetNeonColor(bool isBlack)
    {
        Color c = Palette[(midiNote / 12) % Palette.Length];
        _mpb.SetColor(EmissiveColor, c * 3f);   // HDR glow
        _mpb.SetColor(BaseColorProp,  c * 0.6f);
        meshRenderer.SetPropertyBlock(_mpb);
    }

    private void SetMissColor()
    {
        Color red = new Color(1f, 0.2f, 0f);
        _mpb.SetColor(EmissiveColor, red * 2f);
        _mpb.SetColor(BaseColorProp,  red);
        meshRenderer.SetPropertyBlock(_mpb);
    }

    private void ReturnToPool()
    {
        isActive = false;
        NotePool.Instance?.Return(this);
    }
}