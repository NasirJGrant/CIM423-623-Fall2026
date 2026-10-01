using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JdogLabs
{
    /// <summary>
    /// Professional Material Texture Assigner for Unity URP/Built-in pipelines.
    /// Automatically detects and assigns textures to materials with intelligent type detection.
    /// </summary>
    public class MaterialTextureAssigner : EditorWindow
    {
        #region Constants

        private static class ShaderProperties
        {
            // URP Lit Properties
            public const string BaseMap = "_BaseMap";
            public const string BumpMap = "_BumpMap";
            public const string MetallicGlossMap = "_MetallicGlossMap";
            public const string OcclusionMap = "_OcclusionMap";
            public const string EmissionMap = "_EmissionMap";
            public const string ParallaxMap = "_ParallaxMap";
            public const string DetailAlbedoMap = "_DetailAlbedoMap";
            public const string DetailNormalMap = "_DetailNormalMap";

            // Built-in Standard Properties (fallbacks)
            public const string MainTex = "_MainTex";
            public const string BumpMapStandard = "_BumpMap";
            public const string MetallicGlossMapStandard = "_MetallicGlossMap";
        }

        private static class Shaders
        {
            public const string URPLit = "Universal Render Pipeline/Lit";
            public const string BuiltInStandard = "Standard";
        }

        private static class EditorPrefsKeys
        {
            public const string DebugMode = "MaterialTextureAssigner_DebugMode";
            public const string DefaultSavePath = "MaterialTextureAssigner_SavePath";
            public const string AutoConfigureImport = "MaterialTextureAssigner_AutoConfigure";
        }

        #endregion

        #region Texture Detection Patterns

        private static readonly string[] AlbedoPatterns =
            { "albedo", "diffuse", "basecolor", "base_color", "color", "_col", "_diff", "_alb" };

        private static readonly string[] NormalPatterns =
            { "normal", "nrm", "_n.", "_n_", "_normal_", "norm", "nmap" };

        private static readonly string[] MetallicPatterns =
            { "metallic", "metalness", "_met", "_metal" };

        private static readonly string[] RoughnessPatterns =
            { "roughness", "rough", "_rgh", "_rough" };

        private static readonly string[] SmoothnessPatterns =
            { "smoothness", "smooth", "gloss", "glossiness" };

        private static readonly string[] AOPatterns =
            { "occlusion", "ambient", "_ao", "cavity" };

        private static readonly string[] EmissionPatterns =
            { "emission", "emissive", "glow", "selfillum", "illumination" };

        private static readonly string[] HeightPatterns =
            { "height", "displacement", "disp", "parallax", "_h." };

        #endregion

        #region Types

        public enum TextureType
        {
            Unknown,
            Albedo,
            Normal,
            Metallic,
            Roughness,
            Smoothness,
            AmbientOcclusion,
            Emission,
            Height,
            DetailAlbedo,
            DetailNormal
        }

        [Serializable]
        public class TextureAssignment
        {
            public Texture2D Texture;
            public TextureType DetectedType;
            public TextureType OverrideType;
            public bool UseOverride;
            public string TargetProperty;

            public TextureType EffectiveType => UseOverride ? OverrideType : DetectedType;
        }

        #endregion

        #region Fields

        // Settings
        private bool debugMode;
        private bool autoConfigureImport = true;

        // Lock state (not persisted - resets each session)
        private bool isLocked;

        // Material
        private Material targetMaterial;
        private string newMaterialName = "New Material";
        private string savePath = "Assets/Materials/";

        // Textures
        private List<TextureAssignment> textureAssignments = new List<TextureAssignment>();

        // UI State
        private Vector2 scrollPosition;
        private bool showSettings;
        private bool showPreview = true;
        private int processingProgress;
        private int processingTotal;
        private bool isProcessing;

        // Styles (lazy initialized)
        private GUIStyle headerStyle;
        private GUIStyle boxStyle;
        private GUIStyle statusStyle;

        #endregion

        #region Initialization

        [MenuItem("Tools/Material Texture Assigner")]
        public static void ShowWindow()
        {
            var window = GetWindow<MaterialTextureAssigner>("Material Texture Assigner");
            window.minSize = new Vector2(400, 500);
        }

        private void OnEnable()
        {
            LoadPreferences();
        }

        private void OnDisable()
        {
            SavePreferences();
        }

        private void LoadPreferences()
        {
            debugMode = EditorPrefs.GetBool(EditorPrefsKeys.DebugMode, false);
            savePath = EditorPrefs.GetString(EditorPrefsKeys.DefaultSavePath, "Assets/Materials/");
            autoConfigureImport = EditorPrefs.GetBool(EditorPrefsKeys.AutoConfigureImport, true);
        }

        private void SavePreferences()
        {
            EditorPrefs.SetBool(EditorPrefsKeys.DebugMode, debugMode);
            EditorPrefs.SetString(EditorPrefsKeys.DefaultSavePath, savePath);
            EditorPrefs.SetBool(EditorPrefsKeys.AutoConfigureImport, autoConfigureImport);
        }

        private void InitializeStyles()
        {
            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    margin = new RectOffset(0, 0, 10, 5)
                };
            }

            if (boxStyle == null)
            {
                boxStyle = new GUIStyle("box")
                {
                    padding = new RectOffset(10, 10, 10, 10),
                    margin = new RectOffset(0, 0, 5, 5)
                };
            }

            if (statusStyle == null)
            {
                statusStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(10, 10, 8, 8)
                };
            }
        }

        #endregion

        #region Selection Handling

        private void OnSelectionChange()
        {
            var selection = Selection.objects;

            // Check for material selection
            foreach (var obj in selection)
            {
                if (obj is Material material)
                {
                    targetMaterial = material;
                    Log($"Material selected: {material.name}");
                    break;
                }
            }

            // Get currently selected textures
            var selectedTextures = selection.OfType<Texture2D>().ToList();

            if (isLocked)
            {
                // Locked: only add new textures, never remove
                foreach (var texture in selectedTextures)
                {
                    if (!textureAssignments.Any(t => t.Texture == texture))
                    {
                        AddTexture(texture);
                    }
                }
            }
            else
            {
                // Unlocked: sync with selection (reset when clicking off)
                if (selectedTextures.Count > 0)
                {
                    // New textures selected - replace list
                    textureAssignments.Clear();
                    foreach (var texture in selectedTextures)
                    {
                        AddTexture(texture);
                    }
                }
                else
                {
                    // No textures selected - clear the list
                    textureAssignments.Clear();
                }
            }

            Repaint();
        }

        private void AddTexture(Texture2D texture)
        {
            var assignment = new TextureAssignment
            {
                Texture = texture,
                DetectedType = DetectTextureType(texture),
                UseOverride = false
            };
            assignment.TargetProperty = GetPropertyForType(assignment.EffectiveType);
            textureAssignments.Add(assignment);
            Log($"Added texture: {texture.name} (Detected: {assignment.DetectedType})");
        }

        #endregion

        #region Texture Type Detection

        private TextureType DetectTextureType(Texture2D texture)
        {
            string name = texture.name.ToLowerInvariant();
            string path = AssetDatabase.GetAssetPath(texture);

            // Check naming patterns (most reliable)
            if (MatchesAnyPattern(name, NormalPatterns))
                return TextureType.Normal;

            if (MatchesAnyPattern(name, MetallicPatterns))
                return TextureType.Metallic;

            if (MatchesAnyPattern(name, RoughnessPatterns))
                return TextureType.Roughness;

            if (MatchesAnyPattern(name, SmoothnessPatterns))
                return TextureType.Smoothness;

            if (MatchesAnyPattern(name, AOPatterns) || name.EndsWith("ao"))
                return TextureType.AmbientOcclusion;

            if (MatchesAnyPattern(name, EmissionPatterns))
                return TextureType.Emission;

            if (MatchesAnyPattern(name, HeightPatterns))
                return TextureType.Height;

            if (MatchesAnyPattern(name, AlbedoPatterns))
                return TextureType.Albedo;

            // Check import settings as fallback
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                if (importer.textureType == TextureImporterType.NormalMap)
                    return TextureType.Normal;
            }

            return TextureType.Unknown;
        }

        private bool MatchesAnyPattern(string name, string[] patterns)
        {
            return patterns.Any(p => name.Contains(p));
        }

        private string GetPropertyForType(TextureType type)
        {
            return type switch
            {
                TextureType.Albedo => ShaderProperties.BaseMap,
                TextureType.Normal => ShaderProperties.BumpMap,
                TextureType.Metallic => ShaderProperties.MetallicGlossMap,
                TextureType.Roughness => ShaderProperties.MetallicGlossMap,
                TextureType.Smoothness => ShaderProperties.MetallicGlossMap,
                TextureType.AmbientOcclusion => ShaderProperties.OcclusionMap,
                TextureType.Emission => ShaderProperties.EmissionMap,
                TextureType.Height => ShaderProperties.ParallaxMap,
                TextureType.DetailAlbedo => ShaderProperties.DetailAlbedoMap,
                TextureType.DetailNormal => ShaderProperties.DetailNormalMap,
                _ => string.Empty
            };
        }

        private Color GetTypeColor(TextureType type)
        {
            return type switch
            {
                TextureType.Albedo => new Color(0.4f, 0.8f, 0.4f),
                TextureType.Normal => new Color(0.5f, 0.5f, 1f),
                TextureType.Metallic => new Color(0.8f, 0.8f, 0.8f),
                TextureType.Roughness => new Color(0.6f, 0.6f, 0.6f),
                TextureType.Smoothness => new Color(0.7f, 0.7f, 0.7f),
                TextureType.AmbientOcclusion => new Color(0.9f, 0.6f, 0.6f),
                TextureType.Emission => new Color(1f, 0.9f, 0.3f),
                TextureType.Height => new Color(0.6f, 0.4f, 0.8f),
                TextureType.Unknown => new Color(1f, 0.5f, 0.5f),
                _ => Color.white
            };
        }

        #endregion

        #region Texture Import Configuration

        private void ConfigureTextureImport(Texture2D texture, TextureType type)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
            {
                LogWarning($"Could not get importer for texture: {texture.name}");
                return;
            }

            bool needsReimport = false;

            switch (type)
            {
                case TextureType.Normal:
                    needsReimport = ConfigureAsNormalMap(importer);
                    break;

                case TextureType.Metallic:
                case TextureType.Roughness:
                case TextureType.Smoothness:
                case TextureType.AmbientOcclusion:
                case TextureType.Height:
                    needsReimport = ConfigureAsLinearTexture(importer);
                    break;

                case TextureType.Albedo:
                case TextureType.Emission:
                    needsReimport = ConfigureAsSRGBTexture(importer);
                    break;
            }

            if (needsReimport)
            {
                Log($"Reimporting texture with updated settings: {texture.name}");
                importer.SaveAndReimport();
            }
        }

        private bool ConfigureAsNormalMap(TextureImporter importer)
        {
            bool changed = false;

            if (importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                changed = true;
            }

            if (importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                changed = true;
            }

            if (!importer.mipmapEnabled)
            {
                importer.mipmapEnabled = true;
                changed = true;
            }

            if (!importer.streamingMipmaps)
            {
                importer.streamingMipmaps = true;
                changed = true;
            }

            return changed;
        }

        private bool ConfigureAsLinearTexture(TextureImporter importer)
        {
            bool changed = false;

            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                changed = true;
            }

            if (importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                changed = true;
            }

            if (!importer.mipmapEnabled)
            {
                importer.mipmapEnabled = true;
                changed = true;
            }

            if (!importer.streamingMipmaps)
            {
                importer.streamingMipmaps = true;
                changed = true;
            }

            return changed;
        }

        private bool ConfigureAsSRGBTexture(TextureImporter importer)
        {
            bool changed = false;

            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                changed = true;
            }

            if (!importer.sRGBTexture)
            {
                importer.sRGBTexture = true;
                changed = true;
            }

            if (!importer.mipmapEnabled)
            {
                importer.mipmapEnabled = true;
                changed = true;
            }

            if (!importer.streamingMipmaps)
            {
                importer.streamingMipmaps = true;
                changed = true;
            }

            return changed;
        }

        #endregion

        #region Material Operations

        private bool ValidateShaderAvailability(string shaderName, out Shader shader)
        {
            shader = Shader.Find(shaderName);
            if (shader == null)
            {
                LogError($"Shader not found: {shaderName}. Please ensure the required render pipeline is installed.");
                return false;
            }
            return true;
        }

        private void CreateNewMaterial()
        {
            // Validate shader availability
            if (!ValidateShaderAvailability(Shaders.URPLit, out Shader shader))
            {
                // Fallback to Standard shader
                if (!ValidateShaderAvailability(Shaders.BuiltInStandard, out shader))
                {
                    EditorUtility.DisplayDialog("Error",
                        "Could not find URP Lit or Standard shader. Please ensure a render pipeline is configured.",
                        "OK");
                    return;
                }
                Log("URP not available, using Standard shader as fallback.");
            }

            // Validate and create directory
            string normalizedPath = savePath.Replace("\\", "/");
            if (!normalizedPath.EndsWith("/"))
                normalizedPath += "/";

            if (!normalizedPath.StartsWith("Assets/"))
            {
                EditorUtility.DisplayDialog("Error",
                    "Save path must be within the Assets folder.",
                    "OK");
                return;
            }

            try
            {
                if (!AssetDatabase.IsValidFolder(normalizedPath.TrimEnd('/')))
                {
                    string[] folders = normalizedPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    string currentPath = folders[0]; // "Assets"

                    for (int i = 1; i < folders.Length; i++)
                    {
                        string nextPath = currentPath + "/" + folders[i];
                        if (!AssetDatabase.IsValidFolder(nextPath))
                        {
                            AssetDatabase.CreateFolder(currentPath, folders[i]);
                            Log($"Created folder: {nextPath}");
                        }
                        currentPath = nextPath;
                    }
                }

                // Create material
                Material material = new Material(shader);
                string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{normalizedPath}{newMaterialName}.mat");

                AssetDatabase.CreateAsset(material, assetPath);
                AssetDatabase.SaveAssets();

                targetMaterial = material;
                EditorGUIUtility.PingObject(material);
                Selection.activeObject = material;

                Log($"Created new material: {assetPath}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to create material: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to create material: {ex.Message}", "OK");
            }
        }

        private void AssignTextures()
        {
            if (targetMaterial == null)
            {
                LogError("No target material selected.");
                return;
            }

            var validAssignments = textureAssignments
                .Where(a => a.Texture != null && a.EffectiveType != TextureType.Unknown)
                .ToList();

            if (validAssignments.Count == 0)
            {
                EditorUtility.DisplayDialog("No Textures",
                    "No valid textures to assign. Please add textures and ensure they have a detected or assigned type.",
                    "OK");
                return;
            }

            isProcessing = true;
            processingTotal = validAssignments.Count;
            processingProgress = 0;

            try
            {
                Undo.RecordObject(targetMaterial, "Assign Textures to Material");

                // Group assignments by property to handle conflicts
                var assignmentsByProperty = validAssignments
                    .GroupBy(a => a.TargetProperty)
                    .ToList();

                foreach (var group in assignmentsByProperty)
                {
                    string property = group.Key;
                    var assignments = group.ToList();

                    if (assignments.Count > 1)
                    {
                        // Handle metallic/roughness channel packing
                        if (property == ShaderProperties.MetallicGlossMap)
                        {
                            HandleMetallicRoughnessAssignment(assignments);
                        }
                        else
                        {
                            // Multiple textures for same slot - use first and warn
                            LogWarning($"Multiple textures assigned to {property}. Using: {assignments[0].Texture.name}");
                            AssignSingleTexture(assignments[0]);
                        }
                    }
                    else
                    {
                        AssignSingleTexture(assignments[0]);
                    }

                    processingProgress += assignments.Count;
                }

                EditorUtility.SetDirty(targetMaterial);
                AssetDatabase.SaveAssets();

                Log($"Successfully assigned {validAssignments.Count} texture(s) to {targetMaterial.name}");
            }
            catch (Exception ex)
            {
                LogError($"Error during texture assignment: {ex.Message}");
            }
            finally
            {
                isProcessing = false;
            }
        }

        private void AssignSingleTexture(TextureAssignment assignment)
        {
            if (autoConfigureImport)
            {
                ConfigureTextureImport(assignment.Texture, assignment.EffectiveType);
            }

            string property = assignment.TargetProperty;

            // Check for property existence with fallbacks
            if (!targetMaterial.HasProperty(property))
            {
                // Try fallback properties
                string fallback = GetFallbackProperty(property);
                if (!string.IsNullOrEmpty(fallback) && targetMaterial.HasProperty(fallback))
                {
                    property = fallback;
                    Log($"Using fallback property {fallback} for {assignment.Texture.name}");
                }
                else
                {
                    LogWarning($"Material does not have property '{property}' for texture '{assignment.Texture.name}'");
                    return;
                }
            }

            targetMaterial.SetTexture(property, assignment.Texture);

            // Enable emission if assigning emission map
            if (assignment.EffectiveType == TextureType.Emission)
            {
                targetMaterial.EnableKeyword("_EMISSION");
                if (targetMaterial.HasProperty("_EmissionColor"))
                {
                    targetMaterial.SetColor("_EmissionColor", Color.white);
                }
            }

            Log($"Assigned {assignment.Texture.name} to {property}");
        }

        private void HandleMetallicRoughnessAssignment(List<TextureAssignment> assignments)
        {
            var metallic = assignments.FirstOrDefault(a => a.EffectiveType == TextureType.Metallic);
            var roughness = assignments.FirstOrDefault(a => a.EffectiveType == TextureType.Roughness);
            var smoothness = assignments.FirstOrDefault(a => a.EffectiveType == TextureType.Smoothness);

            // If we have both metallic and roughness, warn about channel packing
            if (metallic != null && (roughness != null || smoothness != null))
            {
                LogWarning("Both Metallic and Roughness/Smoothness textures detected. " +
                           "URP expects these packed into one texture (Metallic in R, Smoothness in A). " +
                           "Assigning Metallic texture. Consider using a channel-packed texture.");
            }

            // Prefer metallic, then smoothness, then roughness
            var textureToUse = metallic ?? smoothness ?? roughness;
            if (textureToUse != null)
            {
                AssignSingleTexture(textureToUse);

                // If using roughness, set smoothness to 0 so roughness texture controls it
                if (textureToUse == roughness && targetMaterial.HasProperty("_Smoothness"))
                {
                    targetMaterial.SetFloat("_Smoothness", 0f);
                }
            }
        }

        private string GetFallbackProperty(string property)
        {
            return property switch
            {
                ShaderProperties.BaseMap => ShaderProperties.MainTex,
                _ => null
            };
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            InitializeStyles();

            EditorGUILayout.Space(10);

            // Header with settings toggle
            DrawHeader();

            // Settings panel
            if (showSettings)
            {
                DrawSettingsPanel();
            }

            EditorGUILayout.Space(5);

            // Material section
            DrawMaterialSection();

            EditorGUILayout.Space(10);

            // Texture section
            DrawTextureSection();

            EditorGUILayout.Space(10);

            // Preview section
            if (showPreview && textureAssignments.Count > 0)
            {
                DrawPreviewSection();
            }

            EditorGUILayout.Space(10);

            // Action buttons
            DrawActionButtons();

            // Progress bar during processing
            if (isProcessing)
            {
                EditorGUI.ProgressBar(
                    GUILayoutUtility.GetRect(18, 22),
                    (float)processingProgress / processingTotal,
                    $"Processing {processingProgress}/{processingTotal}"
                );
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Material Texture Assigner", headerStyle);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(showSettings ? "Hide Settings" : "Settings", GUILayout.Width(100)))
            {
                showSettings = !showSettings;
            }
            EditorGUILayout.EndHorizontal();

            // Status line
            string status = GetStatusMessage();
            EditorGUILayout.LabelField(status, statusStyle);
        }

        private string GetStatusMessage()
        {
            string lockIndicator = isLocked ? " [LOCKED]" : "";

            if (targetMaterial == null)
                return $"Select or create a material to begin.{lockIndicator}";

            if (textureAssignments.Count == 0)
                return $"Material: {targetMaterial.name} | Select textures in Project window.{lockIndicator}";

            int validCount = textureAssignments.Count(a => a.EffectiveType != TextureType.Unknown);
            int unknownCount = textureAssignments.Count - validCount;

            string msg = $"Material: {targetMaterial.name} | {validCount} texture(s) ready";
            if (unknownCount > 0)
                msg += $" | {unknownCount} unknown";

            msg += lockIndicator;

            return msg;
        }

        private void DrawSettingsPanel()
        {
            EditorGUILayout.BeginVertical(boxStyle);

            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            debugMode = EditorGUILayout.Toggle(
                new GUIContent("Debug Logging", "Enable detailed logging to Console"),
                debugMode);

            autoConfigureImport = EditorGUILayout.Toggle(
                new GUIContent("Auto-Configure Import", "Automatically configure texture import settings"),
                autoConfigureImport);

            EditorGUILayout.Space(5);

            savePath = EditorGUILayout.TextField(
                new GUIContent("Default Save Path", "Default path for new materials"),
                savePath);

            EditorGUILayout.EndVertical();
        }

        private void DrawMaterialSection()
        {
            EditorGUILayout.LabelField("Material", headerStyle);

            EditorGUILayout.BeginVertical(boxStyle);

            targetMaterial = (Material)EditorGUILayout.ObjectField(
                "Target Material",
                targetMaterial,
                typeof(Material),
                false);

            if (targetMaterial == null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Create New Material", EditorStyles.boldLabel);

                newMaterialName = EditorGUILayout.TextField("Name", newMaterialName);

                EditorGUILayout.BeginHorizontal();
                savePath = EditorGUILayout.TextField("Path", savePath);
                if (GUILayout.Button("Browse", GUILayout.Width(60)))
                {
                    string selected = EditorUtility.OpenFolderPanel("Select Save Location", "Assets", "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        if (selected.StartsWith(Application.dataPath))
                        {
                            savePath = "Assets" + selected.Substring(Application.dataPath.Length) + "/";
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);

                if (GUILayout.Button("Create Material", GUILayout.Height(28)))
                {
                    CreateNewMaterial();
                }
            }
            else
            {
                // Show material info
                EditorGUILayout.LabelField("Shader", targetMaterial.shader.name, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTextureSection()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Textures", headerStyle);
            GUILayout.FlexibleSpace();

            // Lock toggle button
            Color originalBg = GUI.backgroundColor;
            if (isLocked)
            {
                GUI.backgroundColor = new Color(1f, 0.85f, 0.4f); // Yellow/gold when locked
            }

            GUIContent lockContent = new GUIContent(
                isLocked ? "Locked" : "Lock",
                isLocked
                    ? "Textures are locked. Click to unlock (will clear on deselect)."
                    : "Click to lock textures (prevents clearing when clicking away)."
            );

            if (GUILayout.Button(lockContent, GUILayout.Width(60)))
            {
                isLocked = !isLocked;
                Log(isLocked ? "Texture selection locked" : "Texture selection unlocked");
            }
            GUI.backgroundColor = originalBg;

            if (GUILayout.Button("Clear All", GUILayout.Width(70)))
            {
                textureAssignments.Clear();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical(boxStyle);

            if (textureAssignments.Count == 0)
            {
                string helpText = isLocked
                    ? "Selection is LOCKED. Select textures to add them.\nThey will stay until manually removed."
                    : "Select textures in the Project window to add them here.\nTexture types are auto-detected from naming conventions.";

                EditorGUILayout.HelpBox(helpText, MessageType.Info);
            }
            else
            {
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(200));

                for (int i = 0; i < textureAssignments.Count; i++)
                {
                    DrawTextureRow(i);
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTextureRow(int index)
        {
            var assignment = textureAssignments[index];

            EditorGUILayout.BeginHorizontal();

            // Color indicator for type
            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = GetTypeColor(assignment.EffectiveType);

            EditorGUILayout.BeginVertical("box", GUILayout.Width(position.width - 100));

            EditorGUILayout.BeginHorizontal();

            // Texture field
            assignment.Texture = (Texture2D)EditorGUILayout.ObjectField(
                assignment.Texture,
                typeof(Texture2D),
                false,
                GUILayout.Width(60),
                GUILayout.Height(60));

            EditorGUILayout.BeginVertical();

            // Texture name
            EditorGUILayout.LabelField(assignment.Texture != null ? assignment.Texture.name : "None", EditorStyles.boldLabel);

            // Type selection
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Type:", GUILayout.Width(35));

            if (assignment.UseOverride)
            {
                assignment.OverrideType = (TextureType)EditorGUILayout.EnumPopup(assignment.OverrideType, GUILayout.Width(120));
            }
            else
            {
                EditorGUILayout.LabelField(assignment.DetectedType.ToString(), GUILayout.Width(120));
            }

            assignment.UseOverride = EditorGUILayout.Toggle(assignment.UseOverride, GUILayout.Width(20));
            EditorGUILayout.LabelField("Override", GUILayout.Width(55));

            EditorGUILayout.EndHorizontal();

            // Update target property when type changes
            assignment.TargetProperty = GetPropertyForType(assignment.EffectiveType);

            // Target property display
            EditorGUILayout.LabelField($"→ {assignment.TargetProperty}", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            GUI.backgroundColor = originalColor;

            // Remove button
            if (GUILayout.Button("×", GUILayout.Width(25), GUILayout.Height(60)))
            {
                textureAssignments.RemoveAt(index);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
        }

        private void DrawPreviewSection()
        {
            showPreview = EditorGUILayout.Foldout(showPreview, "Assignment Preview", true);

            if (!showPreview) return;

            EditorGUILayout.BeginVertical(boxStyle);

            var assignments = textureAssignments
                .Where(a => a.EffectiveType != TextureType.Unknown)
                .GroupBy(a => a.TargetProperty)
                .ToList();

            if (assignments.Count == 0)
            {
                EditorGUILayout.HelpBox("No valid assignments. Set texture types manually or add textures with recognizable names.", MessageType.Warning);
            }
            else
            {
                foreach (var group in assignments)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(group.Key, EditorStyles.boldLabel, GUILayout.Width(150));
                    EditorGUILayout.LabelField("←", GUILayout.Width(20));

                    string textureNames = string.Join(", ", group.Select(a => a.Texture.name));
                    EditorGUILayout.LabelField(textureNames);

                    if (group.Count() > 1 && group.Key == ShaderProperties.MetallicGlossMap)
                    {
                        EditorGUILayout.LabelField("⚠", GUILayout.Width(20));
                    }

                    EditorGUILayout.EndHorizontal();
                }

                // Warnings
                var metallicGroup = assignments.FirstOrDefault(g => g.Key == ShaderProperties.MetallicGlossMap);
                if (metallicGroup != null && metallicGroup.Count() > 1)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.HelpBox(
                        "Multiple textures target MetallicGlossMap. URP expects channel-packed texture (Metallic in R, Smoothness in A).",
                        MessageType.Warning);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = targetMaterial != null && textureAssignments.Any(a => a.EffectiveType != TextureType.Unknown);

            if (GUILayout.Button("Assign Textures", GUILayout.Height(35)))
            {
                AssignTextures();
            }

            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Logging

        private void Log(string message)
        {
            if (debugMode)
            {
                Debug.Log($"[MaterialTextureAssigner] {message}");
            }
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[MaterialTextureAssigner] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[MaterialTextureAssigner] {message}");
        }

        #endregion
    }
}
