using UnityEngine;
using UnityEngine.Rendering;
using System;
using UnityEngine.EventSystems;
using Unity.VisualScripting;



#if UNITY_EDITOR
using UnityEditor;
#endif


namespace Polyart
{
    [ExecuteAlways]
    public class OceanTool : MonoBehaviour
    {
        private Vector3 prevPos;
        private Vector3 prevScale;

        public float preTessQuadSize = 20f;
        public float tessellationAmount = 50f;

        private MaterialPropertyBlock materialPropertyBlock;

        public Camera cam;

        private const float startingSize = 500;
        private Bounds oceanBounds, oceanBoundsWithDepth;

        public Material oceanMaterial;

        [Range(1, 32)]
        public int numWaves = 16;

        public WaveDirectionMode waveDirectionMode = WaveDirectionMode.LinearDirection;

        public Vector2 WavelengthMinMax = new Vector2(1f, 200f);
        public Vector2 heightMinMax = new Vector2(0.03f, 2f);
        public Vector2 OffsetMinMax = new Vector2(0f, 0.75f);
        public float shoreWPODampeningDistance = 11f;
        private float maxHeight;

        public float WavelengthFalloff = 1.0f;
        public float heightFalloff = 1.0f;
        public float OffsetFalloff = 1.0f;

        public Vector3 flowDirectionPosition; // Local-space ocean position
        public Texture2D flowDirectionPositionIcon;

        public float waterDepth = 50f;
        public BoxCollider boxCollider;

        bool isCurrentlyUnderwater = false;
        public Material underwaterMaterial;
        private Material underwaterMaterialInstance;

#if BuoyancyEnabled

        public ComputeShader buoyancyComputeShader;
        private const int maxFloatingObjectsNum = 512;
        private int buoyancyKernel;
        private Dictionary<float, Transform> floatingObjects = new Dictionary<float, Transform>();
        private Vector4[] buoyancyInput, buoyancyOutput;
        private ComputeBuffer objectPositionsBuffer, buoyancyResultsBuffer;

#endif

        public enum WaveDirectionMode
        {
            LinearDirection, 
            RadialDirection
        }

        private void Initialize()
        {
#if BuoyancyEnabled
            InitBuoyancy();
#endif
            InitMaterial();
            GeneratePlane();
            //GeneratePlane();
            InitCollision();
            cam = Camera.main;
            InitUnderwaterPostProcess();
        }

        private void InitUnderwaterPostProcess()
        {
            if (cam  == null)
            {
                Debug.LogError("Camera is NOT Valid for the Underwater Post Processing");
                return;
            }

            OceanUnderwaterPostProcessing oceanUnderwaterPostProcessing = cam.GetComponent<OceanUnderwaterPostProcessing>();
            if (oceanUnderwaterPostProcessing == null)
                oceanUnderwaterPostProcessing = cam.AddComponent<OceanUnderwaterPostProcessing>();

            oceanUnderwaterPostProcessing.oceanTool = this;
        }

        private void InitMaterial()
        {
            if (oceanMaterial == null)
            {
                Debug.LogError("Ocean Material is NOT Valid!");
                return;
            }

            if (underwaterMaterial == null)
            {
                Debug.LogError("Underwater Material is NOT Valid!");
                return;
            }

            underwaterMaterialInstance = new Material(underwaterMaterial);

            if (oceanMaterial.HasFloat("_ShoreDistanceWPODampening"))
                underwaterMaterialInstance.SetFloat("_ShoreDistanceWPODampening", oceanMaterial.GetFloat("_ShoreDistanceWPODampening"));

            Vector4[] waveData = new Vector4[32];

            System.Random rand = new System.Random(1234);

            maxHeight = 0;

            for (int i = 0; i < numWaves; i++)
            {
                float baseAlpha = 1f - (float)i / numWaves;

                // Add controlled randomness to alpha
                float randOffset = (float)(rand.NextDouble() * 2.0 - 1.0) * (0.1f /* this can be made a parameter to control randomness */ / numWaves);
                float alpha = Mathf.Clamp01(baseAlpha + randOffset);

                float wavelength = Mathf.Lerp(WavelengthMinMax.x, WavelengthMinMax.y, Mathf.Pow(alpha, WavelengthFalloff));
                float height = Mathf.Max(Mathf.Lerp(heightMinMax.x, heightMinMax.y, Mathf.Pow(alpha, heightFalloff)), 0.0001f);
                maxHeight += height;
                float offset = Mathf.Lerp(OffsetMinMax.x, OffsetMinMax.y, Mathf.Pow((float)i / numWaves, OffsetFalloff));

                waveData[i] = new Vector4(wavelength, height, offset, 0);
            }

            oceanBounds.size = new Vector3(oceanBounds.size.x, maxHeight, oceanBounds.size.z);
            Vector3 oceanBoundsWithDepthCenter = oceanBounds.center;
            oceanBoundsWithDepthCenter.y -= waterDepth / 2f;
            Vector3 oceanBoundsWithDepthSize = oceanBounds.size;
            oceanBoundsWithDepthSize.y += waterDepth;
            oceanBoundsWithDepth = new Bounds(oceanBoundsWithDepthCenter, oceanBoundsWithDepthSize);


            Vector3 flowDirectionWorldPosition = transform.TransformPoint(flowDirectionPosition);
            Vector4 FlowPivot = new Vector4(flowDirectionWorldPosition.x, flowDirectionWorldPosition.z, waveDirectionMode == WaveDirectionMode.RadialDirection ? 1 : 0, maxHeight);

            materialPropertyBlock = new MaterialPropertyBlock();

            materialPropertyBlock.SetInt("_WaveCount", numWaves);
            materialPropertyBlock.SetVectorArray("_WaveData", waveData);
            materialPropertyBlock.SetVector("_FlowPivot", FlowPivot);

            materialPropertyBlock.SetFloat("_ShoreDistanceWPODampening", shoreWPODampeningDistance);
            materialPropertyBlock.SetFloat("_WaterHeight", transform.position.y);

            materialPropertyBlock.SetFloat("_EdgeLength", tessellationAmount);

            underwaterMaterialInstance.SetInt("_WaveCount", numWaves);
            underwaterMaterialInstance.SetVectorArray("_WaveData", waveData);
            underwaterMaterialInstance.SetVector("_FlowPivot", FlowPivot);

            underwaterMaterialInstance.SetFloat("_ShoreDistanceWPODampening", shoreWPODampeningDistance);
            underwaterMaterialInstance.SetFloat("_WaterHeight", transform.position.y);

            SetWindDirection();

            Invoke("InitReflectionProbe", 2f);

            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                TerrainData terrainData = terrain.terrainData;
                if (terrainData != null)
                {
                    RenderTexture heightmap = terrainData.heightmapTexture;
                    if (heightmap != null)
                    {
                        Vector3 terrainPosition = terrain.transform.position;
                        Vector3 terrainSize = terrainData.size;
                        Vector4 terrainPosAndSize = new Vector4(terrainPosition.x, terrainPosition.z, terrainSize.x, terrainSize.z);
                        float terrainHeight = terrainData.heightmapScale.y * 2f;

                        materialPropertyBlock.SetTexture("_TerrainHeightMap", heightmap);
                        materialPropertyBlock.SetVector("_TerrainPosAndSize", terrainPosAndSize);
                        materialPropertyBlock.SetVector("_TerrainHeight", new Vector4(terrainHeight, terrainPosition.y, 0, 0));

                        underwaterMaterialInstance.SetTexture("_TerrainHeightMap", heightmap);
                        underwaterMaterialInstance.SetVector("_TerrainPosAndSize", terrainPosAndSize);
                        underwaterMaterialInstance.SetVector("_TerrainHeight", new Vector4(terrainHeight, terrainPosition.y, 0, 0));
#if BuoyancyShader
                        buoyancyComputeShader.SetTexture(buoyancyKernel, "_TerrainHeightMap", heightmap);
                        buoyancyComputeShader.SetVector("_TerrainPositionAndSize", terrainPosAndSize);
                        buoyancyComputeShader.SetFloat("_TerrainHeight", terrainHeight);

                        buoyancyComputeShader.SetInt("_WaveCount", numWaves);
                        buoyancyComputeShader.SetVectorArray("_WaveData", waveData);
                        buoyancyComputeShader.SetVector("_FlowPivot", FlowPivot);

                        buoyancyComputeShader.SetFloat("_ShoreDistanceWPODampening", shoreWPODampeningDistance);
#endif
                    }
                }

            }

            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterial = oceanMaterial;
                meshRenderer.SetPropertyBlock(materialPropertyBlock);
            }
        }

        private void InitReflectionProbe()
        {
            ReflectionProbe probe = FindObjectOfType<ReflectionProbe>();
            if (probe != null)
            {

                switch (probe.mode)
                {
                    case ReflectionProbeMode.Baked:
                        Texture cubemapBaked = probe.bakedTexture;
                        if (cubemapBaked != null)
                            materialPropertyBlock.SetTexture("_ReflectionProbeTexture", cubemapBaked);
                        else
                            Debug.LogWarning("Baked Reflection Probe Texture NOT Valid!");
                        break;
                    case ReflectionProbeMode.Realtime:
                        RenderTexture cubemapRealTime = probe.realtimeTexture;
                        if (cubemapRealTime != null)
                            materialPropertyBlock.SetTexture("_ReflectionProbeTexture", cubemapRealTime);
                        else
                            Debug.LogWarning("Real Time Reflection Probe Texture NOT Valid!");
                        break;
                    case ReflectionProbeMode.Custom:
                        Texture cubemapCustomBaked = probe.customBakedTexture;
                        if (cubemapCustomBaked != null)
                            materialPropertyBlock.SetTexture("_ReflectionProbeTexture", cubemapCustomBaked);
                        else
                            Debug.LogWarning("Custom Reflection Probe Texture NOT Valid!");
                        break;
                }
            }
        }

        private void SetWindDirection()
        {
            if (materialPropertyBlock == null)
                return;

            Vector3 flowDirectionWorldPosition = transform.TransformPoint(flowDirectionPosition);
            Vector3 windDirectionVector = flowDirectionWorldPosition - transform.position;
            windDirectionVector.y = 0;
            windDirectionVector = windDirectionVector.normalized;

            Vector4 windDirection = new Vector4(-windDirectionVector.x, -windDirectionVector.z, transform.position.x, transform.position.z);

            materialPropertyBlock.SetVector("_WindDirection", windDirection);

            underwaterMaterialInstance.SetVector("_WindDirection", windDirection);

#if BuoyancyEnabled
            buoyancyComputeShader.SetVector("_WindDirection", windDirection);
#endif
        }

        public void SetHandlePos(Vector3 newPos)
        {
            flowDirectionPosition = newPos;

            if (materialPropertyBlock == null)
                return;

            Vector3 flowDirectionWorldPosition = transform.TransformPoint(flowDirectionPosition);
            Vector4 flowPivot = new Vector4(flowDirectionWorldPosition.x, flowDirectionWorldPosition.z, waveDirectionMode == WaveDirectionMode.RadialDirection ? 1 : 0, transform.position.y);

            materialPropertyBlock.SetVector("_FlowPivot", flowPivot);

            underwaterMaterialInstance.SetVector("_FlowPivot", flowPivot);

            SetWindDirection();

            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterial = oceanMaterial;
                meshRenderer.SetPropertyBlock(materialPropertyBlock);
            }
        }

        // This function is called after the camera has finished rendering
        // and before the image is displayed on the screen.
        public void ExecuteunderwaterPostProcess(RenderTexture source, RenderTexture destination)
        {
            // If the underwaterMaterialInstance (shader) is not assigned, just copy the source to the destination
            if (underwaterMaterialInstance == null || !isCurrentlyUnderwater)
            {
                Graphics.Blit(source, destination);
                return;
            }

            // 'Graphics.Blit' performs the screen-space blit (copy/draw operation)
            // It takes the 'source' (the fully rendered scene image), sets it as 
            // the main texture in your 'underwaterMaterialInstance', runs your shader, and 
            // outputs the result to the 'destination'.
            Graphics.Blit(source, destination, underwaterMaterialInstance);
        }

        private void InitCollision()
        {
            if (boxCollider == null)
            {
                boxCollider = gameObject.AddComponent<BoxCollider>();
            }
            Vector3 scale = transform.lossyScale;
            Vector3 boundsCenter = transform.InverseTransformPoint(oceanBoundsWithDepth.center);
            Vector3 boundsSize = transform.InverseTransformVector(oceanBoundsWithDepth.size);
            boxCollider.center = boundsCenter;//new Vector3(0f,- (waterDepth * 0.5f /scale.y), 0f);
            boxCollider.size = boundsSize;//new Vector3(oceanBounds.size.x/scale.x, (maxHeight + waterDepth) / scale.y, oceanBounds.size.z/scale.z);
        }

        private void GeneratePlane()
        {
            int vertCountX = Mathf.RoundToInt(oceanBounds.size.x / preTessQuadSize);
            int vertCountZ = Mathf.RoundToInt(oceanBounds.size.z / preTessQuadSize);

            if (vertCountX < 2 || vertCountZ < 2)
            {
                Debug.LogWarning("Plane too small for given quad size.");
                return;
            }


            int totalVertices = vertCountX * vertCountZ;

            Vector3[] vertices = new Vector3[totalVertices];
            Vector2[] uv = new Vector2[totalVertices];
            Vector3[] normals = new Vector3[totalVertices];
            Vector4[] tangents = new Vector4[totalVertices];
            int[] triangles = new int[(vertCountX - 1) * (vertCountZ - 1) * 6];

            float scaleX = transform.localScale.x;
            float scaleZ = transform.localScale.z;                

            Vector3 minBoundPos = new Vector3(-oceanBounds.extents.x / scaleX, 0f, -oceanBounds.extents.z / scaleZ);
            Vector3 maxBoundPos = new Vector3(oceanBounds.extents.x / scaleX, 0f, oceanBounds.extents.z / scaleZ);

            // Generate vertices
            for (int z = 0; z < vertCountZ; z++)
            {
                for (int x = 0; x < vertCountX; x++)
                {
                    int i = x + z * vertCountX;

                    float alphaX = (float)x / (vertCountX - 1);
                    float alphaZ = (float)z / (vertCountZ - 1);

                    float posX = Mathf.Lerp(minBoundPos.x, maxBoundPos.x, alphaX);
                    float posZ = Mathf.Lerp(minBoundPos.z, maxBoundPos.z, alphaZ);

                    vertices[i] = new Vector3(posX, 0, posZ);
                    uv[i] = new Vector2(alphaX, alphaZ);
                    normals[i] = Vector3.up;
                    tangents[i] = new Vector4(1, 0, 0, 1);
                }
            }

            // Generate triangles
            int triIndex = 0;
            for (int z = 0; z < vertCountZ - 1; z++)
            {
                for (int x = 0; x < vertCountX - 1; x++)
                {
                    int i = x + z * vertCountX;

                    triangles[triIndex++] = i;
                    triangles[triIndex++] = i + vertCountX;
                    triangles[triIndex++] = i + 1;

                    triangles[triIndex++] = i + 1;
                    triangles[triIndex++] = i + vertCountX;
                    triangles[triIndex++] = i + vertCountX + 1;
                }
            }

            Mesh mesh = new Mesh();

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.triangles = triangles;

            GetComponent<MeshFilter>().mesh = mesh;
        }

#if BuoyancyEnabled
        private void InitBuoyancy()
        {
            floatingObjects = new Dictionary<float, Transform>();
            buoyancyInput = new Vector4[maxFloatingObjectsNum];
            buoyancyOutput = new Vector4[maxFloatingObjectsNum];

            buoyancyKernel = buoyancyComputeShader.FindKernel("BuoyancyCalculation");

            if (objectPositionsBuffer != null )
                objectPositionsBuffer.Release();
            if (buoyancyResultsBuffer != null )
                buoyancyResultsBuffer.Release();

            objectPositionsBuffer = new ComputeBuffer(maxFloatingObjectsNum, sizeof(float) * 4);
            buoyancyResultsBuffer = new ComputeBuffer(maxFloatingObjectsNum, sizeof(float) * 4);

            buoyancyComputeShader.SetBuffer(buoyancyKernel, "results", buoyancyResultsBuffer);
        }
#endif

#if UNITY_EDITOR
        private void OnValidate()
        {
#if BuoyancyEnabled
            InitBuoyancy();
#endif
            InitMaterial();
            InitCollision();
        }
#endif
        private void Start()
        {
            Initialize();
        }

        private void Update()
        {
            Vector3 currPos = transform.position;
            Vector3 currScale = transform.localScale;

            if (prevScale != currScale || prevPos != currPos)
            {
                // Update bounds
                oceanBounds = new Bounds(currPos, new Vector3(startingSize * currScale.x, maxHeight, startingSize * currScale.z));

                prevPos = currPos;
                prevScale = currScale;

                Initialize();
            }
#if UNITY_EDITOR
            GetActiveCamera();
#endif

            isCurrentlyUnderwater = boxCollider.bounds.Contains(cam.transform.position);

#if BuoyancyEnabled
            buoyancyInput = new Vector4[maxFloatingObjectsNum];
            int counter = 0;
            foreach (var floatingObject in floatingObjects)
            {
                Vector3 pos = floatingObject.Value.position;
                float id = floatingObject.Key;

                buoyancyInput[counter] = new Vector4(pos.x, pos.y, pos.z, id);

                counter++;
            }

            objectPositionsBuffer.SetData( buoyancyInput );
            buoyancyComputeShader.SetBuffer(buoyancyKernel, "objectInputData", objectPositionsBuffer);
            buoyancyComputeShader.SetFloat("_Time", Time.time);

            buoyancyComputeShader.Dispatch(buoyancyKernel, maxFloatingObjectsNum / 64, 1, 1);

            // Request async readback (schedules it — not immediate!)
            AsyncGPUReadback.Request(buoyancyResultsBuffer, HandleBuoyancy);
#endif
        }

#if BuoyancyEnabled
        private void HandleBuoyancy(AsyncGPUReadbackRequest req)
        {
            if (!req.hasError)
            {
                var result = req.GetData<Vector4>().ToArray();
                buoyancyOutput = result;
            }
            else
            {
                Debug.Log("Buoyancy GPU Callback NOT Valid!");
            }
        }
#endif

#if UNITY_EDITOR
        public void GetActiveCamera()
        {
            Camera activeCamera = null;

            // Check if we're in the Unity Editor (not in Play mode)
            if (!Application.isPlaying)
            {
                // Get the SceneView camera (Editor-only)
                activeCamera = SceneView.lastActiveSceneView.camera;
                if (activeCamera != null)
                {
                    cam = activeCamera;
                }
                else
                {
                    Debug.Log("Scene view Invalid Camera");
                }
            }
            if (cam == null)
            {
                // Fallback to MainCamera (works in Play mode and builds)
                activeCamera = Camera.main; // Uses the "MainCamera" tagged camera
                if (activeCamera == null)
                {
                    // If no MainCamera, find any active camera
                    activeCamera = Camera.current; // Current rendering camera (less reliable)
                    if (activeCamera == null)
                        activeCamera = UnityEngine.Object.FindAnyObjectByType<Camera>(); // Last resort
                }

                cam = activeCamera;
            }
        }
#endif

#if BuoyancyEnabled
        private float GetObjectID(GameObject go)
        {
            int id = go.GetInstanceID();
            return (id & 0x7FFFFFFF) / 1000000f;
        }
#endif
        void OnTriggerEnter(Collider other)
        {
#if BuoyancyEnabled
            if (other.GetComponent<Rigidbody>())
            {
                float idFloat = GetObjectID(other.gameObject);

                if (!floatingObjects.ContainsKey(idFloat))
                {
                    floatingObjects.Add(idFloat, other.transform);
                    Debug.Log($"Object {other.name} entered ocean with id {idFloat}");
                }
            }
#endif
        }

        void OnTriggerExit(Collider other)
        {
#if BuoyancyEnabled
            if (other.GetComponent<Rigidbody>())
            {
                float idFloat = GetObjectID(other.gameObject);

                floatingObjects.Remove(idFloat);
            }
#endif
        }

        void OnDestroy()
        {
#if BuoyancyEnabled
            objectPositionsBuffer.Release();
            buoyancyResultsBuffer.Release();
#endif
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(oceanBoundsWithDepth.center, oceanBoundsWithDepth.size);
        }

        private void OnDrawGizmos()
        {
#if BuoyancyEnabled
            if (buoyancyOutput != null)
            {
                for (int i = 0; i < buoyancyOutput.Length; i++)
                {
                    Vector4 data = buoyancyOutput[i];

                    float id = data.w;
                    if (floatingObjects.TryGetValue(id, out var floatingObject))
                    {
                        Vector3 pos = floatingObject.position;
                        pos.y = data.z;

                        Gizmos.DrawWireSphere(pos, 2f);
                        Debug.Log(floatingObject.name);
                    }
                }
            }
            else
            {
                Debug.Log("Buoyancy Output is NOT Valid!");
            }
#endif
        }
    }

#if UNITY_EDITOR

    [CustomEditor(typeof(OceanTool))]
    public class OceanToolEditor : Editor
    {
        bool isHovering = false;
        bool showMeshSettings = false;
        bool showWaveSettings = false;

        void OnSceneGUI()
        {
            OceanTool ocean = (OceanTool)target;

            Vector3 basePos = ocean.transform.position;
            Vector3 handlePos = ocean.transform.TransformPoint( ocean.flowDirectionPosition );
            handlePos.y = basePos.y + 10f;

            Vector2 guiPoint = HandleUtility.WorldToGUIPoint(handlePos);
            float size = 64f;
            Rect iconRect = new Rect(guiPoint.x - size / 2, guiPoint.y - size / 2, size, size);

            Event guiEvent = Event.current;
            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            EventType type = guiEvent.GetTypeForControl(controlID);

            // Hover detection
            isHovering = iconRect.Contains(guiEvent.mousePosition);

            switch (type)
            {
                case EventType.MouseDown:
                    if (isHovering && guiEvent.button == 0)
                    {
                        GUIUtility.hotControl = controlID;
                        guiEvent.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlID)
                    {
                        Ray ray = HandleUtility.GUIPointToWorldRay(guiEvent.mousePosition);
                        Plane plane = new Plane(Vector3.up, basePos + Vector3.up * 10f);
                        if (plane.Raycast(ray, out float dist))
                        {
                            Vector3 worldPos = ray.GetPoint(dist);
                            Undo.RecordObject(ocean, "Drag Ocean Handle");
                            ocean.SetHandlePos(ocean.transform.InverseTransformPoint(worldPos));
                        }
                        guiEvent.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlID)
                    {
                        GUIUtility.hotControl = 0;
                        guiEvent.Use();
                    }
                    break;
            }

            // Draw icon with hover tint
            if (ocean.flowDirectionPositionIcon != null)
            {
                Handles.BeginGUI();

                if (isHovering)
                    GUI.color = new Color(1, 1, 1, 1f); // slight highlight
                else
                    GUI.color = new Color(1, 1, 1, 0.8f); // dim when not hovered

                GUI.DrawTexture(iconRect, ocean.flowDirectionPositionIcon, ScaleMode.ScaleToFit, true);

                GUI.color = Color.white; // reset
                Handles.EndGUI();
            }

            // Draw helper line
            Handles.color = isHovering ? Color.yellow : new Color(0f, 1f, 1f, 0.25f);
            Handles.DrawLine(basePos, handlePos);
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;

            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private bool CenteredFoldout(bool isExpanded, string label, GUIStyle labelStyle, float height = 24f)
        {
            Rect foldoutRect = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
            Rect arrowRect = new Rect(foldoutRect.x, foldoutRect.y, 20, foldoutRect.height);

            // Draw arrow (no label)
            isExpanded = EditorGUI.Foldout(arrowRect, isExpanded, GUIContent.none, true);

            // Handle full-width click
            Event e = Event.current;
            if (e.type == EventType.MouseDown && foldoutRect.Contains(e.mousePosition))
            {
                if (!arrowRect.Contains(e.mousePosition))
                {
                    isExpanded = !isExpanded;
                    e.Use();
                }
            }

            // Draw centered label
            GUI.Label(foldoutRect, label, labelStyle);

            return isExpanded;
        }

        public override void OnInspectorGUI()
        {
            OceanTool ocean = (OceanTool)target;

            GUILayout.Space(15);

            GUIStyle largeLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 36,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            GUILayout.Label("Ocean Tool", largeLabelStyle);

            EditorGUILayout.Space(20, true);

            GUIStyle medLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Normal,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            GUIStyle smallLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                wordWrap = true, // Enable text wrapping
                normal = { textColor = new UnityEngine.Color(0.85f, 0.85f, 0.85f) },
                alignment = TextAnchor.UpperLeft // Better alignment for wrapped text
            };

            GUIStyle smallLabelStyleCenter = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                wordWrap = true, // Enable text wrapping
                normal = { textColor = new UnityEngine.Color(1f, 1f, 1f) },
                alignment = TextAnchor.MiddleCenter
            };

            GUIStyle smallBoldLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                wordWrap = true, // Enable text wrapping
                normal = { textColor = new UnityEngine.Color(0.95f, 0.95f, 0.95f) },
                alignment = TextAnchor.UpperLeft // Better alignment for wrapped text
            };

            // Custom styles for enabled and disabled buttons
            GUIStyle enabledStyle = new GUIStyle(GUI.skin.button);
            enabledStyle.normal.textColor = Color.white;
            enabledStyle.fontSize = 12;
            enabledStyle.normal.background = MakeTex(2, 2, new Color(0f, 0.4f, 0f));

            GUIStyle disabledStyle = new GUIStyle(GUI.skin.button);
            disabledStyle.normal.textColor = Color.white;
            disabledStyle.normal.background = MakeTex(2, 2, new Color(0.4f, 0f, 0f));

            // Create a box or area for the text to constrain width
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(
                "This is a tool that helps you create any type of Ocean. It provides controls to help you Art Direct and Optimize your Ocean based on your needs.",
                smallLabelStyle
            );
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            showMeshSettings = CenteredFoldout(showMeshSettings, "Mesh Settings", medLabelStyle);

            GUILayout.Space(5);
            if (showMeshSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField(
                    "The Ocean is a single Subdivided Plane.\nYou can control the initial resolution by changing the \"preTessQuadSize\" property.\nThe plane then will be tessellated by the \"TesselationAmount\" property. Higher values means less tessellation -> more performance.\nTry to find the best values for your ocean scale and the target visual fidelity. ",
                    smallLabelStyle
                );
                EditorGUI.indentLevel--;

                GUILayout.Space(10);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("preTessQuadSize"), new GUIContent("Pre Tess Quad Size"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("tessellationAmount"), new GUIContent("Tessellation Amount"));
                
                GUILayout.Space(15);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            showWaveSettings = CenteredFoldout(showWaveSettings, "Waves Settings", medLabelStyle);

            GUILayout.Space(5);
            if (showWaveSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField(
                    "The selected number of waves are blended to create the final result. Values are assigned to each wave independently based on the Min and Max values. Wave number 0 has the Min value and the last wave has the Max value.",
                    smallLabelStyle
                );
                EditorGUI.indentLevel--;

                GUILayout.Space(10);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("numWaves"), new GUIContent("Num Waves"));
                EditorGUILayout.Space(5);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.Space(5);
                GUILayout.Label("Wave Length", smallLabelStyleCenter);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("WavelengthMinMax"), new GUIContent("Min Max"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("WavelengthFalloff"), new GUIContent("Falloff"));
                EditorGUILayout.Space(5);
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(5);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.Space(5);
                GUILayout.Label("Wave Height", smallLabelStyleCenter);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("heightMinMax"), new GUIContent("Min Max"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("heightFalloff"), new GUIContent("Falloff"));
                EditorGUILayout.Space(5);
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(5);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.Space(5);
                GUILayout.Label("Wave Offset", smallLabelStyleCenter);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OffsetMinMax"), new GUIContent("Min Max"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OffsetFalloff"), new GUIContent("Falloff"));
                EditorGUILayout.Space(5);
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("shoreWPODampeningDistance"), new GUIContent("Shore WPO Dampening Distance"));
                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("oceanMaterial"), new GUIContent("Ocean Material"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("underwaterMaterial"), new GUIContent("Underwater Material"));
                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("waveDirectionMode"), new GUIContent("Wave Direction Mode"));
                EditorGUILayout.Space(5);

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(15);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}