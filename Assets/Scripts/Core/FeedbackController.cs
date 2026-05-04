using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class FeedbackController : MonoBehaviour
{
    public static FeedbackController Instance { get; private set; }

    [Header("Particle Systems")]
    [SerializeField] private ParticleSystem hitParticlePrefab;
    [SerializeField] private ParticleSystem missParticlePrefab;

    [Header("Materials")]
    [SerializeField] private KeyboardMapper mapper;
    [SerializeField] private Material keyHitMaterial;
    [SerializeField] private Material keyMissMaterial;

    private Dictionary<int, MeshRenderer> keyRenderers = new();
    private Dictionary<int, Coroutine> fadeCoroutines = new();
    private Dictionary<int, Material> originalMaterials = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void RegisterKeyRenderer(int midiNote, MeshRenderer mr, Material original)
    {
        keyRenderers[midiNote] = mr;
        originalMaterials[midiNote] = original;
    }

    public void TriggerHit(int midiNote)
    {
        SpawnParticle(hitParticlePrefab, midiNote);
        FlashKey(midiNote, keyHitMaterial);
    }

    public void TriggerMiss(int midiNote)
    {
        SpawnParticle(missParticlePrefab, midiNote);
        FlashKey(midiNote, keyMissMaterial);
    }

    private void FlashKey(int midiNote, Material flashMat)
    {
        if (!keyRenderers.ContainsKey(midiNote)) return;

        var mr = keyRenderers[midiNote];
        mr.material = flashMat;

        if (fadeCoroutines.TryGetValue(midiNote, out var co) && co != null)
            StopCoroutine(co);

        fadeCoroutines[midiNote] = StartCoroutine(RestoreKey(midiNote, 0.25f));
    }

    private IEnumerator RestoreKey(int midiNote, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (keyRenderers.ContainsKey(midiNote) && originalMaterials.ContainsKey(midiNote))
            keyRenderers[midiNote].material = originalMaterials[midiNote];
    }

    private void SpawnParticle(ParticleSystem prefab, int midiNote)
    {
        var lane = mapper.GetLane(midiNote);
        if (lane == null || prefab == null) return;

        var ps = Instantiate(prefab, lane.worldPosition, Quaternion.identity);
        ps.Play();
        Destroy(ps.gameObject, 1.5f);
    }
}