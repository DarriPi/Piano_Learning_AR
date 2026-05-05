using UnityEngine;
using TMPro;
using Unity.VisualScripting;

public class NoteLabelManager : MonoBehaviour
{
    [SerializeField] private KeyboardMapper mapper;
    [SerializeField] private TMP_FontAsset labelFont;
    [SerializeField] private bool showLabels = true;
    [SerializeField] private float labelYOffset = 0.05f;

    private GameObject[] labels = new GameObject[76];

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        for (int i = 0; i < 76; i++)
        {
            var lane = mapper.keyLanes[i];

            // Only label natural notes to avoid clutter
            if (lane.isBlackKey) continue;

            var go = new GameObject($"Label_{lane.noteName}");
            go.transform.SetParent(transform);
            go.transform.position = lane.worldPosition + Vector3.up * labelYOffset;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Face upwards

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = lane.noteName;
            tmp.font = labelFont;
            tmp.fontSize = 0.8f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.8f, 0.8f, 1f, 0.7f);
            labels[i] = go;
        }
    }

    public void SetLabelsVisible(bool visible)
    {
        showLabels = visible;
        foreach (var label in labels)
        {
            label?.SetActive(visible);
        }
    }
}
