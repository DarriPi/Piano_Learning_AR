using UnityEngine;

public class NoteSpawner : MonoBehaviour
{
    public Transform[] keyPositions;
    public GameObject notePrefab;
    public float spawnHeight = 0.3f;

    void Start()
    {
        InvokeRepeating(nameof(SpawnRandomNote), 1f, 1f);
    }

    void SpawnRandomNote()
    {
        int index = Random.Range(0, keyPositions.Length);

        Vector3 spawnPos = keyPositions[index].position + Vector3.up * spawnHeight;

        Instantiate(notePrefab, spawnPos, Quaternion.identity);
    }

    void Awake()
    {
        keyPositions = GetComponentsInChildren<Transform>();
    }
}