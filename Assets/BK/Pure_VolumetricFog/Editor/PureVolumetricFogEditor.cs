using UnityEditor;
using UnityEngine;

namespace BKPureNature
{
    [CustomEditor(typeof(PureVolumetricFog))]
    public class PureVolumetricFogEditor : UnityEditor.Editor
    {
        private const string ShowAdvancedKey = "BKPureNature.PureVolumetricFog.ShowAdvanced";

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawTerrainAndVolume();

            DrawSection("Fog Shape",
                "density",
                "terrainFollow",
                "groundFogHeight",
                "nearFadeDistance");

            DrawLighting();

            DrawSection("Turbulence",
                "noiseAmount",
                "noiseCoverage",
                "largeNoiseScale",
                "detailNoiseScale",
                "windDirection",
                "windSpeed");

            DrawShadows();

            EditorGUILayout.Space();
            bool showAdvanced = SessionState.GetBool(ShowAdvancedKey, false);
            bool toggled = EditorGUILayout.Foldout(
                showAdvanced,
                "Advanced",
                true,
                EditorStyles.foldoutHeader);
            if (toggled != showAdvanced)
                SessionState.SetBool(ShowAdvancedKey, toggled);
            if (toggled)
                DrawAdvanced();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTerrainAndVolume()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Terrain & Volume", EditorStyles.boldLabel);
            DrawProperty("terrains");
            DrawProperty("findActiveTerrainsAutomatically", "Find Active Terrains");
            DrawProperty("horizontalPadding");
            DrawProperty("bottomPadding");
            DrawProperty("topPadding");
            DrawProperty("terrainEdgeFade");

            PureVolumetricFog fog = (PureVolumetricFog)target;
            if (fog.Terrains.Count == 0)
            {
                EditorGUILayout.HelpBox("No active Terrain found in the loaded scenes.", MessageType.Warning);
            }
            else if (serializedObject.FindProperty("terrains").arraySize == 0)
            {
                EditorGUILayout.LabelField("Auto-discovered Terrains", EditorStyles.miniBoldLabel);
                using (new EditorGUI.DisabledScope(true))
                {
                    for (int i = 0; i < fog.Terrains.Count; i++)
                        EditorGUILayout.ObjectField(fog.Terrains[i], typeof(Terrain), true);
                }
            }
            if (PureVolumetricFogRendererFeature.IsInstalled)
                EditorGUILayout.HelpBox("Rendering through the Pure Volumetric Fog renderer feature. Resolution is set on the feature.", MessageType.None);
            else
                EditorGUILayout.HelpBox("Rendering directly at full resolution. Add the Pure Volumetric Fog renderer feature to the URP Renderer to render at half or quarter resolution.", MessageType.None);
            if (GUILayout.Button("Find Terrains"))
            {
                Undo.RecordObject(fog, "Find Terrains");
                fog.FindActiveTerrains();
                EditorUtility.SetDirty(fog);
            }
        }

        private void DrawLighting()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Lighting & Scattering", EditorStyles.boldLabel);
            DrawProperty("followSceneFogColor", "Use Scene Fog Color");
            DrawProperty("followSceneAmbientColor", "Use Scene Ambient Color");
            DrawProperty("directionalLight");
            if (!serializedObject.FindProperty("followSceneFogColor").boolValue)
                DrawProperty("fogColor");
            if (!serializedObject.FindProperty("followSceneAmbientColor").boolValue)
                DrawProperty("ambientColor");
            DrawProperty("ambientStrength");
            DrawProperty("sunStrength");
            DrawProperty("sunScattering");
            DrawProperty("anisotropy");
        }

        private void DrawShadows()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shadows", EditorStyles.boldLabel);
            DrawProperty("enableVolumetricShadows");
            if (!serializedObject.FindProperty("enableVolumetricShadows").boolValue)
                return;

            DrawProperty("terrainCastsFogShadows", "Receive Scene Shadows");
            using (new EditorGUI.DisabledScope(!serializedObject.FindProperty("terrainCastsFogShadows").boolValue))
                DrawProperty("useUnityRealtimeShadows", "Include Mesh Shadows");
            DrawProperty("shadowStrength");
            DrawProperty("ambientShadowStrength");
            DrawProperty("fogSelfShadowStrength");
            DrawProperty("shadowDistance", "Terrain Shadow Distance");
            if (serializedObject.FindProperty("useUnityRealtimeShadows").boolValue)
                EditorGUILayout.HelpBox(
                    "Includes objects casting realtime shadows from the directional light. " +
                    "Mesh shadow range follows Unity's shadow distance. Point and spot lights are not included.",
                    MessageType.Info);
        }

        private void DrawAdvanced()
        {
            EditorGUI.indentLevel++;
            DrawSection("Fog Shape Details",
                "farFadeStart",
                "rebuildWhenTerrainChanges");

            DrawSection("Turbulence Details",
                "noiseSeed",
                "noiseTextureResolution",
                "noiseFloor",
                "noiseContrast",
                "noiseStrength",
                "noiseHeightDistortion",
                "verticalNoiseShear");

            DrawSection("Sampling Stabilization",
                "rayJitterStrength",
                "rayStepSmoothing",
                "noiseMipBias",
                "noiseFilterScale");
            DrawProperty("topSoftness", "Vertical Sampling Scale");

            DrawSection("Rendering Quality",
                "maximumRaySteps",
                "minimumRaySteps",
                "targetStepLength",
                "maximumFogDistance");

            DrawSection("Shadow Details",
                "terrainShadowBias");

            DrawSection("Rendering Details",
                "debugView",
                "useSceneDepth",
                "depthBias");

            EditorGUI.indentLevel--;
        }

        private void DrawSection(string title, params string[] propertyNames)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            foreach (string propertyName in propertyNames)
                DrawProperty(propertyName);
        }

        private void DrawProperty(string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, true);
        }

        private void DrawProperty(string propertyName, string displayName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, new GUIContent(displayName, property.tooltip), true);
        }

        [MenuItem("GameObject/BK/Pure Volumetric Fog", false, 10)]
        private static void CreateGroundFog(MenuCommand command)
        {
            GameObject gameObject = new GameObject("Pure Volumetric Fog");
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Pure Volumetric Fog");
            GameObjectUtility.SetParentAndAlign(gameObject, command.context as GameObject);
            gameObject.AddComponent<PureVolumetricFog>();
            Selection.activeGameObject = gameObject;
        }
    }
}
