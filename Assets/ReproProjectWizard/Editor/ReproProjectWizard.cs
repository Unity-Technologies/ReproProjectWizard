using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using UnityEngine.Rendering;

/// <summary>
/// Create a cut down repro project by copying the dependencies for a few key assets.
/// </summary>
public class ReproProjectWizard : EditorWindow
{
    public static string SettingsPath = "Assets/ReproProjectWizard/ReproProjectSettings.asset";
    public static string[] TextureExtensions = new string[] { ".png", ".jpg" };
    public static string[] ImportTextureExtensions = new string[] { ".psd", ".tif", ".tiff", ".tga", ".gif", ".bmp", ".iff", ".pict" };
    public static string TempPath = "Assets/ReproProjectWizard/Temp";

    private bool m_IsInitialized = false;

    private string m_CurrentProjectPath = "";
    private string m_NewProjectName = "";
    private string m_NewProjectPath = "";

    private int m_TextureScaleFactor = 1;
    private int m_TextureScaleIndex = 0;
    private int[] m_TextureScaleFactors = new int[] { 1, 2, 4, 8, 16 };
    private string[] m_TextureScaleFactorNames = new string[] { "Full", "Half", "Quarter", "Eighth", "Sixteenth" };

    public string NewProjectPath
    {
        get
        {
            if (string.IsNullOrEmpty(m_NewProjectName) || string.IsNullOrEmpty(m_NewProjectPath))
            {
                return null;
            }
            return Path.Combine(m_NewProjectPath, m_NewProjectName);
        }
        set
        {
            // special case if the path is actually a unity project
            if (Directory.Exists(Path.Combine(value, "Assets")) && Directory.Exists(Path.Combine(value, "ProjectSettings")))
            {
                m_NewProjectName = Path.GetFileName(value);
                m_NewProjectPath = Path.GetDirectoryName(value);
            }
            else
            {
                m_NewProjectPath = value;
            }
        }
    }

    // Open project after export
    private bool m_OpenProject = false;

    // Additional UI data for the input list
    // Store whether the each item is shown as text or an object
    // If an object is shown, store a reference to the object
    private struct InputListItem
    {
        public ReproProjectAssetType AssetType;
        public string AssetPath;
        public UnityEngine.Object AssetObject;
    }

    // Input list of paths and wildcards to assets that are to be included in the new project.
    // All dependencies of these assets are also included in the new project.
    private List<InputListItem> m_InputItems = new List<InputListItem>();

    // List of assets that have to be included in any new project.
    // Can be edited but will likely always be the same.
    // Kept as a separate list so m_InputPaths can be quickly reset and these assets don't clutter the display.
    private List<InputListItem> m_ProjectItems = new List<InputListItem>();
    private bool m_ProjectPathsVisible = false;

    // List of files to copy into the new project
    // Stored as a HashSet to avoid copying files more than once
    private HashSet<string> m_FilesToCopy = new HashSet<string>();

    // Store the current state of the wizard between sessions
    private ReproProjectSettings m_Settings = null;

    // Progress bar data
    private string m_ProgressBarTitle = "";
    private string m_ProgressBarInfo = "";
    private float m_ProgressBarStart = 0.0f;
    private float m_ProgressBarEnd = 1.0f;

    // Additional assets that always have to be copied (likely project specific)
    private static string[] m_DefaultAssets = new string[]
    {
    };

    // Create the window
    [MenuItem("Window/Repro Project Wizard")]
    public static void CreateWindow()
    {
        ReproProjectWizard window = EditorWindow.GetWindow<ReproProjectWizard>(utility: true, title: "Repro Project Wizard", focus: true);
        window.Initialize();
    }

    public void OnGUI()
    {
        EditorGUI.BeginChangeCheck();

        // List of all 'root' assets to include in the new project
        GUILayout.Label("Assets to Copy");
        EditorGUI.indentLevel++;
        EditorGUILayout.HelpBox("List all assets you would like to copy to the new project. All dependencies of that asset will also be added to the project. For example, add a scene file to ensure everything referenced in that scene is added.", MessageType.Info);
        OnGUIInputList(m_InputItems);
        EditorGUI.indentLevel--;

        // Give an option to add the current scene if it hasn't been added already
#if UNITY_5_3_OR_NEWER
      string currentScene = SceneManager.GetActiveScene().path;
#else
        string currentScene = EditorApplication.currentScene;
#endif
        if (!IsInInputs(currentScene))
        {
            if (GUILayout.Button("Add Current Scene"))
            {
                m_InputItems.Add(new InputListItem() { AssetType = ReproProjectAssetType.Wildcard, AssetPath = currentScene, AssetObject = null });
            }
        }
        GUILayout.FlexibleSpace();

        // Project Settings
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Project Name", GUILayout.Width(80));
        m_NewProjectName = EditorGUILayout.TextField(m_NewProjectName);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Project Path", GUILayout.Width(80));
        m_NewProjectPath = EditorGUILayout.TextField(m_NewProjectPath);
        if (GUILayout.Button("...", GUILayout.Width(40)))
        {
            string newPath = EditorUtility.OpenFolderPanel("Select New Project Location", string.IsNullOrEmpty(m_NewProjectPath) ? "" : m_NewProjectPath, "");
            if (newPath != m_NewProjectPath && !string.IsNullOrEmpty(newPath))
            {
                // assign to the property which will handle the special case where a unity project path has been selected.
                NewProjectPath = newPath;
            }
            GUI.FocusControl("");
        }
        EditorGUILayout.EndHorizontal();
        if (!IsProjectPathValid())
        {
            EditorGUILayout.HelpBox("Select a valid location for the new project.", MessageType.Error);
        }
        GUILayout.Space(5);
        m_ProjectPathsVisible = EditorGUILayout.Foldout(m_ProjectPathsVisible, "Common Files");
        if (m_ProjectPathsVisible)
        {
            EditorGUI.indentLevel++;
            OnGUIInputList(m_ProjectItems);
            EditorGUILayout.HelpBox("Add assets here that need to be included in any repro project. Specifically, include assets referenced by the Graphics settings. This is also a good place to include assets loaded directly from code.", MessageType.Info);
            EditorGUI.indentLevel--;
        }
        GUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();
        m_OpenProject = EditorGUILayout.Toggle("Open project after export", m_OpenProject);
        m_TextureScaleIndex = EditorGUILayout.Popup("Texture Size", m_TextureScaleIndex, m_TextureScaleFactorNames);
        m_TextureScaleFactor = m_TextureScaleFactors[m_TextureScaleIndex];
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("Create Project"))
        {
            if (!IsInputValid())
            {
                EditorUtility.DisplayDialog("Error", "No assets to copy to the new project.", "OK");
            }
            else if (!IsProjectPathValid())
            {
                EditorUtility.DisplayDialog("Error", "Invalid Project Path.", "OK");
            }
            else
            {
                CreateProject();
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            SaveSettings();
        }
    }

    // Filter which assets appear in the asset browser based on the AssetType field
    private static string[] m_AssetTypesPopup = System.Enum.GetNames(typeof(ReproProjectAssetType));
    private static System.Type[] m_AssetBrowserTypes = new System.Type[] { typeof(UnityEngine.Object), typeof(UnityEditor.SceneAsset), typeof(UnityEngine.GameObject), typeof(UnityEngine.Object) };

    private void OnGUIInputList(List<InputListItem> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            InputListItem item = items[i];
            EditorGUILayout.BeginHorizontal();

            // Toggle between text and object selection
            ReproProjectAssetType assetType = (ReproProjectAssetType)EditorGUILayout.Popup((int)item.AssetType, m_AssetTypesPopup, GUILayout.Width(80));
            if (assetType != item.AssetType)
            {
                item.AssetObject = (assetType == ReproProjectAssetType.Wildcard) ? null : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.AssetPath);
                item.AssetType = assetType;
            }

            if (assetType == ReproProjectAssetType.Wildcard)
            {
                // Text field
                item.AssetPath = EditorGUILayout.TextField(item.AssetPath);

                // File browser button
                if (GUILayout.Button("...", GUILayout.Width(40)))
                {
                    string dir = "Assets";
                    string parentDir = string.IsNullOrEmpty(item.AssetPath) ? "" : Path.GetDirectoryName(item.AssetPath);
                    if (Directory.Exists(item.AssetPath))
                    {
                        dir = item.AssetPath;
                    }
                    else if (Directory.Exists(parentDir))
                    {
                        dir = parentDir;
                    }
                    string newAsset = EditorUtility.OpenFilePanel("Add File", dir, "");
                    if (!string.IsNullOrEmpty(newAsset))
                    {
                        newAsset = newAsset.Replace(m_CurrentProjectPath, "").TrimStart(new char[] { '/', '\\' });
                        item.AssetPath = newAsset;
                        GUI.FocusControl("");
                    }
                }
            }
            else
            {
                // Object selection field
                UnityEngine.Object obj = EditorGUILayout.ObjectField(item.AssetObject, m_AssetBrowserTypes[(int)item.AssetType], allowSceneObjects: false);
                if (obj != item.AssetObject)
                {
                    item.AssetObject = obj;
                    item.AssetPath = obj != null ? AssetDatabase.GetAssetPath(obj) : "";
                }
            }

            // save item data back into list
            items[i] = item;

            // Remove entry button
            if (GUILayout.Button("-", GUILayout.Width(40)))
            {
                // remove this item from the list
                items.RemoveAt(i);
                i--;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("", GUILayout.Width(10));
        if (GUILayout.Button("+"))
        {
            items.Add(new InputListItem() { AssetType = ReproProjectAssetType.Scene, AssetPath = "", AssetObject = null });
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// Initialize the window
    /// </summary>
    private void Initialize()
    {
        if (!m_IsInitialized)
        {
            m_IsInitialized = true;
            m_CurrentProjectPath = Path.GetDirectoryName(Application.dataPath);
            InitializeItems(m_ProjectItems, m_DefaultAssets);

            LoadSettings();
        }
    }

    /// <summary>
    /// Create the Repro project using the current settings
    /// </summary>
    private void CreateProject()
    {
        try
        {
            if (Directory.Exists(NewProjectPath) &&
               (Directory.GetFiles(NewProjectPath).Length != 0 || Directory.GetDirectories(NewProjectPath).Length != 0))
            {
                if (EditorUtility.DisplayDialog("Project Already Exists!",
                   string.Format("There is already an existing project at `{0}`.\nDo you want to overwrite this project?", NewProjectPath),
                   "OK", "Cancel"))
                {
                    DisplayProgressBar("Creating Repro Project", "Deleting Old Project", 0.0f, 0.05f);
                    RecursiveDelete(NewProjectPath);
                }
                else
                {
                    return;
                }
            }

            DisplayProgressBar("Creating Repro Project", "Finding Files", 0.05f, 0.1f);
            CreateTempDirectory();

            // common project files
            AddFiles("ProjectSettings/*.asset");
            AddFiles("ProjectSettings/*.txt");
            AddFiles("Assets/*.cginc");
            AddFiles("Assets/*.hlsl");
            AddFiles("Assets/*SRPMARKER");
            AddFiles("Assets/*.dll");
            AddFiles("Assets/*.cs");
            AddFiles("Assets/*.rsp");
            AddFiles("Assets/*.asmdef");
            AddFiles("Packages/manifest.json");
            AddFiles("Assets/*package.json");

            // Get all the dependencies of the graphics settings
            AddGraphicsSettings();

            // Find all dependencies
            DisplayProgressBar("Creating Repro Project", "Collect Dependencies", 0.1f, 0.2f);
            AddDependencies(m_ProjectItems);
            AddDependencies(m_InputItems);

            // finally copy over all the assets and dependencies
            DisplayProgressBar("Creating Repro Project", "Assets: ", 0.2f, 1.0f);
            CopyFiles();

            if (m_OpenProject)
            {
                EditorApplication.OpenProject(NewProjectPath);
            }
        }
        finally
        {
            ClearProgressBar();
        }
    }

    /// <summary>
    /// Reset all parameters to the values stored in the settings file.
    /// </summary>
    private void LoadSettings()
    {
        if (File.Exists(SettingsPath))
        {
            m_Settings = AssetDatabase.LoadAssetAtPath<ReproProjectSettings>(SettingsPath);

            m_NewProjectName = m_Settings.ProjectName;
            m_NewProjectPath = m_Settings.ProjectPath;
            m_OpenProject = m_Settings.OpenProject;
            m_TextureScaleFactor = m_Settings.TextureScale;
            m_TextureScaleIndex = 0;
            for (int i = 0; i < m_TextureScaleFactors.Length; i++)
            {
                if (m_TextureScaleFactor == m_TextureScaleFactors[i])
                {
                    m_TextureScaleIndex = i;
                    break;
                }
            }

            CopyItemsFromSettings(m_InputItems, m_Settings.InputFiles);
            CopyItemsFromSettings(m_ProjectItems, m_Settings.ProjectFiles);
        }
    }

    /// <summary>
    /// Write the current state of the wizard window to the settings file.
    /// </summary>
    private void SaveSettings()
    {
        if (m_Settings == null)
        {
            m_Settings = ScriptableObject.CreateInstance<ReproProjectSettings>();
            AssetDatabase.CreateAsset(m_Settings, SettingsPath);
        }

        m_Settings.ProjectName = m_NewProjectName;
        m_Settings.ProjectPath = m_NewProjectPath;
        m_Settings.OpenProject = m_OpenProject;
        m_Settings.TextureScale = m_TextureScaleFactor;

        CopyItemsToSettings(ref m_Settings.InputFiles, m_InputItems);
        CopyItemsToSettings(ref m_Settings.ProjectFiles, m_ProjectItems);

        EditorUtility.SetDirty(m_Settings);
    }

    /// <summary>
    /// Convert all path separators to match standard Unity format
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    private string NormalizePath(string path)
    {
        path = path.Replace('\\', '/').Replace("//", "/");
        return path;
    }

    /// <summary>
    /// Add files to be copied to the new project.
    /// </summary>
    /// <param name="results">Where to save the resulting files</param>
    /// <param name="path">Either a direct asset path, a directory or a wildcard.</param>
    private void AddFiles(HashSet<string> results, string path)
    {
        string sourcePath = Path.Combine(m_CurrentProjectPath, path);
        if (File.Exists(sourcePath))
        {
            results.Add(NormalizePath(path));
        }
        else
        {
            // Add files from a wildcard
            string dir = "";
            string pattern = "";
            if (Directory.Exists(sourcePath))
            {
                // If path is a directory, add everything in that directory
                dir = sourcePath;
                pattern = "*";
            }
            else
            {
                dir = Path.GetDirectoryName(sourcePath);
                pattern = Path.GetFileName(sourcePath);
            }

            // Do the actual pattern match on the current project files
            string[] foundFiles = Directory.GetFiles(dir, pattern, SearchOption.AllDirectories);
            foreach (string file in foundFiles)
            {
                string trimmedFile = file.Replace(m_CurrentProjectPath, "").TrimStart(new char[] { '/', '\\' });
                results.Add(NormalizePath(trimmedFile));
            }
        }
    }
    private void AddFiles(string path)
    {
        AddFiles(m_FilesToCopy, path);
    }

    /// <summary>
    /// Take the input list of assets or wildcards and add all dependencies to be copied to the new project.
    /// </summary>
    /// <param name="inputs"></param>
    private void AddDependencies(List<InputListItem> inputs)
    {
        // pattern match all the inputs
        HashSet<string> expandedInputs = new HashSet<string>();
        foreach (InputListItem input in inputs)
        {
            AddFiles(expandedInputs, input.AssetPath);
        }
        // add the input files
        foreach (string file in expandedInputs)
        {
            m_FilesToCopy.Add(file);
        }
        AddDependencies(expandedInputs);
    }

    private void AddDependencies(HashSet<string> inputs)
    {
        // find all dependencies
        string[] inputArray = new string[inputs.Count];
        inputs.CopyTo(inputArray);
        string[] dependencies = AssetDatabase.GetDependencies(inputArray);

        // Add all files to be copied
        foreach (string file in dependencies)
        {
            m_FilesToCopy.Add(NormalizePath(file));
        }
    }

    /// <summary>
    /// Cleanup any existing temp directory and then create it again. 
    /// </summary>
    private void CreateTempDirectory()
    {
        if (Directory.Exists(TempPath))
        {
            Directory.Delete(TempPath, recursive: true);
        }
        Directory.CreateDirectory(TempPath);
    }

    /// <summary>
    /// We need to get all the dependencies of the graphics settings
    /// Grab everything we can from the settings that is exposed to script
    /// </summary>
    private void AddGraphicsSettings()
    {
        HashSet<string> settingsFiles = new HashSet<string>();
        if (GraphicsSettings.renderPipelineAsset != null)
        {
            string rpAssetPath = AssetDatabase.GetAssetPath(GraphicsSettings.renderPipelineAsset);
            Debug.LogFormat("Render Pipeline Asset {0}", rpAssetPath);
            settingsFiles.Add(rpAssetPath);
        }

        foreach (BuiltinShaderType shaderType in System.Enum.GetValues(typeof(BuiltinShaderType)))
        {
            Shader shader = GraphicsSettings.GetCustomShader(shaderType);
            if (shader != null)
            {
                string shaderPath = AssetDatabase.GetAssetPath(shader);
                Debug.LogFormat("Shader {0}", shaderPath);
                if (File.Exists(shaderPath))
                {
                    settingsFiles.Add(shaderPath);
                }
            }
        }

        AddDependencies(settingsFiles);
    }

    /// <summary>
    /// Copy all files into the new project.
    /// Also copy any meta file if one exists next to the source file.
    /// </summary>
    private void CopyFiles()
    {
		if (!string.IsNullOrEmpty(m_ProgressBarTitle))
        {
            EditorUtility.DisplayProgressBar(m_ProgressBarTitle, m_ProgressBarInfo + "Directories", m_ProgressBarStart);
        }

        // create directories, but only once for each possible directory
        HashSet<string> dirs = new HashSet<string>();
        foreach (string file in m_FilesToCopy)
        {
            dirs.Add(Path.GetDirectoryName(file));
        }
        foreach (string directory in dirs)
        {
            Directory.CreateDirectory(Path.Combine(NewProjectPath, directory));
        }

        // copy files
        int i = 0;
        foreach (string file in m_FilesToCopy)
        {
            if (!string.IsNullOrEmpty(m_ProgressBarTitle))
            {
                EditorUtility.DisplayProgressBar(m_ProgressBarTitle, m_ProgressBarInfo + Path.GetFileName(file),
                   m_ProgressBarStart + (m_ProgressBarEnd - m_ProgressBarStart) * (float)i / (float)m_FilesToCopy.Count);
            }

            string source = Path.Combine(m_CurrentProjectPath, file);
            string destination = Path.Combine(NewProjectPath, file);

            if (!File.Exists(destination))
            {
                if (IsTexture(destination) && (m_TextureScaleFactor > 1))
                {
                    CopyTexture(source, destination, m_TextureScaleFactor);
                }
                else
                {
                    File.Copy(source, destination);
                    File.SetAttributes(destination, FileAttributes.Normal);
                }
            }

            // copy the meta file as well if it exists
            string metaSource = source + ".meta";
            string metaDestination = destination + ".meta";
            if (File.Exists(metaSource) && !File.Exists(metaDestination))
            {
                File.Copy(metaSource, metaDestination);
                File.SetAttributes(metaDestination, FileAttributes.Normal);
            }

            i++;
        }
    }

    /// <summary>
    /// True if the path refers to a texture asset that can be resized.
    /// </summary>
    private bool IsTexture(string path)
    {
        bool result = false;
        string extension = Path.GetExtension(path).ToLower();
        for (int i = 0; i < 2; i++)
        {
            string[] list = (i == 0) ? TextureExtensions : ImportTextureExtensions;
            foreach (string ext in list)
            {
                if (ext == extension)
                {
                    result = true;
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// True if the file path refers to a texture that can only be loaded by importing.
    /// </summary>
    private bool IsImportTexture(string path)
    {
        bool result = false;
        string extension = Path.GetExtension(path).ToLower();
        foreach (string ext in ImportTextureExtensions)
        {
            if (ext == extension)
            {
                result = true;
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Copy a texture to the new project while downscaling it.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="destination"></param>
    /// <param name="scaleFactor"></param>
    private void CopyTexture(string source, string destination, int scaleFactor)
    {
        // load source
        Texture2D sourceTex = new Texture2D(64, 64);
        if (!IsImportTexture(source))
        {
            byte[] sourceData = File.ReadAllBytes(source);
            sourceTex.LoadImage(sourceData, markNonReadable: false);
        }
        else
        {
            // copy the source to a temp location
            string filename = Path.GetFileName(source);
            string temppath = Path.Combine(TempPath, filename);
            File.Copy(source, temppath);

            // import the temp texture so we can access formats that aren't png/jpg
            // need to import once to create the importer object, then a second time after changing the settings, sigh...
            AssetDatabase.ImportAsset(temppath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(temppath) as TextureImporter;
            importer.isReadable = true;
#if UNITY_5_6_OR_NEWER
			importer.textureCompression = TextureImporterCompression.Uncompressed;
#else
			importer.textureFormat = TextureImporterFormat.AutomaticTruecolor;
#endif
            AssetDatabase.ImportAsset(temppath, ImportAssetOptions.ForceUpdate);
            sourceTex = AssetDatabase.LoadAssetAtPath<Texture2D>(temppath);

            // cleanup
            File.Delete(temppath);
            File.Delete(temppath + ".meta");
        }

        // scale if required
        Texture2D destTex = sourceTex;
        if (scaleFactor != 1)
        {
            destTex = ScaleTexture2D(sourceTex, scaleFactor);
        }

        // save the result
        byte[] destData = destTex.EncodeToPNG();
        File.WriteAllBytes(destination, destData);

        // cleanup
        if (destTex != sourceTex)
        {
            Object.DestroyImmediate(destTex);
        }
        Object.DestroyImmediate(sourceTex);
    }

    private static Texture2D ScaleTexture2D(Texture2D origTex, int scaleFactor)
    {
        // Create a new target texture at the desired size and copy pixels
        int newWidth = origTex.width / scaleFactor;
        int newHeight = origTex.height / scaleFactor;
        var newTex = new Texture2D(newWidth, newHeight, TextureFormat.ARGB32, false);
        var newPix = new Color[newTex.width * newTex.height];

        // Copy pixels
        for (var y = 0; y < newTex.height; y++)
        {
            for (var x = 0; x < newTex.width; x++)
            {
                var xFrac = x * 1.0f / (newTex.width - 1);
                var yFrac = y * 1.0f / (newTex.height - 1);

                newPix[y * newTex.width + x] = origTex.GetPixelBilinear(xFrac, yFrac);
            }
        }

        newTex.SetPixels(newPix);
        newTex.Apply();

        return newTex;
    }

    /// <summary>
    /// Recursively delete files and directories, even if the files are readonly.
    /// Standard C# delete fails on read only files.
    /// </summary>
    private void RecursiveDelete(string path)
    {
        foreach (string directory in Directory.GetDirectories(path))
        {
            RecursiveDelete(directory);
            Directory.Delete(directory);
        }

        foreach (string file in Directory.GetFiles(path))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }
    }


    private void DisplayProgressBar(string title, string infoPrefix, float startProgress = 0, float endProgress = 1)
    {
        m_ProgressBarTitle = title;
        m_ProgressBarInfo = infoPrefix;
        m_ProgressBarStart = startProgress;
        m_ProgressBarEnd = endProgress;

        EditorUtility.DisplayProgressBar(title, infoPrefix, startProgress);
    }

    private void ClearProgressBar()
    {
        m_ProgressBarTitle = "";
        EditorUtility.ClearProgressBar();
    }

    private bool IsInputValid()
    {
        bool valid = false;
        if (m_InputItems.Count > 0)
        {
            foreach (InputListItem item in m_InputItems)
            {
                if (!string.IsNullOrEmpty(item.AssetPath))
                {
                    valid = true;
                    break;
                }
            }
        }

        return valid;
    }

    private bool IsProjectPathValid()
    {
        bool valid = false;
        if (!string.IsNullOrEmpty(NewProjectPath))
        {
            if (Directory.Exists(NewProjectPath))
            {
                valid = true;
            }
            else if (Directory.Exists(Path.GetDirectoryName(NewProjectPath)))
            {
                valid = true;
            }
        }

        return valid;
    }

    private void CopyItemsToSettings(ref ReproProjectSettings.InputItem[] settingsItems, List<InputListItem> sourceItems)
    {
        settingsItems = new ReproProjectSettings.InputItem[sourceItems.Count];
        for (int i = 0; i < sourceItems.Count; i++)
        {
            settingsItems[i].AssetType = sourceItems[i].AssetType;
            settingsItems[i].AssetPath = sourceItems[i].AssetPath;
        }
    }

    private void CopyItemsFromSettings(List<InputListItem> targetItems, ReproProjectSettings.InputItem[] settingsItems)
    {
        if (settingsItems == null)
        {
            return;
        }

        targetItems.Clear();
        for (int i = 0; i < settingsItems.Length; i++)
        {
            ReproProjectSettings.InputItem sourceItem = settingsItems[i];
            InputListItem item = new InputListItem() { AssetType = sourceItem.AssetType, AssetPath = sourceItem.AssetPath, AssetObject = null };
            if (item.AssetType != ReproProjectAssetType.Wildcard && !string.IsNullOrEmpty(item.AssetPath))
            {
                item.AssetObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.AssetPath);
            }
            targetItems.Add(item);
        }
    }

    private void InitializeItems(List<InputListItem> items, string[] paths)
    {
        items.Clear();
        foreach (string path in paths)
        {
            InputListItem item = new InputListItem() { AssetType = ReproProjectAssetType.Wildcard, AssetPath = path, AssetObject = null };
            items.Add(item);
        }
    }

    private bool IsInInputs(string input)
    {
        foreach (InputListItem item in m_InputItems)
        {
            if (item.AssetPath == input)
            {
                return true;
            }
        }
        return false;
    }
}

