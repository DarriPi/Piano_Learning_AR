using MidiPlayerTK;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;

// Based on open-source code by Madhav Sunil
// Licensed under the MIT License
public class AnchorSpawner : MonoBehaviour
{
    public GameObject anchorPrefab;
    public GameObject controllerIndicatorPrefab; // Prefab for the controller indicator (e.g., a small sphere or arrow)
    public Material planeMaterial; // Material for generated plane
    public GameObject songMenuCanvas; // Reference to the song menu canvas to show/hide
    public event System.Action<Transform> OnPlaneCreated; // Lets other scripts know when the plane is created and provide its transform

    // Expose the top edge points of the plane for use by other scripts (like MidiNoteVisualiser)
    public Vector3 TopEdgeStart { get; private set; }
    public Vector3 TopEdgeEnd { get; private set; }

    private List<GameObject> anchors = new List<GameObject>(); // Store all the anchors that are going to be placed
    private bool planeCreated = false; // Track if the plane has been created to prevent multiple creations
    private GameObject controllerIndicator; // Reference to the instantiated controller indicator
    private Transform rightHandTransform; // Reference to the right hand transform

    void Start()
    {
        // Hide the canvas at the start. It will be shown once the plane is created.
        if (songMenuCanvas != null)
        {
            songMenuCanvas.SetActive(false);
        }

        // Start coroutine to attach indicator when controller is ready
        StartCoroutine(AttachIndicatorWhenReady());

#if UNITY_EDITOR || UNITY_STANDALONE
        // Automatically create 4 anchors in a rectangular formation for Mac/Editor
        Vector3 center = new Vector3(0, -1f, 2f); // Below and in front of camera
        float width = 2f;
        float height = 0.5f;

        // Create 4 anchors in a rectangle
        Vector3[] anchorPositions = {
           center + new Vector3(-width/2, 0, height/2),   // Top left
           center + new Vector3(width/2, 0, height/2),    // Top right
           center + new Vector3(width/2, 0, -height/2),   // Bottom right
           center + new Vector3(-width/2, 0, -height/2)  // Bottom left
       };

        foreach (Vector3 position in anchorPositions)
        {
            PlaceAnchor(position);
        }
#endif
    }

    private IEnumerator AttachIndicatorWhenReady()
    {
        GameObject rightHand = null;
        // Wait until the RightHandAnchor exists in the scene
        while (rightHand == null)
        {
            rightHand = GameObject.Find("RightHandAnchor");
            if (rightHand == null)
                yield return null; // wait for next frame
        }

        rightHandTransform = rightHand.transform;

        // Only instantiate the controller indicator if the prefab is assigned
        if (controllerIndicatorPrefab != null)
        {
            controllerIndicator = Instantiate(controllerIndicatorPrefab);
            controllerIndicator.transform.SetParent(rightHand.transform, false);
            controllerIndicator.transform.localPosition = new Vector3(0, 0, 0.05f);
            controllerIndicator.transform.localRotation = Quaternion.identity;
        }
    }

    void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            Debug.Log("[AnchorScript] Button One pressed!");
        }

        if (rightHandTransform == null)
            return; // Can't spawn without knowing where the controller is

        // Button A spawns an anchor at the controller's forward position, but only if we have less than 4 anchors
        if (OVRInput.GetDown(OVRInput.Button.One) && anchors.Count < 4)
        {
            Vector3 spawnPoint = rightHandTransform.position + rightHandTransform.forward * 0.05f;
            PlaceAnchor(spawnPoint);
            Debug.Log($"[AnchorScript] Spawned anchor at {spawnPoint}. Total anchors: {anchors.Count}");
        }
        else if (OVRInput.GetDown(OVRInput.Button.Two))
        {
            // Handle Button Two press (e.g., reset anchors)
            // Reset the anchors and plane to allow the user to start over
            ResetAnchorsAndPlane();
        }
        else if (OVRInput.GetDown(OVRInput.Button.Three))
        {
            // Hide the rectangle so it is not visible in the scene, but keep the anchors active
            var planeObj = GameObject.Find("AnchorPlane");
            if (planeObj != null)
            {
                planeObj.SetActive(false);
            }
        }
    }

    void PlaceAnchor(Vector3 position)
    {
        GameObject anchor = Instantiate(anchorPrefab, position, Quaternion.identity);
        anchor.transform.parent = null;  // crucial to prevent movement
        anchors.Add(anchor);

        if (anchors.Count == 4 && !planeCreated)
        {
            CreatePlane();
            planeCreated = true;
        }
    }

    void CreatePlane()
    {
        GameObject plane = new GameObject("AnchorPlane");
        MeshFilter meshFilter = plane.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = plane.AddComponent<MeshRenderer>();

        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[4];
        for (int i = 0; i < 4; i++)
            vertices[i] = anchors[i].transform.position;

        mesh.vertices = vertices;
        mesh.triangles = new int[] { 0, 1, 2, 2, 3, 0 };
        mesh.RecalculateNormals();
        meshFilter.mesh = mesh;

        Material matToUse = planeMaterial;
        if (matToUse == null)
        {
            // Use Unlit/Color for proper stereo projection
            matToUse = new Material(Shader.Find("Unlit/Color"));
            matToUse.color = new Color(1f, 0f, 0f, 0.2f); // semi-transparent red

            // Enable transparency and depth support manually
            matToUse.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            matToUse.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            matToUse.SetInt("_ZWrite", 1);
            matToUse.renderQueue = 3000;
        }
        else
        {
            // Assign material is planeMaterial is provided, but ensure it has the correct rendering mode for transparency if needed
            plane.GetComponent<MeshRenderer>().material = planeMaterial;
        }

        //Expose the top edge
        TopEdgeStart = anchors[0].transform.position;
        TopEdgeEnd = anchors[1].transform.position;

        Debug.Log($"[AnchorScript] Plane created! TopEdgeStart: {TopEdgeStart}, TopEdgeEnd: {TopEdgeEnd}");
        Debug.Log($"[AnchorScript] Plane width: {Vector3.Distance(TopEdgeStart, TopEdgeEnd)}");

        if (songMenuCanvas != null)
        {
            songMenuCanvas.SetActive(true);
        }

        // Hide the instruction popup
        Instructions instructions = FindAnyObjectByType<Instructions>();
        if (instructions != null)
        {
            instructions.Hide();
        }

        OnPlaneCreated?.Invoke(plane.transform);

        MidiFilePlayer midiPlayer = FindAnyObjectByType<MidiFilePlayer>();
        if (midiPlayer != null)
        {
            Debug.Log("[AnchorScript] Found MidiFilePlayer, starting playback...");
            midiPlayer.MPTK_Play();
        }
        else
        {
            Debug.LogWarning("[AnchorScript] MidiFilePlayer not found in scene!");
        }

        // Show the song menu canvas
        if (songMenuCanvas != null)
        {
            songMenuCanvas.SetActive(true); // Show the canvas
        }

        // Force the plane to face world +Z
        plane.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
    }

    public void ResetAnchorsAndPlane()
    {
        // Destroy all anchor GameObjects
        foreach (var anchor in anchors)
        {
            if (anchor != null)
                Destroy(anchor);
        }
        anchors.Clear();

        // Destroy the plane if it exists
        var planeObj = GameObject.Find("AnchorPlane");
        if (planeObj != null)
            Destroy(planeObj);

        // Reset top edge points
        TopEdgeStart = Vector3.zero;
        TopEdgeEnd = Vector3.zero;

        // Optionally, hide the song menu canvas
        if (songMenuCanvas != null)
            songMenuCanvas.SetActive(false);

        // Show the instruction popup again
        Instructions instructions = FindAnyObjectByType<Instructions>();
        if (instructions != null)
        {
            instructions.Show();
        }

        // Reset controller indicator (if it was hidden/disabled, re-enable and reset position)
        //if (controllerIndicator != null)
        //{
        //    controllerIndicator.SetActive(true);
        //    controllerIndicator.transform.SetParent(this.transform);
        //    controllerIndicator.transform.localPosition = new Vector3(0, 0, 0.05f);
        //    controllerIndicator.transform.localRotation = Quaternion.identity;
        //}

        // Reset MidiNoteBarVisualizer (clear bars, destroy grid, reset state)
        var visualizer = FindAnyObjectByType<MidiNoteVisualiser>();
        if (visualizer != null)
        {
            visualizer.ReloadAndRestart(); // This will clear bars and reset state
            // Destroy grid overlay if it exists
            var grid = FindAnyObjectByType<MidiNoteLaneGrid>();
            if (grid != null)
                Destroy(grid.gameObject);
        }

        // Stop MIDI playback if needed
        var midiPlayer = FindAnyObjectByType<MidiFilePlayer>();
        if (midiPlayer != null)
        {
            midiPlayer.MPTK_Stop();
        }

        // Reset state
        planeCreated = false;
    }
}