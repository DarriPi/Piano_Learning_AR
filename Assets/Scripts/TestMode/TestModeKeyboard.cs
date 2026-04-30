using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class TestModeKeyboard : MonoBehaviour
{
    private int totalKeys = 76; // Total number of keys in the keyboard (A0 to E7)

    [SerializeField] private KeyboardMapper keyboardMapper; // Prefab for the test mode keyboard
    [SerializeField] private Material whiteKeyMaterial; // Material for the white keys
    [SerializeField] private Material blackKeyMaterial; // Material for the black keys
    [SerializeField] private Material pressedKeyMaterial; // Material for the pressed keys

    // Dictionary to hold key objects for easy access and track which keys are currently pressed
    private Dictionary<int, GameObject> keyObjects = new Dictionary<int, GameObject>(); // Dictionary to hold key objects for easy access
    private Dictionary<int, bool> keyPressed = new Dictionary<int, bool>(); // Dictionary to track which keys are currently pressed

    private const float WHITE_W = 0.023f, WHITE_H = 0.015f, WHITE_D = 0.12f;
    private const float BLACK_W = 0.014f, BLACK_H = 0.020f, BLACK_D = 0.075f;

    void Start()
    {
        BuildVirtualKeyboard();
    }

    // This method will build the virtual keyboard 
    // based on the provided dimensions and materials
    void BuildVirtualKeyboard()
    {
        for (int i = 0; i < 76; i++)
        {
            var lane = keyboardMapper.keyLanes[i];
            bool black = lane.isBlackKey;

            var keyGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            keyGO.name = $"Key_{lane.noteName}";
            keyGO.transform.SetParent(transform);

            Vector3 size = black
                ? new Vector3(BLACK_W, BLACK_H, BLACK_D)
                : new Vector3(WHITE_W, WHITE_H, WHITE_D);

            keyGO.transform.localScale = size;
            keyGO.transform.position = lane.worldPosition - Vector3.up * (WHITE_H / 2f);

            var mr = keyGO.GetComponent<MeshRenderer>();
            mr.material = black ? blackKeyMaterial : whiteKeyMaterial; // ← material not sharedMaterial

            keyObjects[lane.midiNote] = keyGO;
            keyPressed[lane.midiNote] = false;
        }
    }

    public void SimulateKeyPress(int midiNote, bool isDown)
    {
        if (!keyObjects.ContainsKey(midiNote)) return;
        keyPressed[midiNote] = isDown;
        var mr = keyObjects[midiNote].GetComponent<MeshRenderer>();
        mr.material = isDown  // ← material not sharedMaterial
            ? pressedKeyMaterial
            : (keyboardMapper.GetLane(midiNote).isBlackKey ? blackKeyMaterial : whiteKeyMaterial);
    }

    public bool IsKeyPressed(int midiNote)
    {
        return keyPressed.TryGetValue(midiNote, out bool isPressed) && isPressed; // Return true if the key is currently pressed
    }
}
