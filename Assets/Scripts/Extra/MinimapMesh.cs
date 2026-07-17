using UnityEngine;
using UnityEngine.AI;

public class MinimapMesh : MonoBehaviour
{
    [SerializeField] 
    MeshFilter meshFilter;

    [ContextMenu("Bake NavMesh")]
    void BakeNavMesh()
    {
        //get navmesh data
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        //create new mesh with data
        Mesh mesh = new Mesh();
        mesh.vertices = triangulation.vertices;
        mesh.triangles = triangulation.indices;
        mesh.RecalculateNormals();
        // set to mesh filter
        meshFilter.mesh = mesh;
    }
}