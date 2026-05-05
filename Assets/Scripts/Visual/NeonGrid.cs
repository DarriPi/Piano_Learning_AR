using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class NeonGrid : MonoBehaviour
{
    [SerializeField] private KeyboardMapper mapper;
    [SerializeField] private float gridHeight = 0.01f;
    [SerializeField] private Material gridMaterial;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        BuildGrid();
    }

    void BuildGrid()
    {
        var mesh = new Mesh { name = "NeonGrid" };
        var verts = new List<Vector3>();
        var indices = new List<int>();

        for (int i = 0; i < mapper.keyLanes.Count(); i++)
        {
            Vector3 bottom = mapper.keyLanes[i].worldPosition;
            Vector3 top = bottom + Vector3.up * gridHeight;
            int vIdx = verts.Count;
            verts.Add(bottom);
            verts.Add(top);
            indices.Add(vIdx);
            indices.Add(vIdx + 1);
        }

        // Horizontal lines every 0.5m
        float leftX = mapper.keyLanes[0].worldPosition.x;
        float rightX = mapper.keyLanes.Last().worldPosition.x;
        float baseY = mapper.keyLanes[0].worldPosition.y;

        for (float h = 0f; h <= gridHeight; h += 0.5f)
        {
            int vIdx = verts.Count;
            verts.Add(new Vector3(leftX, baseY + h, 0f));
            verts.Add(new Vector3(rightX, baseY + h, 0f));
            indices.Add(vIdx);
            indices.Add(vIdx + 1);
        }

        mesh.SetVertices(verts);
        mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
        GetComponent<MeshFilter>().mesh = mesh;
        GetComponent<MeshRenderer>().sharedMaterial = gridMaterial;
    }
}
