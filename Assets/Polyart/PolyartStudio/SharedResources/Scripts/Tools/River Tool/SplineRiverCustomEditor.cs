#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.UIElements;

namespace Polyart
{

    [CustomEditor(typeof(SplineRiverGenerator))]
    public class SplineMesSplineRiverCustomEditorhPainter : Editor
    {
        private SplineRiverGenerator splineRiverGenerator;
        [Range(0f, 0.1f)]
        private float heightOffset = 0.02f;
        private float brushRadius = 0.5f;
        private float brushIntensity = 0.02f;
        private float brushFalloff = 0.5f;
        private bool painting = false;
        private Vector3 lastMousePosition;
        private bool showMeshResolution;
        bool riverSettings;
        bool editSpline;
        private PaintMode paintMode = PaintMode.NormalExtrusion;
        private WidthPaintDirection widthPaintDirection = WidthPaintDirection.Left;
        private LengthPaintDirection lengthPaintDirection = LengthPaintDirection.Down;
        private Stack<Mesh> meshStateStack = new Stack<Mesh>(); // Stack to store mesh states
        private bool wasPainting;

        private string savePath = "Assets/Polyart/PolyartStudio/Farmlands/Test/SteliosTests/Rivers", saveName = "Path";

        private enum PaintMode
        {
            WidthModifier,
            LengthModifier,
            NormalExtrusion,
            UpExtrusion,
            FoamPaint,
        }

        private enum WidthPaintDirection
        {
            Left, Right
        }

        private enum LengthPaintDirection
        {
            Down, Up
        }

        void OnEnable()
        {
            splineRiverGenerator = (SplineRiverGenerator)target;
            showMeshResolution = true;
            riverSettings = true;
            editSpline = false;
            editSpline = false;
            brushRadius = splineRiverGenerator.width / 2f;

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
            MeshFilter meshFilter = splineRiverGenerator.GetComponentInChildren<MeshFilter>();
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
            SceneView.RepaintAll(); // Refresh Scene View
        }

        void RegisterUndo()
        {
            MeshFilter meshFilter = splineRiverGenerator.GetComponentInChildren<MeshFilter>();
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

            Undo.RegisterCompleteObjectUndo(meshFilter.sharedMesh, "Mesh Paint"); // Register Unity's built-in undo system
        }


        void OnSceneGUI(SceneView sceneView)
        {
            if (!painting) return;

            // Prevent default Scene View interaction
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            Event e = Event.current;

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Transform meshTransform = splineRiverGenerator.transform;
            Mesh mesh = splineRiverGenerator.GetComponentInChildren<MeshFilter>().sharedMesh;

            if (RaycastMesh(ray, mesh, meshTransform, out RaycastHit hit))
            {
                float colorValue = heightOffset * 10f;
                Handles.color = Color.green;
                Handles.DrawWireDisc(hit.point, hit.normal, brushRadius, 3f); // Thicker outline

                // Draw normal line
                Handles.color = Color.green;
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
                        float normalizedDistance = Mathf.Clamp01(distance / brushRadius);

                        // Use Lerp to interpolate the color between green and yellow based on distance
                        Color vertexColor = Color.Lerp(Color.green, Color.yellow, normalizedDistance);

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
            Mesh mesh = splineRiverGenerator.GetComponentInChildren<MeshFilter>().sharedMesh;
            if (mesh == null) return;

            Vector3[] vertices = mesh.vertices;
            Color[] colors = mesh.colors.Length == vertices.Length ? mesh.colors : new Color[vertices.Length];
            Transform meshTransform = splineRiverGenerator.transform;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPos = meshTransform.TransformPoint(vertices[i]);
                float distance = Vector3.Distance(hitPoint, worldPos);
                if (distance < brushRadius)
                {
                    float falloff = Mathf.Clamp01(1f - (distance / brushRadius)) * brushFalloff;

                    switch (paintMode)
                    {
                        case PaintMode.NormalExtrusion:
                            vertices[i] = Vector3.Lerp(vertices[i], vertices[i] + (mesh.normals[i] * brushIntensity * 0.1f), falloff);
                            break;
                        case PaintMode.UpExtrusion:
                            vertices[i] = Vector3.Lerp(vertices[i], vertices[i] + (splineRiverGenerator.transform.up * brushIntensity * 0.1f), falloff);
                            break;
                        case PaintMode.WidthModifier:
                            Vector3 moveDir = Vector3.Cross((Vector3)mesh.tangents[i].normalized, mesh.normals[i].normalized).normalized;

                            vertices[i] = Vector3.Lerp(vertices[i], vertices[i] + (moveDir * brushIntensity * 0.1f * (widthPaintDirection == WidthPaintDirection.Left ? 1f : -1f) ), falloff); ;
                            break;
                        case PaintMode.LengthModifier:
                            vertices[i] = Vector3.Lerp(vertices[i], vertices[i] + ((Vector3)mesh.tangents[i].normalized * brushIntensity * 0.1f * (lengthPaintDirection == LengthPaintDirection.Down ? 1f : -1f) ), falloff); ;
                            break;
                        case PaintMode.FoamPaint:
                            colors[i].r = Mathf.Lerp(colors[i].r, brushIntensity, falloff);
                            break;
                    }
                }
            }
            mesh.vertices = vertices;
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
            if (splineRiverGenerator.splineContainer == null)
            {
                splineRiverGenerator = (SplineRiverGenerator)target;
            }
            Material prevMaterial = splineRiverGenerator.material;
            float prevWidth = splineRiverGenerator.width;
            float prevTileLength = splineRiverGenerator.tileLength;
            int prevWidthSegments = splineRiverGenerator.widthSegments;
            bool prevTraceForTerrain = splineRiverGenerator.traceForTerrain;

            GUILayout.Space(15);

            GUIStyle largeLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 36,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            GUILayout.Label("River Generator", largeLabelStyle);

            EditorGUILayout.Space(20, true);

            GUIStyle medLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Normal,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            EditorGUILayout.LabelField("River Parameters", medLabelStyle);

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

            riverSettings = EditorGUILayout.Foldout(riverSettings, "River Settings", true, EditorStyles.foldoutHeader);
            if (riverSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("width"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("material"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("traceForTerrain"));
                EditorGUI.indentLevel--;
            }
            /*
            GUILayout.Space(10);


            if (GUILayout.Button("Regenerate Mesh"))
            {
                splineRiverGenerator.GenerateMesh();
            }
            */

            GUILayout.Space(10);


            if (GUILayout.Button(editSpline ? "Stop Editing Spline" : "Edit Spline"))
            {
                editSpline = !editSpline;

                if (splineRiverGenerator.splineContainer != null)
                {
                    if (editSpline)
                    {
                        Selection.activeGameObject = splineRiverGenerator.splineContainer.gameObject; // Select the child
                        ToolManager.SetActiveContext<SplineToolContext>();
                    }
                    else
                    {
                        Selection.activeGameObject = splineRiverGenerator.gameObject; // Revert selection back to the parent
                        ToolManager.SetActiveContext<GameObjectToolContext>();
                    }
                }
                else
                {
                    Debug.LogWarning("No SplineContainer found in child objects.");
                }
            }

            GUILayout.Space(30);

            EditorGUILayout.LabelField("Fix Clipping Tool", medLabelStyle);

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
                "This is a brush tool that lets you modify the River Mesh. Select a Paint Mode and Start Painting.",
                smallLabelStyle
            );
            EditorGUILayout.EndVertical();
            GUILayout.Space(3);

            paintMode = (PaintMode)EditorGUILayout.EnumPopup("Paint Mode:", paintMode);

            //heightOffset = EditorGUILayout.Slider("Height Offset", heightOffset, 0f, 0.1f);
            brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0f, splineRiverGenerator.width / 2);
            switch (paintMode)
            {
                case PaintMode.WidthModifier:
                    widthPaintDirection = (WidthPaintDirection)EditorGUILayout.EnumPopup("Paint Direction:", widthPaintDirection);
                    break;
                case PaintMode.LengthModifier:
                    lengthPaintDirection = (LengthPaintDirection)EditorGUILayout.EnumPopup("Paint Direction:", lengthPaintDirection);
                    break;
            }
            brushIntensity = EditorGUILayout.Slider("Brush Strength", brushIntensity, -1f, 1f);
            brushFalloff = EditorGUILayout.Slider("Brush Falloff", brushFalloff, 0f, 1f);

            if (GUILayout.Button(painting ? "Stop Painting" : "Start Painting"))
            {
                painting = !painting;
                SceneView.RepaintAll();
            }

            GUILayout.Space(30);

            // Apply changes to serialized object
            serializedObject.ApplyModifiedProperties();

            if (splineRiverGenerator.material != prevMaterial ||
                splineRiverGenerator.traceForTerrain != prevTraceForTerrain ||
                splineRiverGenerator.tileLength != prevTileLength ||
                splineRiverGenerator.widthSegments != prevWidthSegments ||
                splineRiverGenerator.width != prevWidth)
            {
                splineRiverGenerator.GenerateMesh(1f );
            }

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
            if (splineRiverGenerator == null)
            {
                Debug.LogError("River Tool is Invalid!");
                return;
            }

            MeshFilter meshFilter = splineRiverGenerator.GetComponentInChildren<MeshFilter>();
            MeshRenderer meshRenderer = splineRiverGenerator.GetComponentInChildren<MeshRenderer>();

            if (meshFilter == null || meshRenderer == null)
            {
                Debug.LogError("River Tool has an invalid MeshFilter or MeshRenderer!");
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
            lodMeshes[1] = splineRiverGenerator.GenerateMesh(1.5f);          // LOD1: Medium
            lodMeshes[2] = splineRiverGenerator.GenerateMesh(2.5f);          // LOD2: Low

            Material[] clonedMaterials = new Material[1];

            if (splineRiverGenerator.GetMaterialInstance() != null)
            {
                string materialAssetPath = Path.Combine(savePath, $"{saveName}_Material.mat");
                materialAssetPath = AssetDatabase.GenerateUniqueAssetPath(materialAssetPath);

                Material materialCopy = new Material(splineRiverGenerator.GetMaterialInstance());
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

            // Save as a prefab
            string fullPath = Path.Combine(savePath, saveName + ".prefab");
            fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);

            PrefabUtility.SaveAsPrefabAsset(newMeshPrefab, fullPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Mesh prefab with LODs saved to: " + fullPath);

            newMeshPrefab.transform.position = splineRiverGenerator.transform.position;
            newMeshPrefab.transform.rotation = splineRiverGenerator.transform.rotation;

            splineRiverGenerator.gameObject.SetActive(false);
        }
    }

}
#endif