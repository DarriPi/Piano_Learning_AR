using UnityEngine;
using System.Collections.Generic;
public class NotePool : MonoBehaviour
{
    public static NotePool Instance { get; private set; }
    [SerializeField] private GameObject notePrefab;
    [SerializeField] private int initialPoolSize = 128;
    private Queue<NoteObject> pool = new();
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        for (int i = 0; i < initialPoolSize; i++)
            CreateNote();
    }
    private NoteObject CreateNote()
    {
        var go = Instantiate(notePrefab, transform);
        var obj = go.GetComponent<NoteObject>();
        go.SetActive(false);
        pool.Enqueue(obj);
        return obj;
    }
    public NoteObject Get()
    {
        NoteObject note = pool.Count > 0 ? pool.Dequeue() : CreateNote();
        note.gameObject.SetActive(true);
        return note;
    }
    public void Return(NoteObject note)
    {
        note.gameObject.SetActive(false);
        note.transform.SetParent(transform);
        pool.Enqueue(note);
    }
}