#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;


namespace Polyart
{
    public class RiverMaterialCustomInspector : ShaderGUI
    {
        private bool showWaterColor = false, debugWaterColor = false;
        private bool showOpacity = false;
        private bool showCaustics = false;
        private bool showNormalsAndRefraction = false, debugNormalsAndRefraction = false;
        private bool showFlowLines = false, debugFlowLines = false;
        private bool showEdgeFoam = false, debugEdgeFoam = false;
        private bool showPaintedFoam = false;
        private bool showSteepnessFoam = false;
        private bool showVoronoiNoise = false, debugVoronoiNoise = false;
        private bool showCloudNoise = false, debugCloudNoise;

        private void debugShader(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material material = (Material)materialEditor.target;

            material.DisableKeyword("_DEBUG_ON");

            if (debugWaterColor)
            {
                material.EnableKeyword("_DEBUG_ON");

                material.SetFloat("_DebugNormals", 0f);
                material.SetFloat("_DebugFlowLines", 0f);
                material.SetFloat("_DebugEdgeFoam", 0f);
                material.SetFloat("_DebugVoronoiFoam", 0f);
                material.SetFloat("_DebugCloudNoise", 0f);
                material.SetFloat("_DebugWaterColor", 1f);
            }
            else if (debugNormalsAndRefraction)
            {
                material.EnableKeyword("_DEBUG_ON");

                material.SetFloat("_DebugWaterColor", 0f);
                material.SetFloat("_DebugFlowLines", 0f);
                material.SetFloat("_DebugEdgeFoam", 0f);
                material.SetFloat("_DebugVoronoiFoam", 0f);
                material.SetFloat("_DebugCloudNoise", 0f);
                material.SetFloat("_DebugNormals", 1f);
            }
            else if (debugFlowLines)
            {
                material.EnableKeyword("_DEBUG_ON");

                material.SetFloat("_DebugWaterColor", 0f);
                material.SetFloat("_DebugNormals", 0f);
                material.SetFloat("_DebugEdgeFoam", 0f);
                material.SetFloat("_DebugVoronoiFoam", 0f);
                material.SetFloat("_DebugCloudNoise", 0f);
                material.SetFloat("_DebugFlowLines", 1f);
            }
            else if (debugEdgeFoam)
            {
                material.EnableKeyword("_DEBUG_ON");

                material.SetFloat("_DebugWaterColor", 0f);
                material.SetFloat("_DebugNormals", 0f);
                material.SetFloat("_DebugFlowLines", 0f);
                material.SetFloat("_DebugVoronoiFoam", 0f);
                material.SetFloat("_DebugCloudNoise", 0f);
                material.SetFloat("_DebugEdgeFoam", 1f);
            }
            else if (debugVoronoiNoise)
            {
                material.EnableKeyword("_DEBUG_ON");

                material.SetFloat("_DebugWaterColor", 0f);
                material.SetFloat("_DebugNormals", 0f);
                material.SetFloat("_DebugFlowLines", 0f);
                material.SetFloat("_DebugEdgeFoam", 0f);
                material.SetFloat("_DebugCloudNoise", 0f);
                material.SetFloat("_DebugVoronoiFoam", 1f);
            }
            else if (debugCloudNoise)
            {
                material.EnableKeyword("_DEBUG_ON");

                material.SetFloat("_DebugWaterColor", 0f);
                material.SetFloat("_DebugNormals", 0f);
                material.SetFloat("_DebugFlowLines", 0f);
                material.SetFloat("_DebugEdgeFoam", 0f);
                material.SetFloat("_DebugVoronoiFoam", 0f);
                material.SetFloat("_DebugCloudNoise", 1f);
            }
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            GUILayout.Space(15);

            GUIStyle largeLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 36,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            GUILayout.Label("River Material", largeLabelStyle);

            EditorGUILayout.Space(20, true);

            GUIStyle medLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Normal,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            //EditorGUILayout.LabelField("River Parameters", medLabelStyle);

            GUIStyle smallLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                wordWrap = true, // Enable text wrapping
                normal = { textColor = new UnityEngine.Color(0.85f, 0.85f, 0.85f) },
                alignment = TextAnchor.UpperLeft // Better alignment for wrapped text
            };

            GUIStyle smallBoldLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                wordWrap = true, // Enable text wrapping
                normal = { textColor = new UnityEngine.Color(0.95f, 0.95f, 0.95f) },
                alignment = TextAnchor.UpperLeft // Better alignment for wrapped text
            };

            // Create a box or area for the text to constrain width
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(
                "The River Material consists of many different parts. You can debug certain parts for a better understanding on what each parameter does.",
                smallLabelStyle
            );
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            showWaterColor = EditorGUILayout.Foldout(showWaterColor, "Water Color", true, EditorStyles.foldoutHeader);
            if (showWaterColor)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "The Final River Color consist of 3 different colors. A darker one and a lighter one that are blended together by the Base Color Variation Texture, and a third one that is blended on top by the Highlight Variation Texture.",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Color3", properties), "Lighter Color");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Color4", properties), "Darker Color");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_ColorVariationTexture", properties), "Base Color Variation Texture");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_ColorVariationSpeed", properties), "Base Color Variation Speed");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_ColorVariationDepth", properties), new GUIContent("Base Color variation Depth", "Slight Parallax effect to give fake depth"));
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Color5", properties), "Highlight Color");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_NoiseLines", properties), "Highlight Variation Texture");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_NoiseLinesSpeed", properties), "Highlight Variation Speed");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_NoiseLinesDistortion", properties), "Highlight Variation Distortion");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_NoiseLinesPow", properties), "Highlight Variation Pow");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_NoiseLinesOpacity", properties), "Highlight Variation Opacity");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_NoiseLinesReveal", properties), new GUIContent("Highlight Variation Reveal", "Applies the highlight Color only on top of the Lighter Color."));
                GUILayout.Space(5);
                debugWaterColor = EditorGUILayout.Toggle("Debug Water Color?", debugWaterColor);
                GUILayout.Space(10);
            }

            showOpacity = EditorGUILayout.Foldout(showOpacity, "Opacity", true, EditorStyles.foldoutHeader);
            if (showOpacity)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "The water is not fully transparent. It just samples the Scene Color to apply refraction. It is only transparent on the edges to help with terrain blending.",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_EdgeOpacityDistance", properties), new GUIContent("Edge Opacity", "Helps with lighting on the terrain intersections."));
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_ColorMaxOpacity", properties), new GUIContent("Max Opacity", "The maximum Opacity the water gets against the refracted background."));
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_ColorTransparencyDepthNear", properties), new GUIContent("Depth Near", "Handles the transparency of the water."));
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_ColorTransparencyDepthFar", properties), new GUIContent("Depth Near", "Handles the transparency of the water."));
                GUILayout.Space(10);
            }

            showCaustics = EditorGUILayout.Foldout(showCaustics, "Caustics", true, EditorStyles.foldoutHeader);
            if (showCaustics)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "The Caustics have a fake depth effect handled by the Normal Distortion and Depth parameters.",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_CausticsTexture", properties), "Caustics Texture");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_CausticsSpeed", properties), "Caustics Speed");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_CausticsOpacity", properties), "Caustics Opacity");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_CausticsDepth", properties), "Caustics Edge Distance");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_CausticsViewOffset", properties), "Caustics Distortion");
                GUILayout.Space(10);
            }

            showNormalsAndRefraction = EditorGUILayout.Foldout(showNormalsAndRefraction, "Normals and Refraction", true, EditorStyles.foldoutHeader);
            if (showNormalsAndRefraction)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "The refraction is based on the Normals of the water, so the distortion is affected by their size and speed.",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_WaterNormals", properties), "Normal Map");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_NormalsSpeed", properties), "Normals Speed");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_NormalsDistortionIntensity", properties), "NormalsDistortion");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_NormalsStrength", properties), "Normals Strength");
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Refraction", properties), "Refraction");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Smoothness", properties), "Smoothness");
                GUILayout.Space(5);
                debugNormalsAndRefraction = EditorGUILayout.Toggle("Debug Water Normals?", debugNormalsAndRefraction);
                GUILayout.Space(10);
            }

            showFlowLines = EditorGUILayout.Foldout(showFlowLines, "Flow Lines", true, EditorStyles.foldoutHeader);
            if (showFlowLines)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "The flow lines are controlled separately for steep and flat water, but there are some shared parameters as well. Steep Water is defined by ",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Label(
                    "Shared",
                    smallBoldLabelStyle
                );
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesNumber", properties), "Lines Number");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesLengthGradience", properties), "Length Gradience");
                GUILayout.Space(5);
                GUILayout.Label(
                    "Flat",
                    smallBoldLabelStyle
                );
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesOpacity", properties), "Opacity");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesSpeed", properties), "Speed");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesLength", properties), "Length");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesWidthGradience", properties), "Width Gradience");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesPow", properties), "Contrast");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesDistortion", properties), "Distortion");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesMask", properties), new GUIContent("Random Line Disolve", "The higher the value the more lines are removed."));
                GUILayout.Space(5);
                GUILayout.Label(
                    "Steep",
                    smallBoldLabelStyle
                );
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesWaterfall", properties), "Opacity");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesSpeedWaterfall", properties), "Speed");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesLengthWaterfall", properties), "Length");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesWidthGradienceWaterfall", properties), "Width Gradience");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesPowWaterfall", properties), "Contrast");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesDistortionWaterfall", properties), "Distortion");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamLinesMaskWaterfall", properties), new GUIContent("Random Line Disolve", "The higher the value the more lines are removed."));
                GUILayout.Space(5);
                debugFlowLines = EditorGUILayout.Toggle("Debug Flow Lines?", debugFlowLines);
                GUILayout.Space(10);
            }

            showEdgeFoam = EditorGUILayout.Foldout(showEdgeFoam, "Edge Foam", true, EditorStyles.foldoutHeader);
            if (showEdgeFoam)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "The edge foam is calculated based on the UVs, so the river mesh should properly follow the terrain.",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_EdgeFoamIntensity", properties), "Edge Foam Opacity");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_EdgeFoamContrast", properties), "Edge Foam Contrast");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_EdgeFoamNoiseMultiplier", properties), "Edge Foam Noise Influence");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_EdgeFoamThickness", properties), "Edge Foam Thickness");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_EdgeFoamDepth", properties), "Edge Foam Depth");
                GUILayout.Space(5);
                debugEdgeFoam = EditorGUILayout.Toggle("Debug Edge Foam?", debugEdgeFoam);
                GUILayout.Space(10);
            }

            showPaintedFoam = EditorGUILayout.Foldout(showPaintedFoam, "Painted Foam", true, EditorStyles.foldoutHeader);
            if (showPaintedFoam)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "You can paint this Foam using the river tool. What it does is painting the Alpha Channel of the Vertex Colors to reveal the Foam.",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamTexture", properties), "Foam Texture");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamSpeed", properties), "Foam Speed");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamNormalDistortion", properties), "Foam Distortion");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FoamHardness", properties), "Foam Hardness");
                GUILayout.Space(10);
            }

            showSteepnessFoam = EditorGUILayout.Foldout(showSteepnessFoam, "Steepness Foam", true, EditorStyles.foldoutHeader);
            if (showSteepnessFoam)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "This foam is the transition between Steep and Flat Water. You can define the angle of the transition.",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_SteepWaterStartDeg", properties), new GUIContent("Transition Angle", "0 - 90 degrees"));
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_SlopeLinesOpacity", properties), "Stepness Foam Opacity");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_SlopeLinesStep", properties), "Stepness Foam Step");
                GUILayout.Space(10);
            }

            showVoronoiNoise = EditorGUILayout.Foldout(showVoronoiNoise, "Voronoi Foam", true, EditorStyles.foldoutHeader);
            if (showVoronoiNoise)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "The Voronoi Foam is present at Steep Slopes and it also produces some small Foam Dots flowing with the river.",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_SteepVoronoiTexture", properties), "Voronoi Texture");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_SteepVoronoiSpeed", properties), "Voronoi Speed");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_SteepVoronoiDistortion", properties), "Voronoi Distortion");
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_SteepVoronoiStep", properties), "Voronoi Step");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_SteepVoronoiOpacity", properties), "Voronoi Opacity");
                GUILayout.Space(5);
                GUILayout.Label(
                    "Flat Foam Dots",
                    smallBoldLabelStyle
                );
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_SteepWaterSmallDotsStep", properties), "Foam Dots Step");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_SteepWaterSmallDotsOpacity", properties), "Foam Dots Opacity");
                GUILayout.Space(5);
                GUILayout.Label(
                    "Steep Foam Dots",
                    smallBoldLabelStyle
                );
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FlatWaterSmallDotsStep", properties), "Foam Dots Step");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_FlatWaterSmallDotsOpacity", properties), "Foam Dots Opacity");
                GUILayout.Space(5);
                debugVoronoiNoise = EditorGUILayout.Toggle("Debug Voronoi Foam?", debugVoronoiNoise);
                GUILayout.Space(10);
            }

            showCloudNoise = EditorGUILayout.Foldout(showCloudNoise, "Cloud Noise", true, EditorStyles.foldoutHeader);
            if (showCloudNoise)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "This noise is used by many different parts of the River Shader mainly for adding Distortion. It is also used to mask out parts of the Edge Foam.",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_CloudNoise", properties), "Cloud Noise Texture");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_CloudNoiseSpeed", properties), "Cloud Noise Speed");
                GUILayout.Space(5);
                debugCloudNoise = EditorGUILayout.Toggle("Debug Cloud Noise?", debugCloudNoise);
                GUILayout.Space(10);
            }

            debugShader(materialEditor, properties);
        }
    }
}

#endif