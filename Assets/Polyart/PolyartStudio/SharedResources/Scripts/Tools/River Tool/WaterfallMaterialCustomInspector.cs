#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;


namespace Polyart
{
    public class WaterfallMaterialCustomInspector : ShaderGUI
    {
        private bool showWaterColor = false, debugWaterColor = false;
        private bool showOpacity = false;
        private bool showCaustics = false;
        private bool showNormalsAndRefraction = false, debugNormalsAndRefraction = false;
        private bool showEdgeFoam = false;


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

            GUILayout.Label("Waterfall Material", largeLabelStyle);

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
                "The Waterfall Material consists of many different parts. You can debug certain parts for a better understanding on what each parameter does.",
                smallLabelStyle
            );
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            showWaterColor = EditorGUILayout.Foldout(showWaterColor, "Water Color", true, EditorStyles.foldoutHeader);
            if (showWaterColor)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "The Final Waterfall Color consist of 3 different colors. A darker one and a lighter one that are blended together by the Base Color Variation Texture, and a third one that is blended on top by the Highlight Variation Texture.",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Color1", properties), "Lighter Color");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Color2", properties), "Darker Color");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_ColorVariationTexture", properties), "Color Variation Texture");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_ColorVariationSpeed", properties), "Color Variation Speed");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_ColorVariationDepth", properties), new GUIContent("Color Variation Depth", "Slight Parallax effect to give fake depth"));
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Color3", properties), "Highlight Color");
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
                    "The Opacity controls the fading on the start of the waterfall, so that it blends with the rivers",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_InitialOpacityGradience", properties), new GUIContent("Waterfall Start Opacity", "Helps with blending with the rivers. The higher the value the sharper the transition."));
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_WaterfallEdgeSpeed", properties), "Edge Mask Speed");
                GUILayout.Space(10);
            }        

            showNormalsAndRefraction = EditorGUILayout.Foldout(showNormalsAndRefraction, "Lighting", true, EditorStyles.foldoutHeader);
            if (showNormalsAndRefraction)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "The Normals are calculated by the Color Variation Texture.",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_NormalStrength", properties), "Normals Strength");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Smoothness", properties), "Smoothness");
                GUILayout.Space(5);
                debugNormalsAndRefraction = EditorGUILayout.Toggle("Debug Water Normals?", debugNormalsAndRefraction);
                GUILayout.Space(10);
            }

            showCaustics = EditorGUILayout.Foldout(showCaustics, "Bottom Noise", true, EditorStyles.foldoutHeader);
            if (showCaustics)
            {
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_WaterfallStartNoise", properties), "Noise Texture");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_BottomFoamSpeed", properties), "Noise Speed");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_BottomFoamExtendMin", properties), "Noise Start Position");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_BottomFoamExtendMax", properties), "Noise End Position");
                GUILayout.Space(10);
            }

            showEdgeFoam = EditorGUILayout.Foldout(showEdgeFoam, "Edge Foam", true, EditorStyles.foldoutHeader);
            if (showEdgeFoam)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "The Edge Foam is produced by the scene depth, meaning what is behind the surface of the Waterfall.",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_EdgeFoamDistance", properties), "Foam Distance");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_EdgeFoamStep", properties), "Foam Step");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_EdgeFoamOpacity", properties), "Foam Opacity");
                GUILayout.Space(10);
            }

            debugShader(materialEditor, properties);            
        }
    }
}

#endif