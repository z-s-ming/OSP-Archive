#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class TerrainToMeshExporter : EditorWindow
{
    private int sampleStep = 1;
    private bool addMeshCollider = true;
    private bool disableOriginalTerrain = true;

    [MenuItem("Tools/RDW/Convert Selected Terrain To Mesh")]
    public static void ShowWindow()
    {
        GetWindow<TerrainToMeshExporter>("Terrain To Mesh");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Convert Unity Terrain to Mesh", EditorStyles.boldLabel);

        sampleStep = EditorGUILayout.IntSlider(
            "Sample Step",
            sampleStep,
            1,
            16
        );

        addMeshCollider = EditorGUILayout.Toggle(
            "Add Mesh Collider",
            addMeshCollider
        );

        disableOriginalTerrain = EditorGUILayout.Toggle(
            "Disable Original Terrain",
            disableOriginalTerrain
        );

        EditorGUILayout.Space();

        if (GUILayout.Button("Convert Selected Terrain"))
        {
            ConvertSelectedTerrain();
        }

        EditorGUILayout.HelpBox(
            "Sample Step = 1 keeps the original terrain heightmap resolution. " +
            "Larger values generate fewer vertices and better runtime performance.",
            MessageType.Info
        );
    }

    private void ConvertSelectedTerrain()
    {
        GameObject selected = Selection.activeGameObject;

        if (selected == null)
        {
            Debug.LogError("Please select a Terrain GameObject first.");
            return;
        }

        Terrain terrain = selected.GetComponent<Terrain>();

        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogError("Selected GameObject does not contain a valid Terrain component.");
            return;
        }

        TerrainData data = terrain.terrainData;

        Mesh mesh = GenerateMeshFromTerrain(data, sampleStep);
        mesh.name = selected.name + "_Mesh";

        string folder = "Assets/GeneratedTerrainMeshes";
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        string meshPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{folder}/{mesh.name}.asset"
        );

        AssetDatabase.CreateAsset(mesh, meshPath);
        AssetDatabase.SaveAssets();

        GameObject meshObject = new GameObject(selected.name + "_Mesh");
        Undo.RegisterCreatedObjectUndo(meshObject, "Create Terrain Mesh");

        meshObject.transform.SetParent(selected.transform.parent, false);
        meshObject.transform.localPosition = selected.transform.localPosition;
        meshObject.transform.localRotation = selected.transform.localRotation;
        meshObject.transform.localScale = selected.transform.localScale;
        meshObject.layer = selected.layer;
        meshObject.tag = selected.tag;

        MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();

        // 优先使用 Terrain 的 materialTemplate
        if (terrain.materialTemplate != null)
        {
            meshRenderer.sharedMaterial = terrain.materialTemplate;
        }
        else
        {
            // 如果你之前手动给 Terrain 加了 MeshRenderer，则复制它的材质
            MeshRenderer oldRenderer = selected.GetComponent<MeshRenderer>();
            if (oldRenderer != null && oldRenderer.sharedMaterial != null)
            {
                meshRenderer.sharedMaterial = oldRenderer.sharedMaterial;
            }
            else
            {
                meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
            }
        }

        if (addMeshCollider)
        {
            MeshCollider meshCollider = meshObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
        }

        if (disableOriginalTerrain)
        {
            terrain.enabled = false;

            TerrainCollider terrainCollider = selected.GetComponent<TerrainCollider>();
            if (terrainCollider != null)
            {
                terrainCollider.enabled = false;
            }
        }

        Selection.activeGameObject = meshObject;

        Debug.Log($"Terrain converted to mesh: {meshPath}");
    }

    private static Mesh GenerateMeshFromTerrain(TerrainData data, int step)
    {
        int heightmapResolution = data.heightmapResolution;
        Vector3 terrainSize = data.size;

        List<int> xSamples = BuildSampleIndices(heightmapResolution, step);
        List<int> zSamples = BuildSampleIndices(heightmapResolution, step);

        int vertexCountX = xSamples.Count;
        int vertexCountZ = zSamples.Count;

        float[,] heights = data.GetHeights(
            0,
            0,
            heightmapResolution,
            heightmapResolution
        );

        Vector3[] vertices = new Vector3[vertexCountX * vertexCountZ];
        Vector2[] uvs = new Vector2[vertices.Length];

        for (int z = 0; z < vertexCountZ; z++)
        {
            for (int x = 0; x < vertexCountX; x++)
            {
                int hmX = xSamples[x];
                int hmZ = zSamples[z];

                float normalizedX = hmX / (float)(heightmapResolution - 1);
                float normalizedZ = hmZ / (float)(heightmapResolution - 1);

                float worldX = normalizedX * terrainSize.x;
                float worldZ = normalizedZ * terrainSize.z;
                float worldY = heights[hmZ, hmX] * terrainSize.y;

                int index = z * vertexCountX + x;
                vertices[index] = new Vector3(worldX, worldY, worldZ);
                uvs[index] = new Vector2(normalizedX, normalizedZ);
            }
        }

        List<int> triangles = new List<int>();

        for (int z = 0; z < vertexCountZ - 1; z++)
        {
            for (int x = 0; x < vertexCountX - 1; x++)
            {
                int v00 = z * vertexCountX + x;
                int v10 = z * vertexCountX + x + 1;
                int v01 = (z + 1) * vertexCountX + x;
                int v11 = (z + 1) * vertexCountX + x + 1;

                // 保证法线朝上
                triangles.Add(v00);
                triangles.Add(v01);
                triangles.Add(v10);

                triangles.Add(v10);
                triangles.Add(v01);
                triangles.Add(v11);
            }
        }

        Mesh mesh = new Mesh();

        if (vertices.Length > 65535)
        {
            mesh.indexFormat = IndexFormat.UInt32;
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles.ToArray();

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static List<int> BuildSampleIndices(int resolution, int step)
    {
        List<int> indices = new List<int>();

        for (int i = 0; i < resolution; i += step)
        {
            indices.Add(i);
        }

        int last = resolution - 1;
        if (indices[indices.Count - 1] != last)
        {
            indices.Add(last);
        }

        return indices;
    }
}
#endif