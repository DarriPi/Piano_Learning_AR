using UnityEngine;

/// <summary>
/// Draws a grid overlay for MIDI note bars, aligned with the top edge of the target plane.
/// Not a piano key overlay, but a visual guide for where MIDI bars will land.
/// </summary>
public class MidiNoteLaneGrid : MonoBehaviour
{
    [Header("Grid Settings")]
    public int numberOfLanes = 88;
    public float gridLength = 1.5f; // How far the grid extends from the top edge
    public int horizontalDivisions = 8; // Number of horizontal timing lines
    public Color gridColor = new Color(1, 1, 1, 0.2f);
    public float lineWidth = 0.001f;

    private LineRenderer lineRenderer;

    public void SetupGrid(Vector3 topEdgeStart, Vector3 topEdgeEnd, Vector3 planeNormal, float length, int numLanes, int numHorizontal = 8)
    {
        // Remove old grid lines
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        numberOfLanes = numLanes;
        gridLength = length;
        horizontalDivisions = numHorizontal;

        // Calculate direction vectors
        Vector3 edgeDir = (topEdgeEnd - topEdgeStart).normalized;
        float edgeLength = Vector3.Distance(topEdgeStart, topEdgeEnd);
        Vector3 forward = planeNormal.normalized; // Grid extends away from the top edge

        // Non-uniform lane spacing: first and last lanes are wider
        float wideFactor = 1.5f; // First and last lane are 1.5x as wide
        int N = numberOfLanes;
        float totalFactor = (N - 2) + 2 * wideFactor;
        float normalLaneWidth = edgeLength / totalFactor;
        float wideLaneWidth = normalLaneWidth * wideFactor;

        // Build cumulative positions for each vertical line
        float[] positions = new float[N + 1]; // 0 to N
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
        // Normalize to [0,1] for interpolation
        for (int i = 0; i <= N; i++)
            positions[i] /= edgeLength;

        // Draw vertical lane lines
        for (int i = 0; i <= N; i++)
        {
            float t = positions[i];
            Vector3 laneStart = Vector3.Lerp(topEdgeStart, topEdgeEnd, t);
            Vector3 laneEnd = laneStart + forward * gridLength;

            GameObject lineObj = new GameObject($"LaneLine_{i}");
            lineObj.transform.parent = this.transform;
            var lr = lineObj.AddComponent<LineRenderer>();

              // Use Standard shader and set up for transparency
            var standardMat = new Material(Shader.Find("Standard"));
            standardMat.SetFloat("_Mode", 3); // 3 = Transparent
            Color transparentColor = gridColor;
            transparentColor.a = 0.25f; // Fainter line
            standardMat.color = transparentColor;
            standardMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            standardMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            standardMat.SetInt("_ZWrite", 0);
            standardMat.DisableKeyword("_ALPHATEST_ON");
            standardMat.EnableKeyword("_ALPHABLEND_ON");
            standardMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            standardMat.renderQueue = 3000;

            lr.material = standardMat;
            lr.widthMultiplier = lineWidth;
            lr.loop = false;
            lr.useWorldSpace = true;
            lr.numCapVertices = 0;

            lr.positionCount = 2;
            lr.SetPosition(0, laneStart);
            lr.SetPosition(1, laneEnd);
        }

        // (Optional) Draw horizontal timing lines in the same way if needed
        // Uncomment below to add horizontal lines
        /*
        for (int h = 1; h <= horizontalDivisions; h++)
        {
            float t = h / (float)(horizontalDivisions + 1);
            Vector3 rowStart = topEdgeStart + forward * (gridLength * t);
            Vector3 rowEnd = topEdgeEnd + forward * (gridLength * t);

            GameObject lineObj = new GameObject($"TimingLine_{h}");
            lineObj.transform.parent = this.transform;
            var lr = lineObj.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, rowStart);
            lr.SetPosition(1, rowEnd);
            lr.material = new Material(Shader.Find("Unlit/Color"));
            lr.material.color = gridColor;
            lr.widthMultiplier = lineWidth;
            lr.useWorldSpace = true;
            lr.numCapVertices = 0;
        }
        */
    }

    public void ClearGrid()
    {
        if (lineRenderer) lineRenderer.positionCount = 0;
    }
} 