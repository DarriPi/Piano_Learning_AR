using UnityEngine;

public class MidiNoteLaneGrid : MonoBehaviour
{
    public void SetupGrid(
        Vector3 topEdgeStart,
        Vector3 topEdgeEnd,
        Vector3 forward,
        float length,
        int laneCount)
    {
        float width = Vector3.Distance(topEdgeStart, topEdgeEnd);

        Vector3 edgeDir = (topEdgeEnd - topEdgeStart).normalized;

        float laneWidth = width / laneCount;

        Material lineMaterial = new Material(Shader.Find("Sprites/Default"));

        for (int i = 0; i <= laneCount; i++)
        {
            float t = i / (float)laneCount;

            Vector3 start = Vector3.Lerp(topEdgeStart, topEdgeEnd, t);
            Vector3 end = start + forward * length;

            GameObject lineObj = new GameObject($"Lane_{i}");

            lineObj.transform.SetParent(transform);

            LineRenderer line = lineObj.AddComponent<LineRenderer>();

            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);

            line.startWidth = 0.003f;
            line.endWidth = 0.003f;

            line.material = lineMaterial;

            line.startColor = Color.cyan;
            line.endColor = Color.cyan;
        }
    }
}

// using UnityEngine;

// /// <summary>
// /// Draws a grid overlay for MIDI note bars, aligned with the top edge of the target plane.
// /// Not a piano key overlay, but a visual guide for where MIDI bars will land.
// /// </summary>
// public class MidiNoteLaneGrid : MonoBehaviour
// {
//     [Header("Grid Settings")]
//     public int numberOfLanes = 76; // Alter the number of lanes. Gonna make it 76 for now
//     public float gridLength = 1.5f; // How far the grid extends from the top edge. 
//     public int horizontalDivisions = 8; // Number of horizontal timing lines
//     public Color gridColor = new Color(1, 1, 1, 0.35f);
//     public float lineWidth = 0.005f;

//     private LineRenderer lineRenderer;

//     public void SetupGrid(Vector3 topEdgeStart, Vector3 topEdgeEnd, Vector3 planeNormal, float length, int numLanes, int numHorizontal = 8)
//     {
//         // Remove old grid lines
//         foreach (Transform child in transform)
//             Destroy(child.gameObject);

//         numberOfLanes = numLanes;
//         gridLength = length;
//         horizontalDivisions = numHorizontal;

//         // Calculate direction vectors
//         Vector3 edgeDir = (topEdgeEnd - topEdgeStart).normalized;
//         float edgeLength = Vector3.Distance(topEdgeStart, topEdgeEnd);
//         Vector3 forward = planeNormal.normalized; // Grid extends away from the top edge

//         // Non-uniform lane spacing: first and last lanes are wider
//         float wideFactor = 1.5f; // First and last lane are 1.5x as wide
//         int N = numberOfLanes;
//         float totalFactor = (N - 2) + 2 * wideFactor;
//         float normalLaneWidth = edgeLength / totalFactor;
//         float wideLaneWidth = normalLaneWidth * wideFactor;

//         // Build cumulative positions for each vertical line
//         float[] positions = new float[N + 1]; // 0 to N, number of lanes
//         positions[0] = 0f; // Start at the left edge
//         for (int i = 1; i <= N; i++)
//         {
//             if (i == 1)
//                 positions[i] = positions[i - 1] + wideLaneWidth;
//             else if (i == N)
//                 positions[i] = positions[i - 1] + wideLaneWidth;
//             else
//                 positions[i] = positions[i - 1] + normalLaneWidth;
//         }
//         // Normalize to [0,1] for interpolation
//         for (int i = 0; i <= N; i++)
//             positions[i] /= edgeLength;

//         // Draw vertical lane lines
//         for (int i = 0; i <= N; i++)
//         {
//             float t = positions[i];
//             Vector3 laneStart = Vector3.Lerp(topEdgeStart, topEdgeEnd, t);
//             Vector3 laneEnd = laneStart + forward * gridLength;

//             GameObject lineObj = new GameObject($"LaneLine_{i}");
//             lineObj.transform.parent = this.transform;
//             var lr = lineObj.AddComponent<LineRenderer>();

//             var lineMat = new Material(Shader.Find("Unlit/Color"));
//             Color lineColor = gridColor;
//             lineColor.a = Mathf.Max(gridColor.a, 0.25f);
//             lineMat.color = lineColor;

//             lr.material = lineMat;
//             lr.startColor = lineColor;
//             lr.endColor = lineColor;
//             lr.startWidth = lineWidth;
//             lr.endWidth = lineWidth;
//             lr.widthMultiplier = 1f;
//             lr.alignment = LineAlignment.View;
//             lr.loop = false;
//             lr.useWorldSpace = true;
//             lr.numCapVertices = 0;

//             lr.positionCount = 2;
//             lr.SetPosition(0, laneStart);
//             lr.SetPosition(1, laneEnd);
//         }

//         // (Optional) Draw horizontal timing lines in the same way if needed
//         // Uncomment below to add horizontal lines
//         /*
//         for (int h = 1; h <= horizontalDivisions; h++)
//         {
//             float t = h / (float)(horizontalDivisions + 1);
//             Vector3 rowStart = topEdgeStart + forward * (gridLength * t);
//             Vector3 rowEnd = topEdgeEnd + forward * (gridLength * t);

//             GameObject lineObj = new GameObject($"TimingLine_{h}");
//             lineObj.transform.parent = this.transform;
//             var lr = lineObj.AddComponent<LineRenderer>();
//             lr.positionCount = 2;
//             lr.SetPosition(0, rowStart);
//             lr.SetPosition(1, rowEnd);
//             lr.material = new Material(Shader.Find("Unlit/Color"));
//             lr.material.color = gridColor;
//             lr.widthMultiplier = lineWidth;
//             lr.useWorldSpace = true;
//             lr.numCapVertices = 0;
//         }
//         */
//     }

//     public void ClearGrid()
//     {
//         if (lineRenderer) lineRenderer.positionCount = 0;
//     }
// } 