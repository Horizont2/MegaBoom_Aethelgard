#if UNITY_EDITOR

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using System;
using UnityEditor.Splines;
using System.IO;

namespace Polyart
{

    using UnityEditor;
    using UnityEditor.EditorTools;


    [ExecuteInEditMode]
    public class PathTool : MonoBehaviour
    {
        public SplineContainer splineContainer; // Reference to the SplineContainer
        private MeshRenderer meshRenderer;
        public MeshFilter meshFilter;
        public int lengthSegments = 50; // Number of divisions along the spline
        public int widthSegments = 4;
        public float width = 2f, tileLength = 1f; // Width of the generated mesh
        [Range(0f, 0.05f)]
        public float verticalOffset = 0.01f;
        public LayerMask collisionTracingLayerMask; // For terrain snapping
        public Material material;
        private Material prevMaterial = null;
        private Material materialInstance;
        public float raycastHeight = 50f; // Height for terrain raycasting
        public float raycastDistance = 100f; // Distance for terrain raycasting
        private Mesh mesh;
        private Vector3 prevTransform;
        private Quaternion prevRotation;
        private int prevHash;
        private Vector3 offsetVector;
        [Range(0f, 0.04f)]
        public float initialTerrainOffset = 0.02f;
        public bool mirrorUV = false;
        float splineLength;

        private void Reset()
        {
            splineContainer = GetComponent<SplineContainer>();
            GenerateInitialMesh();
        }

        private void OnEnable()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            splineContainer = GetComponent<SplineContainer>();
            meshFilter = GetComponent<MeshFilter>();
            prevTransform = transform.position;
            prevRotation = transform.rotation;
            prevHash = 0;
        }

        private void OnValidate()
        {
            if (material != prevMaterial)
            {
                SetMaterial();

                prevMaterial = material;
            }

            offsetVector = new Vector3(0f, initialTerrainOffset, 0f);

            SetMaterialParameters();


            // Delay mesh generation until after validation is complete
            EditorApplication.delayCall += () =>
            {
                if (this != null) // Ensure the object still exists
                    GenerateInitialMesh();
            };

        }

        public void Update()
        {
            if (prevTransform != transform.position || prevRotation != transform.rotation)
            {
                GenerateInitialMesh();
            }
            prevTransform = transform.position;
            prevRotation = transform.rotation;

            int currHash = CalculateSplineHash(splineContainer);
            if (prevHash != currHash)
            {
                GenerateInitialMesh();

                SetMaterialParameters();
            }
            prevHash = currHash;
        }

        public void GenerateInitialMesh()
        {
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();
            }
            meshFilter.sharedMesh = GenerateMesh(1f);
            SetMaterial();
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
                meshFilter = gameObject.GetComponent<MeshFilter>();
            }

            // Now we can safely assign the mesh to meshFilter
            Mesh mesh = new Mesh();

            splineLength = Mathf.Round(splineContainer.Spline.GetLength());
            lengthSegments = (int)Mathf.Round(splineLength / (tileLength * resolutionDivisor));
            int currWidthSegments = (int)Mathf.Ceil((float)widthSegments / resolutionDivisor);

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector4> tangents = new List<Vector4>();

            // Iterate through the spline path
            for (int i = 0; i <= lengthSegments; i++)
            {
                float t = i / (float)lengthSegments; // Normalized parameter (0 to 1)

                // Get the spline point in local space
                Vector3 localPoint = splineContainer.Spline.EvaluatePosition(t);

                // Calculate the tangent in local space
                float3 tempTangent = splineContainer.Spline.EvaluateTangent(t);
                Vector3 tangent = new Vector3(tempTangent.x, tempTangent.y, tempTangent.z).normalized;

                // Now calculate the normal based on the tangent in local space
                Vector3 normal = Vector3.Cross(tangent, Vector3.up).normalized;


                float widthSegmentsLength = width / currWidthSegments;
                bool widthSegmentsOdd = currWidthSegments % 2 == 1;

                // Define left and right points for the width of the mesh
                Vector3 leftPoint1 = localPoint - normal * width * 0.5f;
                Vector3 rightPoint1 = localPoint + normal * width * 0.5f;

                for (int j = 0; j <= currWidthSegments; j++)
                {
                    Vector3 leftPoint = Vector3.Lerp(leftPoint1, rightPoint1, j * (1f / currWidthSegments));

                    leftPoint = transform.TransformPoint(leftPoint);

                    leftPoint.y = SampleTerrainHeight(leftPoint);

                    leftPoint = transform.InverseTransformPoint(leftPoint);

                    // Add vertices
                    vertices.Add(leftPoint + offsetVector);

                    tangents.Add(new Vector4(tempTangent.x, tempTangent.y, tempTangent.z, 1));

                    // Add UVs
                    //uvs.Add(new Vector2(j * (1f / widthSegments), splineContainer.CalculateLength() * t /width));
                    uvs.Add(new Vector2(j * (1f / currWidthSegments), t));
                }
                // Create triangles
                if (i < lengthSegments)
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

            Color[] colors = new Color[vertices.Count];

            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = Color.black; // Default color
            }

            // Apply data to mesh
            mesh.Clear();
            mesh.vertices = vertices.ToArray();
            mesh.tangents = tangents.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.colors = colors;
            mesh.uv = uvs.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        private void SetMaterial()
        {
            // Create and assign a new material instance
            if (material != null)
            {
                if (material != prevMaterial)
                {
                    if (meshRenderer == null)
                    {
                        meshRenderer = GetComponent<MeshRenderer>();
                    }

                    meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                    meshRenderer.material = material;

                    SetMaterialParameters();
                }
            }
            else
            {
                Debug.LogWarning("Path Material is null.");
            }
        }

        private void SetMaterialParameters()
        {
            MaterialPropertyBlock mpc = new MaterialPropertyBlock();

            if (mirrorUV)
            {
                mpc.SetFloat("_Tiling_X", 2f);
            }
            else
            {
                mpc.SetFloat("_Tiling_X", 1f);
            }

            bool hasTex = false;

            if (material != null)
            {
                if (material.HasTexture("_Base_Color"))
                {
                    Texture tex = material.GetTexture("_Base_Color");
                    if (tex != null)
                    {
                        float ratio = tex.height / tex.width;
                        mpc.SetFloat("_Tiling", (splineLength / ratio) / width);
                        hasTex = true;
                    }
                }
            }

            if (!hasTex)
            {
                mpc.SetFloat("_Tiling", splineLength / width);
            }
            mpc.SetFloat("_Length", splineLength);
            
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();

            if (meshRenderer != null)
            {
                meshRenderer.SetPropertyBlock(mpc);
            }
        }

        float SampleTerrainHeight(Vector3 point)
        {
            RaycastHit hit;
            Vector3 origin = point + Vector3.up * raycastHeight;

            if (Physics.Raycast(origin, Vector3.down, out hit, raycastDistance, collisionTracingLayerMask))
            {
                return hit.point.y;
            }
            return point.y;
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
            return materialInstance;
        }
    }

    [CustomEditor(typeof(PathTool))]
    public class PathToolEditor : Editor
    {
        private enum PaintMode
        {
            RaiseVertically,
            TexturePaint,
            EreaseHeight,
            EreaseTexturePaint
        }

        private PathTool pathTool;
        private bool painting = false;
        private bool showMeshResolution;
        bool pathSettings;
        bool editSpline;
        private bool wasPainting;
        private Vector3 lastMousePosition;
        private PaintMode paintMode = PaintMode.RaiseVertically;

        private float brushRadius = 0.5f;
        private float brushIntensity = 0.5f;
        private float brushFalloff = 0.5f;

        private Stack<Mesh> meshStateStack = new Stack<Mesh>(); // Stack to store mesh states

        private string savePath = "Assets/Polyart/PolyartStudio/Farmlands/Test/SteliosTests/Paths", saveName = "Path";

        void OnEnable()
        {
            pathTool = (PathTool)target;
            showMeshResolution = true;
            pathSettings = true;
            editSpline = false;

            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo; // Listen for undo/redo
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo; // Remove listener
        }

        void OnUndoRedo()
        {
            MeshFilter meshFilter = pathTool.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) return;

            Mesh mesh = meshFilter.sharedMesh;

            if (meshStateStack.Count > 0)
            {
                Mesh previousMesh = meshStateStack.Pop(); // Get the last saved mesh

                // Restore the mesh state
                mesh.vertices = previousMesh.vertices;
                mesh.normals = previousMesh.normals;
                mesh.tangents = previousMesh.tangents;
                mesh.colors = previousMesh.colors;
                mesh.uv = previousMesh.uv;
                mesh.triangles = previousMesh.triangles;

                mesh.MarkModified(); // Mark the mesh as modified
                meshFilter.sharedMesh = mesh; // Force Unity to update the mesh
            }
            Debug.Log(meshStateStack.Count);
            SceneView.RepaintAll(); // Refresh Scene View
        }

        void RegisterUndo()
        {
            MeshFilter meshFilter = pathTool.GetComponentInChildren<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) return;

            Mesh mesh = meshFilter.sharedMesh;

            // Create a deep copy of the mesh
            Mesh meshCopy = new Mesh();
            meshCopy.vertices = mesh.vertices;
            meshCopy.normals = mesh.normals;
            meshCopy.tangents = mesh.tangents;
            meshCopy.colors = mesh.colors;
            meshCopy.uv = mesh.uv;
            meshCopy.triangles = mesh.triangles;

            // Store the deep copy in the stack
            meshStateStack.Push(meshCopy);

            Undo.RegisterCompleteObjectUndo(meshFilter.sharedMesh, "Path Mesh Paint"); // Register Unity's built-in undo system
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (!painting) return;

            // Prevent default Scene View interaction
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            Event e = Event.current;

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Transform meshTransform = pathTool.transform;
            Mesh mesh = pathTool.GetComponent<MeshFilter>().sharedMesh;

            if (RaycastMesh(ray, mesh, meshTransform, out RaycastHit hit))
            {
                Handles.color = Color.green;
                Handles.DrawWireDisc(hit.point, hit.normal, brushRadius);
                Handles.DrawLine(hit.point, hit.point + hit.normal, 3f);

                Vector3[] vertices = mesh.vertices;
                float vertexRadius = 0.05f;

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 worldPos = meshTransform.TransformPoint(vertices[i]);

                    float distance = Vector3.Distance(hit.point, worldPos);
                    if (distance < brushRadius)
                    {
                        // Get the camera position(this can be the scene camera or the main camera)
                        Camera sceneCamera = SceneView.currentDrawingSceneView.camera;
                        Vector3 cameraPosition = sceneCamera.transform.position;

                        // Calculate the vector pointing from the vertex to the camera
                        Vector3 toCamera = (cameraPosition - worldPos).normalized;

                        // Normalize the distance (clamp to [0, 1] range)
                        float falloff = Mathf.Clamp01(1f - (distance / brushRadius)) * brushFalloff;

                        // Use Lerp to interpolate the color between green and yellow based on distance
                        Color vertexColor = Color.Lerp(Color.green, Color.yellow, falloff);

                        // Set the handle color
                        Handles.color = vertexColor;

                        // Draw a solid disc with the normal pointing toward the camera
                        Handles.DrawSolidDisc(worldPos, toCamera, vertexRadius);
                    }
                }

                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    if (!wasPainting)
                    {
                        // Register undo when drag ends
                        RegisterUndo();
                        wasPainting = true;
                    }
                    Vector3 direction = (hit.point - lastMousePosition).normalized;
                    Paint(hit.point, direction);
                    lastMousePosition = hit.point;
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && e.button == 0)
                {
                    wasPainting = false;
                    e.Use();
                }
            }

            SceneView.RepaintAll();
        }

        void Paint(Vector3 hitPoint, Vector3 direction)
        {
            Mesh mesh = pathTool.GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null) return;

            Vector3[] vertices = mesh.vertices;
            Color[] colors = mesh.colors.Length == vertices.Length ? mesh.colors : new Color[vertices.Length];
            Transform meshTransform = pathTool.transform;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPos = meshTransform.TransformPoint(vertices[i]);
                float distance = Vector3.Distance(hitPoint, worldPos);
                if (distance < brushRadius)
                {
                    float falloff = Mathf.Clamp01(1f - (distance / brushRadius)) * brushFalloff;

                    switch (paintMode)
                    {
                        case PaintMode.RaiseVertically:
                            colors[i].r = Mathf.Clamp01(Mathf.Lerp(colors[i].r, colors[i].r + (brushIntensity * 0.1f), falloff));
                            break;
                        case PaintMode.EreaseHeight:
                            colors[i].r = Mathf.Clamp01(Mathf.Lerp(colors[i].r, colors[i].r - (brushIntensity * 0.1f), falloff));
                            break;
                        case PaintMode.TexturePaint:
                            colors[i].g = Mathf.Clamp01(Mathf.Lerp(colors[i].g, colors[i].g + (brushIntensity * 0.1f), falloff));
                            break;
                        case PaintMode.EreaseTexturePaint:
                            colors[i].g = Mathf.Clamp01(Mathf.Lerp(colors[i].g, colors[i].g - (brushIntensity * 0.1f), falloff));
                            break;
                        default:
                            Debug.LogWarning("Path Tool Invalid Paint Mode!");
                            break;
                    }
                }
            }
            mesh.colors = colors;
            mesh.MarkModified();
        }

        bool RaycastMesh(Ray ray, Mesh mesh, Transform meshTransform, out RaycastHit hit)
        {
            hit = new RaycastHit();
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            float closestDistance = float.MaxValue;
            bool hasHit = false;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 v0 = meshTransform.TransformPoint(vertices[triangles[i]]);
                Vector3 v1 = meshTransform.TransformPoint(vertices[triangles[i + 1]]);
                Vector3 v2 = meshTransform.TransformPoint(vertices[triangles[i + 2]]);

                if (RayIntersectsTriangle(ray, v0, v1, v2, out Vector3 point, out float distance) && distance < closestDistance)
                {
                    closestDistance = distance;
                    hit.point = point;
                    hit.normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                    hasHit = true;
                }
            }

            return hasHit;
        }

        bool RayIntersectsTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out Vector3 hitPoint, out float distance)
        {
            hitPoint = Vector3.zero;
            distance = 0f;

            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float a = Vector3.Dot(edge1, h);

            if (a > -Mathf.Epsilon && a < Mathf.Epsilon)
                return false; // Ray is parallel to the triangle.

            float f = 1.0f / a;
            Vector3 s = ray.origin - v0;
            float u = f * Vector3.Dot(s, h);

            if (u < 0.0f || u > 1.0f)
                return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(ray.direction, q);

            if (v < 0.0f || u + v > 1.0f)
                return false;

            distance = f * Vector3.Dot(edge2, q);
            if (distance > Mathf.Epsilon)
            {
                hitPoint = ray.origin + ray.direction * distance;
                return true;
            }

            return false;
        }

        public override void OnInspectorGUI()
        {
            //DrawDefaultInspector();

            GUILayout.Space(15);

            GUIStyle largeLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 36,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            GUILayout.Label("Path Tool", largeLabelStyle);

            EditorGUILayout.Space(20, true);

            GUIStyle medLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Normal,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            EditorGUILayout.LabelField("Path Parameters", medLabelStyle);

            GUILayout.Space(1);

            showMeshResolution = EditorGUILayout.Foldout(showMeshResolution, "Mesh Resolution", true, EditorStyles.foldoutHeader);
            if (showMeshResolution)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("tileLength"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("widthSegments"));
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(5);

            pathSettings = EditorGUILayout.Foldout(pathSettings, "Path Settings", true, EditorStyles.foldoutHeader);
            if (pathSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("width"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("initialTerrainOffset"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("material"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mirrorUV"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("collisionTracingLayerMask"));
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(10);


            if (GUILayout.Button(editSpline ? "Stop Editing Spline" : "Edit Spline"))
            {
                editSpline = !editSpline;
                if (editSpline) ToolManager.SetActiveContext<SplineToolContext>();
                else ToolManager.SetActiveContext<GameObjectToolContext>();
            }

            GUILayout.Space(30);

            EditorGUILayout.LabelField("Paint Tool", medLabelStyle);

            GUILayout.Space(3);

            GUIStyle smallLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                wordWrap = true, // Enable text wrapping
                normal = { textColor = new UnityEngine.Color(0.9f, 0.9f, 0.9f) },
                alignment = TextAnchor.UpperLeft // Better alignment for wrapped text
            };

            // Create a box or area for the text to constrain width
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(
                "This is a brush tool that lets you paint over the parts where the path is clipping through the terrain.",
                smallLabelStyle
            );
            EditorGUILayout.EndVertical();

            GUILayout.Space(5);

            paintMode = (PaintMode)EditorGUILayout.EnumPopup("Paint Mode:", paintMode);

            GUILayout.Space(3);

            //heightOffset = EditorGUILayout.Slider("Height Offset", heightOffset, 0f, 0.1f);
            brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0f, pathTool.width / 2);
            brushIntensity = EditorGUILayout.Slider("Brush Strength", brushIntensity, 0f, 1f);
            brushFalloff = EditorGUILayout.Slider("Brush Falloff", brushFalloff, 0f, 1f);

            GUILayout.Space(5);

            if (GUILayout.Button(painting ? "Stop Painting" : "Start Painting"))
            {
                painting = !painting;
                SceneView.RepaintAll();
            }

            GUILayout.Space(30);

            // Apply changes to serialized object
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.LabelField("Save as Prefab", medLabelStyle);

            GUILayout.Space(3);

            saveName = EditorGUILayout.TextField("Prefab Name", saveName);
            savePath = EditorGUILayout.TextField("Output Path", savePath);

            GUILayout.Space(3);

            if (GUILayout.Button("Save Mesh"))
            {
                SaveAsset();
            }

            GUILayout.Space(50);
        }

        private void SaveAsset()
        {
            if (pathTool == null)
            {
                Debug.LogError("Path Tool is Invalid!");
                return;
            }

            MeshFilter meshFilter = pathTool.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = pathTool.GetComponent<MeshRenderer>();

            if (meshFilter == null || meshRenderer == null)
            {
                Debug.LogError("Path Tool has an invalid MeshFilter or MeshRenderer!");
                return;
            }

            // Ensure save path is valid
            if (!savePath.StartsWith("Assets/"))
            {
                Debug.LogError("Save path must be inside the Assets folder! Current path: " + savePath);
                return;
            }

            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }

            // Create a new GameObject for prefab
            GameObject newMeshPrefab = new GameObject(saveName);
            LODGroup lodGroup = newMeshPrefab.AddComponent<LODGroup>();

            List<LOD> lodLevels = new List<LOD>();
            List<GameObject> lodObjects = new List<GameObject>();

            // Generate LOD meshes
            Mesh[] lodMeshes = new Mesh[3];
            lodMeshes[0] = Instantiate(meshFilter.sharedMesh);   // LOD0: Original
            lodMeshes[1] = pathTool.GenerateMesh(1.5f);          // LOD1: Medium
            lodMeshes[2] = pathTool.GenerateMesh(2.5f);          // LOD2: Low

            Material[] clonedMaterials = new Material[1];

            if (pathTool.GetMaterialInstance() != null)
            {
                string materialAssetPath = Path.Combine(savePath, $"{saveName}_Material.mat");
                materialAssetPath = AssetDatabase.GenerateUniqueAssetPath(materialAssetPath);

                Material materialCopy = new Material(pathTool.GetMaterialInstance());
                AssetDatabase.CreateAsset(materialCopy, materialAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                clonedMaterials[0] = AssetDatabase.LoadAssetAtPath<Material>(materialAssetPath);
            }

            for (int i = 0; i < lodMeshes.Length; i++)
            {
                if (lodMeshes[i] == null)
                {
                    Debug.LogError($"LOD {i} mesh generation failed!");
                    return;
                }

                // Save the mesh as an asset
                string meshAssetPath = Path.Combine(savePath, $"{saveName}_LOD{i}.asset");
                meshAssetPath = AssetDatabase.GenerateUniqueAssetPath(meshAssetPath);
                AssetDatabase.CreateAsset(lodMeshes[i], meshAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // Create GameObject for LOD
                GameObject lodObject = new GameObject($"{saveName}_LOD{i}");
                lodObject.transform.parent = newMeshPrefab.transform;

                MeshFilter newMeshFilter = lodObject.AddComponent<MeshFilter>();
                MeshRenderer newMeshRenderer = lodObject.AddComponent<MeshRenderer>();
                newMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                newMeshFilter.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);

                newMeshRenderer.sharedMaterials = clonedMaterials;
                lodObjects.Add(lodObject);
            }

            // Assign LOD distances (LOD0 = close, LOD2 = far)
            LOD[] lods = new LOD[]
            {
        new LOD(0.6f, new Renderer[] { lodObjects[0].GetComponent<Renderer>() }), // LOD0: 60% visibility
        new LOD(0.3f, new Renderer[] { lodObjects[1].GetComponent<Renderer>() }), // LOD1: 30%
        new LOD(0.1f, new Renderer[] { lodObjects[2].GetComponent<Renderer>() })  // LOD2: 10%
            };

            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();

            newMeshPrefab.AddComponent<PathLODHeightMultiplier>();

            // Save as a prefab
            string fullPath = Path.Combine(savePath, saveName + ".prefab");
            fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);

            PrefabUtility.SaveAsPrefabAsset(newMeshPrefab, fullPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Mesh prefab with LODs saved to: " + fullPath);

            newMeshPrefab.transform.position = pathTool.transform.position;
            newMeshPrefab.transform.rotation = pathTool.transform.rotation;

            pathTool.gameObject.SetActive(false);
        }


    }


}
#else

using UnityEngine;

namespace Polyart
{
    public class PathTool : MonoBehaviour
    {

    }
}

#endif