using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class SmoothNormalsBaker
{
    [MenuItem("Tools/Bake Smooth Normals")]
    static void BakeSmoothNormals()
    {
        int count = 0;
        foreach (var go in Selection.gameObjects)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            if (!mf.sharedMesh.name.EndsWith("_smoothed"))
            {
                var copy = Object.Instantiate(mf.sharedMesh);
                copy.name = mf.sharedMesh.name + "_smoothed";
                mf.sharedMesh = copy;
            }

            Bake(mf.sharedMesh);
            count++;
        }

        Debug.Log($"Baked smooth normals on {count} mesh(es).");
    }

    static void Bake(Mesh mesh)
    {
        Vector3[] verts = mesh.vertices;
        Vector3[] normals = mesh.normals;
        var smoothed = new Vector3[verts.Length];

        var map = new Dictionary<Vector3, List<int>>();
        for (int i = 0; i < verts.Length; i++)
        {
            if (!map.TryGetValue(verts[i], out var list))
                map[verts[i]] = list = new List<int>();
            list.Add(i);
        }

        foreach (var list in map.Values)
        {
            var avg = Vector3.zero;
            foreach (var i in list) avg += normals[i];
            avg.Normalize();
            foreach (var i in list) smoothed[i] = avg;
        }

        var tangents = new Vector4[verts.Length];
        for (int i = 0; i < verts.Length; i++)
            tangents[i] = new Vector4(smoothed[i].x, smoothed[i].y, smoothed[i].z, 1f);

        mesh.tangents = tangents;
    }
}
