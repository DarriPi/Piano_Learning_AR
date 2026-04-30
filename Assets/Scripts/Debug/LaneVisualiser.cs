using Unity.VisualScripting;
using UnityEngine;

public class LaneVisualiser : MonoBehaviour
{
    [SerializeField] private KeyboardMapper keyboardMapper; // Reference to the KeyboardMapper component
    [SerializeField] private Material laneMaterial; // Material to use for the lane visualization
    [SerializeField] private float laneHeight = 2.0f; // Height of the lane above the keyboard

    private LineRenderer[] laneLines; // LineRenderer component to visualize the lane

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Lane lines for a 76 key keyboard (E1 to G7)
        laneLines = new LineRenderer[76];

        for (int i = 0; i < laneLines.Length; i++)
        {
            var go = new GameObject($"LaneLine_{i}");
            go.transform.SetParent(transform);

            var lr = go.AddComponent<LineRenderer>();
            lr.material = laneMaterial;
            lr.startWidth = lr.endWidth = keyboardMapper.keyLanes[i].isBlackKey ? 0.004f : 0.008f; // Thinner for black keys
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            laneLines[i] = lr;
        }
        UpdateLines();
    }

    // Update is called once per frame
    void UpdateLines()
    {
        for (int i = 0; i < laneLines.Length; i++)
        {
            Vector3 botton = keyboardMapper.keyLanes[i].worldPosition;
            Vector3 top = botton + Vector3.up * laneHeight;
            laneLines[i].SetPosition(0, botton);
            laneLines[i].SetPosition(1, top);
        }
    }
}

