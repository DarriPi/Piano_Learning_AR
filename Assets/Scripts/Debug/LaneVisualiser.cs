using UnityEngine;

public class LaneVisualiser : MonoBehaviour
{
    [SerializeField] private KeyboardMapper mapper;
    [SerializeField] private Material laneMaterial;
    //[SerializeField] private float laneLineLength = 2.0f; // How far behind keyboard lines extend

    private LineRenderer[] laneLines;

    void Start()
    {
        laneLines = new LineRenderer[76];
        for (int i = 0; i < 76; i++)
        {
            var go = new GameObject($"LaneLine_{i}");
            go.transform.SetParent(transform);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = laneMaterial;
            lr.startWidth = lr.endWidth = mapper.keyLanes[i].isBlackKey ? 0.004f : 0.007f;
            lr.positionCount = 2;
            laneLines[i] = lr;
        }
        UpdateLines();
    }

    public void RefreshLanes()
    {
        UpdateLines();
    }

    void UpdateLines()
    {
        for (int i = 0; i < 76; i++)
        {
            // Hit position - where note lands at the key
            Vector3 hitPosition = mapper.keyLanes[i].worldPosition;

            // Spawn position - behind the keyboard along Z
            Vector3 spawnPosition = mapper.GetSpawnPosition(mapper.keyLanes[i].midiNote);

            laneLines[i].SetPosition(0, hitPosition);    // Front - player side
            laneLines[i].SetPosition(1, spawnPosition);  // Back - far side of keyboard
        }
    }
}