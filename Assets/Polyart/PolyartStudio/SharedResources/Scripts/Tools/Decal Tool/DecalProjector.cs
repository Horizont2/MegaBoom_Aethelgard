using UnityEngine;
using System.Reflection;
using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Polyart
{
    public class DecalProjector : MonoBehaviour
    {
        public int subdivisionsX = 4; // X-axis subdivisions
        public int subdivisionsZ = 4; // Z-axis subdivisions
        public float offset = 0.02f;
        public Material material;
        public LayerMask raycastLayer; // LayerMask for detecting objects below

        private Vector3 offsetVector;
        public MeshRenderer meshRenderer;
        public MeshFilter meshFilter;
        private Mesh mesh;
        private Vector3 center, bounds;
        private Vector3 prevPosition, prevScale;
        private Quaternion prevRotation;
        private float prevOffset, prevTilingX, prevTilingZ;
#if UNITY_EDITOR
        private void OnValidate()
        {
            //Hide renderer
            meshRenderer.gameObject.hideFlags = HideFlags.HideInHierarchy;

            center = transform.position;
            bounds = transform.localScale;
            offsetVector = transform.up * offset;
            if (subdivisionsX < 1) subdivisionsX = 1;
            if (subdivisionsZ < 1) subdivisionsZ = 1;


            EditorApplication.delayCall += () => GenerateMesh();
        }
#endif

        private void Start()
        {
            GenerateMesh();
        }

        void GenerateMesh()
        {
            if (this == null) return;

            if (!(transform.position != prevPosition || prevScale != transform.localScale || prevRotation != transform.rotation ||
                prevTilingX != subdivisionsX || prevTilingZ != subdivisionsZ || prevOffset != offset))
            {
                return;
            }

            prevPosition = transform.position;
            prevScale = transform.localScale;
            prevRotation = transform.rotation;
            prevTilingX = subdivisionsX;
            prevTilingZ = subdivisionsZ;
            prevOffset = offset;

            offsetVector = transform.up * offset;

            mesh = new Mesh();
            meshFilter.mesh = mesh;

            int vertsPerRow = subdivisionsX + 1;
            int vertsPerCol = subdivisionsZ + 1;
            int totalVerts = vertsPerRow * vertsPerCol;
            int totalTris = subdivisionsX * subdivisionsZ * 2 * 3; // 2 triangles per grid cell

            Vector3[] vertices = new Vector3[totalVerts];
            int[] triangles = new int[totalTris];
            Vector2[] uvs = new Vector2[totalVerts];
            Color[] vertexColors = new Color[totalVerts]; // New array to store colors for each vertex

            // Get the top face of the cube
            Vector3 cubeSize = transform.localScale;
            Vector3 topCenter = transform.position + transform.up * (cubeSize.y / 2f);
            Vector3 right = transform.right * cubeSize.x / 2f;
            Vector3 forward = transform.forward * cubeSize.z / 2f;

            // Generate vertices and project them downward
            for (int z = 0; z < vertsPerCol; z++)
            {
                for (int x = 0; x < vertsPerRow; x++)
                {
                    float percentX = (float)x / subdivisionsX;
                    float percentZ = (float)z / subdivisionsZ;

                    Vector3 startPoint = topCenter
                        - right + right * 2f * percentX  // X axis
                        - forward + forward * 2f * percentZ; // Z axis

                    Vector3 adjustedPoint = PerformRaycast(startPoint, out bool hit); // Adjust vertex position and get hit status

                    // Set color based on hit status
                    vertexColors[z * vertsPerRow + x] = hit ? Color.red : Color.black;

                    int index = z * vertsPerRow + x;
                    vertices[index] = transform.InverseTransformPoint(adjustedPoint); // Convert to local space
                    uvs[index] = new Vector2(percentX, percentZ);
                }
            }

            // Generate triangles
            int triIndex = 0;
            for (int z = 0; z < subdivisionsZ; z++)
            {
                for (int x = 0; x < subdivisionsX; x++)
                {
                    int bottomLeft = z * vertsPerRow + x;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + vertsPerRow;
                    int topRight = topLeft + 1;

                    // First Triangle
                    triangles[triIndex++] = bottomLeft;
                    triangles[triIndex++] = topLeft;
                    triangles[triIndex++] = topRight;

                    // Second Triangle
                    triangles[triIndex++] = bottomLeft;
                    triangles[triIndex++] = topRight;
                    triangles[triIndex++] = bottomRight;
                }
            }

            // Assign mesh data
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.colors = vertexColors; // Assign vertex colors to the mesh
            mesh.RecalculateNormals();

            meshRenderer.sharedMaterial = material;
        }

        private Vector3 PerformRaycast(Vector3 start, out bool hitStatus)
        {
            RaycastHit hit;
            Vector3 direction = -transform.up; // Raycast downwards

            if (Physics.Raycast(start, direction, out hit, bounds.y, raycastLayer))
            {
                hitStatus = true; // Ray hit something
                return hit.point + offsetVector; // Move vertex to hit point
            }
            else
            {
                hitStatus = false; // No hit
                                   // Calculate the world position of the bottom face
                Vector3 bottomPoint = transform.InverseTransformPoint(start);
                bottomPoint.y = -(bounds.y / 2f) / bounds.y;
                bottomPoint = transform.TransformPoint(bottomPoint);
                return bottomPoint; // Move vertex to the bottom face (considering rotation)
            }
        }

#if UNITY_EDITOR

        void OnDrawGizmos()
        {
            // Hide icon when selected
            if (Selection.activeGameObject == gameObject)
                return;

            // Optional fallback if no icon assigned
            Gizmos.DrawIcon(transform.position, "sv_icon_dot0_pix16_gizmo", true); // Unity built-in icon

        }

        private void ToggleSelectionGizmo(bool enabled)
        {
            Type AnnotationUtility = Type.GetType("UnityEditor.AnnotationUtility, UnityEditor");
            var ShowOutlineOption = AnnotationUtility.GetProperty("showSelectionOutline", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            ShowOutlineOption.SetValue(null, enabled);
        }
        private void OnDrawGizmosSelected()
        {
            if (meshRenderer != null)
            {
                EditorUtility.SetSelectedRenderState(meshRenderer, 0);
                ToggleSelectionGizmo(false);
            }

            center = transform.position;
            bounds = transform.localScale;

            GenerateMesh();

            Gizmos.color = Color.green;

            // Store original Gizmos matrix
            Matrix4x4 oldMatrix = Gizmos.matrix;

            // Apply object rotation & position to Gizmos
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

            // Draw rotated wire cube
            Gizmos.DrawWireCube(Vector3.zero, bounds);

            // Reset Gizmos matrix to avoid affecting other objects
            Gizmos.matrix = oldMatrix;

            // Compute arrow start and end points in WORLD space
            Vector3 arrowStart = center - transform.up * (bounds.y / 2f);
            DrawArrow(arrowStart, 0.1f);
        }

        void DrawArrow(Vector3 from, float headSize)
        {
            // Compute arrow end position in WORLD space
            Vector3 to = from + -transform.up * Mathf.Clamp(bounds.y, 0.1f, 0.75f);

            // Draw main line
            Gizmos.DrawLine(from, to);

            // Compute arrowhead directions in WORLD space
            Vector3 direction = (to - from).normalized;
            Vector3 right = Quaternion.AngleAxis(150, transform.right) * direction;
            Vector3 left = Quaternion.AngleAxis(-150, transform.right) * direction;

            // Draw arrowhead lines
            Gizmos.DrawLine(to, to + right * headSize);
            Gizmos.DrawLine(to, to + left * headSize);
        }
#endif
    }
}

#if UNITY_EDITOR

namespace Polyart
{

    [CustomEditor(typeof(DecalProjector))]
    public class DecalProjectorCustomInspector : Editor
    {
        private DecalProjector decal;

        private bool painting = false;
        private bool wasPainting;
        private Vector3 lastMousePosition;

        private float brushRadius = 0.5f;
        private float brushIntensity = 0.5f;
        private float brushFalloff = 0.5f;

        private Stack<Mesh> meshStateStack = new Stack<Mesh>(); // Stack to store mesh states

        public override void OnInspectorGUI()
        {
            GUILayout.Space(15);

            GUILayout.Label("Decal", CustomInspectorsHelper.LargeLabelStyle);

            EditorGUILayout.Space(20, true);

            GUIStyle medLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Normal,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            EditorGUILayout.LabelField("Mesh Parameters", medLabelStyle);

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("subdivisionsX"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("subdivisionsZ"));
            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("offset"));
            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("material"));
            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("raycastLayer"));
            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();

            GUILayout.Space(5);

            GUILayout.Space(15);
            GUIStyle headerStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("Decal Paint Tool", medLabelStyle);
            EditorGUILayout.Space(8);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0f, Mathf.Min(decal.transform.localScale.x, decal.transform.localScale.x) / 2);
            brushIntensity = EditorGUILayout.Slider("Brush Strength", brushIntensity, -1f, 1f);
            brushFalloff = EditorGUILayout.Slider("Brush Falloff", brushFalloff, 0f, 1f);
            GUILayout.Space(3);
            if (GUILayout.Button(painting ? "Painting" : "Not Painting", painting ? CustomInspectorsHelper.EnabledButtonStyle : CustomInspectorsHelper.DisabledButtonStyle))
            {
                painting = !painting;
                SceneView.RepaintAll();
            }
            GUILayout.Space(5);
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);
            serializedObject.ApplyModifiedProperties();
        }

        void OnEnable()
        {
            decal = (DecalProjector)target;
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
            MeshFilter meshFilter = decal.meshFilter;
            Mesh mesh = decal.meshFilter.sharedMesh;
            if (meshFilter || mesh) return;

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
            MeshFilter meshFilter = decal.meshFilter;
            Mesh mesh = decal.meshFilter.sharedMesh;
            if (meshFilter || mesh) return;

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

            Undo.RegisterCompleteObjectUndo(meshFilter.sharedMesh, "Decal Mesh Paint"); // Register Unity's built-in undo system
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (!painting) return;

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            Event e = Event.current;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Transform meshTransform = decal.transform;
            Mesh mesh = decal.meshFilter.sharedMesh;

            if (RaycastMesh(ray, mesh, meshTransform, out RaycastHit hit))
            {
                Handles.color = Color.green;
                Handles.DrawWireDisc(hit.point, hit.normal, brushRadius);
                Handles.DrawLine(hit.point, hit.point + hit.normal, 3f);

                Vector3[] vertices = mesh.vertices;
                float vertexRadius = 0.02f;

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
            if (a > -Mathf.Epsilon && a < Mathf.Epsilon) return false;

            float f = 1.0f / a;
            Vector3 s = ray.origin - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0.0f || u > 1.0f) return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(ray.direction, q);
            if (v < 0.0f || u + v > 1.0f) return false;

            distance = f * Vector3.Dot(edge2, q);
            if (distance > Mathf.Epsilon)
            {
                hitPoint = ray.origin + ray.direction * distance;
                return true;
            }
            return false;
        }

        void Paint(Vector3 hitPoint, Vector3 direction)
        {
            Mesh mesh = decal.meshFilter.sharedMesh;
            if (mesh == null) return;

            Vector3[] vertices = mesh.vertices;
            Color[] colors = mesh.colors.Length == vertices.Length ? mesh.colors : new Color[vertices.Length];
            Transform meshTransform = decal.transform;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPos = meshTransform.TransformPoint(vertices[i]);
                float distance = Vector3.Distance(hitPoint, worldPos);
                if (distance < brushRadius)
                {
                    float falloff = Mathf.Clamp01(1f - (distance / brushRadius)) * brushFalloff;

                    colors[i].g = Mathf.Clamp01(Mathf.Lerp(colors[i].g, colors[i].g + (brushIntensity * 0.1f), falloff));
                }
            }
            mesh.colors = colors;
            mesh.MarkModified();
        }

    }
}
#endif
