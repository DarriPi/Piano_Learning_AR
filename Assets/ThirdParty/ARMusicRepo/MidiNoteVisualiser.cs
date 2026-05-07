using System.Collections.Generic;
using UnityEngine;
using MidiPlayerTK;
using System.Collections;

// Based on open-source code by Madhav Sunil
// Licensed under the MIT License
public class MidiNoteVisualiser : MonoBehaviour
{
    [Header("References")]
    public Transform targetPlane; // Assign the plane in the inspector
    public MidiFilePlayer midiPlayer; // Assign or auto-find

    [Header("Bar Settings")]
    public GameObject barPrefab; // Assign a cube prefab or leave null to use default
    public float barLength = 0.2f;
    public float barHeight = 0.05f;
    public float spawnDistance = 0.15f; // Distance from plane where bars spawn
    public float approachTime = 5.0f; // Seconds before note is played
    public int numberOfLanes = 88; // Number of note lanes (e.g., 24 for two octaves)
    public int centerMidiNote = 60; // Middle 
    public event System.Action<Transform> OnPlaneCreated;
    

    [Header("Visual Settings")]
    public bool enableColorCoding = true; // Enable white/black key color coding
    public bool enableIntensityFeedback = true; // Enable brightness changes as bars approach
    public bool enableFlashEffect = true; // Enable yellow flash when reaching target
    public Color whiteKeyColor = new Color32(0x47, 0x92, 0xF4, 0xFF); // #4792F4
    public Color blackKeyColor = new Color32(0x27, 0x51, 0x88, 0xFF); // #275188
    public Color flashColor = Color.yellow;

    [Header("Debug")]
    public bool showDebugInfo = false;

    private float barWidth;
    private float noteLaneSpacing;
    private AnchorSpawner anchorSpawner;

    private List<MPTKEvent> preloadedMidiEvents = new List<MPTKEvent>();
    private float midiDuration = 0f;
    private bool isReadyToPlay = false;
    private bool isPlaying = false;

    private class NoteBar
    {
        public GameObject barObject;
        public float startTime;
        public float playTime;
        public int note;
        public Vector3 laneOffset;
    }

    private List<NoteBar> activeBars = new List<NoteBar>();

    private MidiNoteLaneGrid noteLaneGrid;

    void Start()
    {
        if (!midiPlayer)
            midiPlayer = FindAnyObjectByType<MidiFilePlayer>();
        // if (midiPlayer)
        //     midiPlayer.OnEventNotesMidi.AddListener(OnMidiNotes);
        // else
        //     Debug.LogError("MidiFilePlayer not found for MidiNoteBarVisualizer.");

        // Subscribe to AnchorSpawner's plane creation event
        anchorSpawner = FindAnyObjectByType<AnchorSpawner>();
        if (anchorSpawner != null)
        {
            anchorSpawner.OnPlaneCreated += SetTargetPlane;
            Debug.Log("[MidiNoteBarVisualizer] AnchorSpawner found.");
        }
        else
        {
            Debug.LogWarning("AnchorSpawner not found. Make sure it exists in the scene.");
        }
        // if (midiPlayer) {
        //     midiPlayer.MPTK_Stop(); // Ensure clean state
        //     midiPlayer.MPTK_Loop = true; // Optional: enable looping
        // }
    }

    private void SetTargetPlane(Transform planeTransform)
    {
        targetPlane = planeTransform;
        CalculateBarDimensions();
        Debug.Log($"[MidiNoteBarVisualizer] Target plane set dynamically: {targetPlane.name}");
        LoadMidiDataFromPlayer();
        isReadyToPlay = true;
        Debug.Log("[MidiNoteBarVisualizer] Ready to play! Press Space or controller button to start.");

        // --- Add grid overlay for MIDI bars ---
        if (noteLaneGrid != null)
        {
            Destroy(noteLaneGrid.gameObject); // Clean up old grid if it exists
        }
        GameObject gridObj = new GameObject("MidiNoteLaneGrid");
        noteLaneGrid = gridObj.AddComponent<MidiNoteLaneGrid>();

        if (anchorSpawner != null)
        {
            Vector3 topEdgeStart = anchorSpawner.TopEdgeStart;
            Vector3 topEdgeEnd = anchorSpawner.TopEdgeEnd;
            Vector3 planeNormal = targetPlane.forward;
            noteLaneGrid.SetupGrid(
                topEdgeStart,
                topEdgeEnd,
                planeNormal,
                1.5f, // grid length (adjust as needed)
                numberOfLanes, // number of lanes (e.g., 88)
                8 // number of horizontal timing lines
            );
        }
        // --- End grid overlay ---
        ReloadAndRestart();
        Play();
    }

    private void LoadMidiDataFromPlayer()
    {
        if (!midiPlayer)
        {
            midiPlayer = FindAnyObjectByType<MidiFilePlayer>();
            if (!midiPlayer)
            {
                Debug.LogError("[MidiNoteBarVisualizer] MidiFilePlayer not found!");
                return;
            }
        }
        
        Debug.Log("[MidiNoteBarVisualizer] Loading MIDI data...");
        midiPlayer.MPTK_Load();
        if (midiPlayer.MPTK_MidiLoaded != null)
        {
            preloadedMidiEvents.Clear();
            preloadedMidiEvents = new List<MPTKEvent>(midiPlayer.MPTK_MidiLoaded.MPTK_MidiEvents);
            midiDuration = (float)midiPlayer.MPTK_MidiLoaded.MPTK_Duration.TotalSeconds;
            Debug.Log($"[MidiNoteBarVisualizer] Loaded {preloadedMidiEvents.Count} MIDI events, duration: {midiDuration} seconds.");
            
            // Count note events
            int noteOnEvents = 0;
            foreach (var evt in preloadedMidiEvents)
            {
                if (evt.Command == MPTKCommand.NoteOn && evt.Velocity > 0)
                    noteOnEvents++;
            }
            Debug.Log($"[MidiNoteBarVisualizer] Found {noteOnEvents} note-on events.");
        }
        else
        {
            Debug.LogError("[MidiNoteBarVisualizer] MPTK_MidiLoaded is null after loading!");
        }
    }

    public void Play()
    {   
        Debug.Log($"[MidiNoteBarVisualizer] Play() called. isReadyToPlay: {isReadyToPlay}, isPlaying: {isPlaying}, events: {preloadedMidiEvents.Count}");
        if (isReadyToPlay && !isPlaying && preloadedMidiEvents.Count > 0)
        {
            StartCoroutine(SpawnBarsCoroutine());
            // if (midiPlayer) midiPlayer.MPTK_Play(); // Uncomment to enable audio playback
            isPlaying = true;
            Debug.Log("[MidiNoteBarVisualizer] Playback and visualization started.");
        }
        else
        {
            Debug.LogWarning($"[MidiNoteBarVisualizer] Cannot start playback: isReadyToPlay={isReadyToPlay}, isPlaying={isPlaying}, events={preloadedMidiEvents.Count}");
        }
    }

    private System.Collections.IEnumerator SpawnBarsCoroutine()
    {
        float startTime = Time.time;
        int spawnedCount = 0;
        Debug.Log($"[MidiNoteBarVisualizer] Starting to spawn bars. Total events: {preloadedMidiEvents.Count}");
        
        foreach (var midiEvent in preloadedMidiEvents)
        {
            if (midiEvent.Command == MPTKCommand.NoteOn && midiEvent.Velocity > 0)
            {
                float eventTime = (float)midiEvent.RealTime / 1000f; // ms to s
                float waitTime = (startTime + eventTime - Time.time);
                if (waitTime > 0)
                    yield return new WaitForSeconds(waitTime);
                
                SpawnNoteBar(midiEvent);
                spawnedCount++;
                
                if (spawnedCount % 10 == 0) // Log every 10th bar
                {
                    Debug.Log($"[MidiNoteBarVisualizer] Spawned {spawnedCount} bars so far...");
                }
            }
        }
        
        Debug.Log($"[MidiNoteBarVisualizer] Finished spawning bars. Total spawned: {spawnedCount}");
    }

    void CalculateBarDimensions()
    {
        if (targetPlane != null)
        {
            MeshFilter mf = targetPlane.GetComponent<MeshFilter>();
            if (mf != null && mf.mesh != null)
            {
                Vector3[] verts = mf.mesh.vertices;
                // Calculate the actual width of the plane using the top edge
                float planeWidth = Vector3.Distance(anchorSpawner.TopEdgeStart, anchorSpawner.TopEdgeEnd);
                Debug.Log($"[MidiNoteBarVisualizer] TopEdgeStart: {anchorSpawner.TopEdgeStart}, TopEdgeEnd: {anchorSpawner.TopEdgeEnd}");
                
                // Calculate spacing and width for 88 keys
                noteLaneSpacing = planeWidth / numberOfLanes;
                barWidth = noteLaneSpacing; // No gap between bars
                Debug.Log($"[MidiNoteBarVisualizer] Plane width: {planeWidth}, Lane spacing: {noteLaneSpacing}, Bar width: {barWidth}");
            }
            else
            {
                Debug.LogWarning("MeshFilter or Mesh missing on targetPlane.");
                noteLaneSpacing = 0.1f;
                barWidth = 0.1f;
            }
        }
        else
        {
            noteLaneSpacing = 0.1f;
            barWidth = 0.1f;
        }
    }

    void OnMidiNotes(List<MPTKEvent> midiEvents)
    {
        foreach (var midiEvent in midiEvents)
        {
            if (midiEvent.Command == MPTKCommand.NoteOn && midiEvent.Velocity > 0)
            {
                SpawnNoteBar(midiEvent);
            }
        }
    }

    // Returns the world position for a given lane index (0 to numberOfLanes-1), matching the grid's non-uniform spacing
    private Vector3 GetLanePosition(int laneIndex, Vector3 topEdgeStart, Vector3 topEdgeEnd)
    {
        float wideFactor = 1.5f;
        int N = numberOfLanes;
        float edgeLength = Vector3.Distance(topEdgeStart, topEdgeEnd);
        float totalFactor = (N - 2) + 2 * wideFactor;
        float normalLaneWidth = edgeLength / totalFactor;
        float wideLaneWidth = normalLaneWidth * wideFactor;

        float[] positions = new float[N + 1];
        positions[0] = 0f;
        for (int i = 1; i <= N; i++)
        {
            if (i == 1)
                positions[i] = positions[i - 1] + wideLaneWidth;
            else if (i == N)
                positions[i] = positions[i - 1] + wideLaneWidth;
            else
                positions[i] = positions[i - 1] + normalLaneWidth;
        }
        for (int i = 0; i <= N; i++)
            positions[i] /= edgeLength;

        float t = positions[laneIndex];
        Debug.Log($"[GetLanePosition] laneIndex: {laneIndex}, t: {t}");
        return Vector3.Lerp(topEdgeStart, topEdgeEnd, t);
    }

    void SpawnNoteBar(MPTKEvent midiEvent)
    {
        if (anchorSpawner == null)
        {
            Debug.LogWarning("[MidiNoteBarVisualizer] AnchorSpawner not set. Cannot spawn bars correctly.");
            return;
        }
        
        // Calculate lane position based on note value
        int firstMidiNote = 21; // A0
        int laneIndex = midiEvent.Value - firstMidiNote;
        laneIndex = Mathf.Clamp(laneIndex, 0, numberOfLanes - 1);
        Debug.Log($"[SpawnNoteBar] MIDI note: {midiEvent.Value}, laneIndex: {laneIndex}");
        
        // Calculate position along the top edge of the plane using non-uniform spacing
        if (targetPlane == null)
        {
            Debug.LogWarning("[MidiNoteBarVisualizer] Target plane not set. Cannot spawn bars.");
            return;
        }
        
        Vector3 edgePoint = GetLanePosition(laneIndex, anchorSpawner.TopEdgeStart, anchorSpawner.TopEdgeEnd);
        Vector3 edgeDir = (anchorSpawner.TopEdgeEnd - anchorSpawner.TopEdgeStart).normalized;
        Vector3 parentPos = edgePoint + targetPlane.forward * (spawnDistance + barLength / 2f);
        GameObject barParent = new GameObject($"BarParent_{midiEvent.Value}_{midiEvent.RealTime}");
        barParent.transform.position = parentPos;
        barParent.transform.rotation = targetPlane.rotation;

        // Ensure parent is empty (no mesh or renderer)
        var meshFilter = barParent.GetComponent<MeshFilter>();
        //if (meshFilter) Destroy(meshFilter);
        var meshRenderer = barParent.GetComponent<MeshRenderer>();
        //if (meshRenderer) Destroy(meshRenderer);

        if (meshRenderer)
        {
            // Create a fully transparent material
            var transparentMat = new Material(Shader.Find("Standard"));
            transparentMat.color = new Color(0, 0, 0, 0); // Fully transparent
            transparentMat.SetFloat("_Mode", 3); // Transparent
            transparentMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            transparentMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            transparentMat.SetInt("_ZWrite", 0);
            transparentMat.DisableKeyword("_ALPHATEST_ON");
            transparentMat.EnableKeyword("_ALPHABLEND_ON");
            transparentMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            transparentMat.renderQueue = 3000;
            meshRenderer.material = transparentMat;
        }

        GameObject bar = barPrefab ? Instantiate(barPrefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.transform.SetParent(barParent.transform, false);
        bar.transform.localPosition = edgeDir * (barWidth / 2f);
        bar.transform.localRotation = Quaternion.identity;
        bar.transform.localScale = new Vector3(barWidth, barHeight, barLength);
        bar.name = $"NoteBar_{midiEvent.Value}_{midiEvent.RealTime}";

        // Add visual feedback - change color based on note type (white/black keys)
        Renderer renderer = bar.GetComponent<Renderer>();
        if (renderer != null && enableColorCoding)
        {
            // Simple color coding: white keys = white, black keys = black
            bool isBlackKey = IsBlackKey(midiEvent.Value);
            renderer.material.color = isBlackKey ? blackKeyColor : whiteKeyColor;
        }

        int noteValue = midiEvent.Value % 12;
        int octave = (midiEvent.Value / 12) - 2; // MIDI note 60 = C4
        string[] noteNames = {"C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"};
        string noteName = noteNames[noteValue];
        Debug.Log($"[MidiNoteBarVisualizer] Spawned note {midiEvent.Value} ({noteName}{octave}) at position {barParent.transform.position}, x: {barParent.transform.position.x}");
        
        NoteBar noteBar = new NoteBar
        {
            barObject = barParent, // Store the parent for movement/destruction
            startTime = Time.time,
            playTime = (float)midiEvent.RealTime / 1000f, // ms to s
            note = midiEvent.Value,
            laneOffset = edgePoint
        };
        activeBars.Add(noteBar);
        
        Debug.Log($"[MidiNoteBarVisualizer] Successfully spawned bar for note {midiEvent.Value} at {barParent.transform.position}. Active bars: {activeBars.Count}");
    }

    // Helper method to determine if a MIDI note corresponds to a black key
    private bool IsBlackKey(int midiNote)
    {
        int noteInOctave = midiNote % 12;
        // Black keys are: 1, 3, 6, 8, 10 (C#, D#, F#, G#, A#)
        return noteInOctave == 1 || noteInOctave == 3 || noteInOctave == 6 || noteInOctave == 8 || noteInOctave == 10;
    }

    void Update()
    {
        // For now, use spacebar or controller button as play button
        // if (isReadyToPlay && !isPlaying && (Input.GetKeyDown(KeyCode.Space) ||
        //     (OVRInput.GetDown(OVRInput.Button.One))))
        // {
        //     Play();
        // }
        float now = Time.time;
        for (int i = activeBars.Count - 1; i >= 0; i--)
        {
            NoteBar nb = activeBars[i];
            float t = (now - nb.startTime) / approachTime;
            t = Mathf.Clamp01(t);


            if (targetPlane == null)
            {
                Debug.LogWarning("Target plane not set. Cannot spawn bars.");
                return;
            }
            // Move from spawn position (above edge) to edge
            Vector3 start = nb.laneOffset + Vector3.forward * spawnDistance;
            Vector3 end = nb.laneOffset;
            nb.barObject.transform.position = Vector3.Lerp(start, end, t);
            nb.barObject.transform.rotation = targetPlane.rotation;

            // Add visual feedback as the bar approaches the target
            Renderer renderer = nb.barObject.GetComponent<Renderer>();
            if (renderer != null && enableIntensityFeedback)
            {
                // Change color intensity based on proximity to target
                Color originalColor = IsBlackKey(nb.note) ? blackKeyColor : whiteKeyColor;
                float intensity = 1f + (1f - t) * 0.5f; // Brighter when far, normal when close
                renderer.material.color = new Color(
                    originalColor.r * intensity,
                    originalColor.g * intensity,
                    originalColor.b * intensity,
                    originalColor.a
                );
            }

            // When the bar reaches the target plane, add a flash effect
            if (t >= 0.95f && t < 1.0f && enableFlashEffect)
            {
                if (renderer != null)
                {
                    renderer.material.color = flashColor; // Flash when reaching target
                }
            }

            // Calculate the front face position of the bar (assuming bar's Z+ points forward)
            Vector3 frontFacePos = nb.barObject.transform.position - Vector3.forward * (barLength / 2f);

            // The plane is at nb.laneOffset, with normal Vector3.forward
            float signedDistance = Vector3.Dot(frontFacePos - nb.laneOffset, Vector3.forward);

            // Destroy the bar if the front face has reached or passed the plane
            if (signedDistance <= 0f)
            {
                Debug.Log($"[MidiNoteBarVisualizer] Destroy Note {nb.barObject}");
                Destroy(nb.barObject);
                activeBars.RemoveAt(i);
                continue;
            }
        }
    }

    public void ReloadAndRestart()
    {
        // Stop and clear all current bars
        foreach (var nb in activeBars)
        {
            if (nb.barObject != null)
                Destroy(nb.barObject);
        }
        activeBars.Clear();
        isPlaying = false;
        isReadyToPlay = false;
        // Reload MIDI data from the player
        LoadMidiDataFromPlayer();
        isReadyToPlay = true;
        //Play();
    }
}