using UnityEngine;

public class KeyboardGridGenerator : MonoBehaviour
{
    public int keyCount = 76;
    public float keyWidth = 0.02f; // tweak later
    public float spacing = 0.002f;

    public GameObject keyPrefab;

    void Start()
    {
        GenerateKeys();
    }

    void GenerateKeys()
    {
        float totalWidth = keyCount * (keyWidth + spacing);
        float startX = -totalWidth / 2f;

        for (int i = 0; i < keyCount; i++)
        {
            Vector3 pos = new Vector3(startX + i * (keyWidth + spacing), 0, 0);

            GameObject key = Instantiate(keyPrefab, transform);
            key.transform.localPosition = pos;
            key.name = "Key_" + i;
        }
    }
}