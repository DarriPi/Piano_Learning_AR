using UnityEngine;

public class QuickTestRunner : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            NoteSpawner.Instance.StartSong();

        if (Input.GetKeyDown(KeyCode.Escape))
            NoteSpawner.Instance.StopSong();
    }
}