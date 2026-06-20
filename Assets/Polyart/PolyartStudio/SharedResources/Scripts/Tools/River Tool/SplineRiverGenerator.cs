#if UNITY_EDITOR

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using System;


using UnityEditor;

namespace Polyart
{
    [Serializable]
    [ExecuteInEditMode]
    public class SplineRiverGenerator : MonoBehaviour
    {
        public SplineContainer splineContainer; // Reference to the SplineContainer
        private MeshRenderer meshRenderer;
        public MeshFilter meshFilter;
        public float tileLength = 1f, prevtTileLength; // Number of divisions along the spline
        public int widthSegments = 4, prevWidthSegments;
        public float width = 1f, prevWidth; // Width of the generated mesh
        [Range(0f, 0.05f)]
        public LayerMask terrainLayer; // For terrain snapping
        public Material material, prevMaterial;
        public bool traceForTerrain = true, prevTraceForTerrain;
        private Mesh mesh;
        private Vector3 prevTransform;
        private Quaternion prevRotation;
        private int prevHash;
        public Color[] prevVertCols;

        private bool shouldReGenerate = false;

        private void OnEnable()
        {
            meshRenderer = GetComponentInChildren<MeshRenderer>();
            splineContainer = GetComponent<SplineContainer>();
            meshFilter = GetComponentInChildren<MeshFilter>();
            prevTransform = transform.position;
            prevRotation = transform.rotation;
            prevHash = 0;
            terrainLayer = LayerMask.GetMask("Terrain");
        }

        private void OnValidate()
        {
            transform.GetChild(0).hideFlags = HideFlags.HideInHierarchy;
            shouldReGenerate = false;
        }

        public void Update()
        {
            // Skip if in play mode or about to enter/exit play mode
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (
                    prevTransform != transform.position || 
                    prevRotation != transform.rotation
                )
            {
                GenerateMesh(1f);

                prevTransform = transform.position;
                prevRotation = transform.rotation;
            }

            if (shouldReGenerate)
            {
                int currHash = CalculateSplineHash(splineContainer);
                if (prevHash != currHash)
                {
                    GenerateMesh(1f);
                }
                prevHash = currHash;
            }
            else
            {
                shouldReGenerate = true;
                prevHash = CalculateSplineHash(splineContainer); 
            }
        }

        public Mesh GenerateMesh(float resolutionDivisor)
        {

            if (splineContainer == null)
            {
                splineContainer = GetComponent<SplineContainer>();
            }

            // Ensure meshFilter is assigned before using it
            if (meshFilter == null)
            {
                meshFilter = gameObject.GetComponentInChildren<MeshFilter>();
            } 

            float splineLength = Mathf.Round(splineContainer.Spline.GetLength());
            int numTiles = (int)Mathf.Round(splineLength / (tileLength * resolutionDivisor));
            int currWidthSegments = (int)Mathf.Ceil((float)widthSegments / resolutionDivisor);
            float numUVTiles = splineLength / width;

            // Now we can safely assign the mesh to meshFilter
            mesh = new Mesh();

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> vertexColor = new List<Color>();
            List<Vector4> tangents = new List<Vector4>();
            
            float prevWidthLeft = width * 0.5f, prevWidthRight = width * 0.5f;

            float widthSegmentsLength = width / currWidthSegments;

            // Iterate through the spline path
            for (int i = 0; i <= numTiles; i++)
            {
                float t = i / (float)numTiles; // Normalized parameter (0 to 1)

                // Get the spline point in local space
                Vector3 localPoint = splineContainer.Spline.EvaluatePosition(t);
                Vector3 wp = transform.TransformPoint(localPoint);

                // Calculate the tangent in local space
                float3 tempTangent = splineContainer.Spline.EvaluateTangent(t);
                Vector4 tangent = new Vector4(tempTangent.x, tempTangent.y, tempTangent.z, 1f).normalized;
                Vector3 tangentWS = transform.TransformDirection(tangent);

                // Now calculate the normal based on the tangent in local space
                Vector3 normal = Vector3.Cross(tangent.normalized, Vector3.up).normalized;
                Vector3 normalWS = transform.TransformDirection(normal);

                Vector3 leftPoint1, rightPoint1;

                if (traceForTerrain)
                {
                    prevWidthRight = Mathf.Lerp(width * 0.5f, prevWidthRight, 0.999f);
                    prevWidthLeft = Mathf.Lerp(width * 0.5f, prevWidthLeft, 0.999f);

                    leftPoint1 = localPoint - normal * prevWidthLeft;
                    rightPoint1 = localPoint + normal * prevWidthRight;

                    RaycastHit hit;
                    if (Physics.Raycast(wp, -normalWS, out hit))
                    {
                        if (hit.distance < width * 0.5f && hit.transform.gameObject.layer == LayerMask.NameToLayer("Terrain"))
                        {
                            leftPoint1 = transform.InverseTransformPoint(hit.point);
                            prevWidthLeft = hit.distance;
                        }
                    }
                    if (Physics.Raycast(wp, normalWS, out hit))
                    {
                        if (hit.distance < width * 0.5f && hit.transform.gameObject.layer == LayerMask.NameToLayer("Terrain"))
                        {
                            rightPoint1 = transform.InverseTransformPoint(hit.point);
                            prevWidthRight = hit.distance;
                        }
                    }
                }
                else
                {
                    leftPoint1 = localPoint - normal * width * 0.5f;
                    rightPoint1 = localPoint + normal * width * 0.5f;
                }

                for (int j = 0; j <= currWidthSegments; j++)
                {
                    Vector3 leftPoint = Vector3.Lerp(leftPoint1, rightPoint1, j * (1f / currWidthSegments));

                    // Add vertices
                    vertices.Add(leftPoint);
                    // Add UVs
                    uvs.Add(new Vector2(j * (1f / currWidthSegments), t * numUVTiles));
                    // Add vertex color
                    //vertexColor.Add(new Color((tangentWS.x + 1f) * 0.5f, (tangentWS.z + 1f) * 0.5f, Vector3.Dot(tangentWS, Vector3.down), 0f));
                    //vertexColor.Add(new Color((tangentWS.x), (tangentWS.z), Vector3.Dot(tangentWS, Vector3.down), 0f));
                    vertexColor.Add(Color.black);
                    tangents.Add(tangent);
                }
                // Create triangles
                if (i < numTiles)
                {
                    int startIndex = i * (currWidthSegments + 1);

                    for (int j = 0; j < currWidthSegments; j++)
                    {
                        // First triangle
                        triangles.Add(startIndex + j + 1);
                        triangles.Add(startIndex + j + currWidthSegments + 1);
                        triangles.Add(startIndex + j);

                        // Second triangle
                        triangles.Add(startIndex + j + 1);
                        triangles.Add(startIndex + j + 1 + currWidthSegments + 1);
                        triangles.Add(startIndex + j + currWidthSegments + 1);
                    }
                }
            }

            // Apply data to mesh
            mesh.Clear();
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();

            mesh.colors = vertexColor.ToArray();
            if (prevVertCols != null)
            {
                if (prevVertCols.Length == mesh.vertices.Length)
                {
                    mesh.colors = prevVertCols;
                }
            }
            prevVertCols = mesh.colors;
            //mesh.colors = mesh.colors.Length == vertexColor.Count ? mesh.colors : vertexColor.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.tangents = tangents.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshFilter.mesh = mesh;

            // Create and assign a new material instance
            if (material != null)
            {
                if (meshRenderer == null)
                {
                    meshRenderer = GetComponentInChildren<MeshRenderer>();
                }
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                meshRenderer.material = material; // Assign the instance to the mesh
            }
            else
            {
                Debug.LogError("Material is null. Cannot create a River.");
            }

            return mesh;
        }

        private static int CalculateSplineHash(SplineContainer container)
        {
            int hash = 17;

            foreach (var spline in container.Splines)
            {
                foreach (var knot in spline.Knots)
                {
                    hash = hash * 23 + knot.Position.GetHashCode();
                    hash = hash * 23 + knot.Rotation.GetHashCode();
                }
            }

            return hash;
        }
        public Material GetMaterialInstance()
        {
            return material;
        }
    }

}

#else

using UnityEngine;

namespace Polyart
{
    public class SplineRiverGenerator : MonoBehaviour
    {

    }
}
#endif
