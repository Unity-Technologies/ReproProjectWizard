using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public class ProjectStats
{
    /// <summary>
    /// Basic data about a file in the project
    /// </summary>
    [Serializable]
    public class ItemBase
    {
        public string FilePath;
        public long FileSize;

        public ItemBase(string path, UnityEngine.Object obj)
        {
            FilePath = path;
            FileInfo fi = new FileInfo(path);
            FileSize = fi.Length;
        }

        public ItemBase()
        {
            FilePath = "";
            FileSize = 0;
        }
    }

    /// <summary>
    /// Data about a Texture2D in the project
    /// </summary>
    [Serializable]
    public class ItemTexture2D : ItemBase
    {
        public int Width;
        public int Height;
        public TextureFormat Format;
        public TextureDimension Dimension;

        public ItemTexture2D(string path, UnityEngine.Object obj) : base(path, obj)
        {
            Texture2D texture = (Texture2D)obj;
            Width = texture.width;
            Height = texture.height;
            Format = texture.format;
            Dimension = texture.dimension;
        }
    }

    /// <summary>
    /// Data about a material in the project including the shader and textures it references
    /// </summary>
    [Serializable]
    public class ItemMaterial : ItemBase
    {
        public string ShaderPath;
        public List<string> TexturePaths = new List<string>();

        public ItemMaterial(string path, UnityEngine.Object obj) : base(path, obj)
        {
            Material material = (Material)obj;
            ShaderPath = AssetDatabase.GetAssetPath(material.shader);
            HashSet<Texture> textures = new HashSet<Texture>();
            foreach(int id in material.GetTexturePropertyNameIDs())
            {
                Texture texture = material.GetTexture(id);
                if (texture != null && !textures.Contains(texture))
                {
                    textures.Add(texture);
                    TexturePaths.Add(AssetDatabase.GetAssetPath(texture));
                }
            }
        }
    }

    /// <summary>
    /// Data about an fbx in the project, including meshes and animations
    /// </summary>
    [Serializable]
    public class ItemFBX : ItemBase
    {
        public int MeshCount = 0;
        public List<int> SubMeshCounts = new List<int>();
        public List<int> Vertices = new List<int>();
        public List<int> Triangles = new List<int>();
        public int AnimationCount = 0;
        public List<string> AnimationNames = new List<string>();
        public List<int> AnimationFrames = new List<int>();
        public List<float> AnimationLengths = new List<float>();

        public ItemFBX(string path, UnityEngine.Object obj) : base(path, obj)
        {
            // Iterate all the objects in this asset
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach(UnityEngine.Object asset in assets)
            {
                Mesh mesh = asset as Mesh;
                if (mesh != null)
                {
                    SubMeshCounts.Add(mesh.subMeshCount);
                    Vertices.Add(mesh.vertexCount);
                    Triangles.Add(mesh.triangles.Length / 3);
                    MeshCount++;
                }
                AnimationClip clip = asset as AnimationClip;
                if (clip != null)
                {
                    AnimationNames.Add(clip.name);
                    AnimationFrames.Add((int)(clip.frameRate * clip.length));
                    AnimationLengths.Add(clip.length);
                    AnimationCount++;
                }
            }
        }
    }

    /// <summary>
    /// Data about an animation clip in the project
    /// </summary>
    [Serializable]
    public class ItemAnimationClip : ItemBase
    {
        public int AnimationFrames;
        public float AnimationLength;

        public ItemAnimationClip(string path, UnityEngine.Object obj) : base(path, obj)
        {
            AnimationClip clip = (AnimationClip)obj;
            AnimationFrames = (int)(clip.frameRate * clip.length);
            AnimationLength = clip.length;
        }
    }

    /// <summary>
    /// Data about an audio clip in the project, including length and compression
    /// </summary>
    [Serializable]
    public class ItemAudioClip : ItemBase
    {
        public float Length;
        public int Channels;
        public int Frequency;
        public int Samples;
        public AudioCompressionFormat CompressionFormat;
        public float CompressionQuality;


        public ItemAudioClip(string path, UnityEngine.Object obj) : base(path, obj)
        {
            AudioClip audioClip = (AudioClip)obj;
            Frequency = audioClip.frequency;
            Channels = audioClip.channels;
            Length = audioClip.length;
            Samples = audioClip.samples;

            AudioImporter importer = (AudioImporter)AssetImporter.GetAtPath(path);
            string platform = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget).ToString();
            AudioImporterSampleSettings settings = importer.GetOverrideSampleSettings(platform);
            CompressionFormat = settings.compressionFormat;
            CompressionQuality = settings.quality;
        }
    }

    /// <summary>
    /// Data about a prefab in the project, including which meshes and materials it references
    /// </summary>
    [Serializable]
    public class ItemPrefab : ItemBase
    {
        public List<string> MeshPaths = new List<string>();
        public List<string> MaterialPaths = new List<string>();

        public ItemPrefab(string path, UnityEngine.Object obj) : base(path, obj)
        {
            GameObject go = (GameObject)obj;
            HashSet<string> meshes = new HashSet<string>();
            HashSet<Material> materials = new HashSet<Material>();

            // find regular render components
            foreach(Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    string meshPath = AssetDatabase.GetAssetPath(mf.sharedMesh);
                    if (!meshes.Contains(meshPath))
                    {
                        MeshPaths.Add(meshPath);
                        meshes.Add(meshPath);
                    }
                }
                AddMaterials(materials, r.sharedMaterials);
            }

            // skinned mesh renderers
            foreach(SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh != null)
                {
                    string meshPath = AssetDatabase.GetAssetPath(smr.sharedMesh);
                    if (!meshes.Contains(meshPath))
                    {
                        MeshPaths.Add(meshPath);
                        meshes.Add(meshPath);
                    }
                }
                AddMaterials(materials, smr.sharedMaterials);
            }

            // particle renderers
            foreach(ParticleSystemRenderer psr in go.GetComponentsInChildren<ParticleSystemRenderer>())
            {
                AddMaterials(materials, psr.sharedMaterials);
            }
        }

        private void AddMaterials(HashSet<Material> materials, Material[] newMaterials)
        {
            foreach (Material m in newMaterials)
            {
                if (m != null && !materials.Contains(m))
                {
                    MaterialPaths.Add(AssetDatabase.GetAssetPath(m));
                    materials.Add(m);
                }
            }
        }
    }

    /// <summary>
    /// The full report on all items/files in the project.
    /// Any filetypes that aren't specifically recognized are added to the Files list with path and filesize recorded.
    /// </summary>
    [Serializable]
    public class Report
    {
        public Dictionary<System.Type, List<ItemBase>> Items;

        public List<ItemTexture2D> Texture2Ds = new List<ItemTexture2D>();
        public List<ItemMaterial> Materials = new List<ItemMaterial>();
        public List<ItemFBX> FBXs = new List<ItemFBX>();
        public List<ItemAnimationClip> AnimationClips = new List<ItemAnimationClip>();
        public List<ItemAudioClip> AudioClips = new List<ItemAudioClip>();
        public List<ItemPrefab> Prefabs = new List<ItemPrefab>();
        public List<ItemBase> Scenes = new List<ItemBase>();
        public List<ItemBase> Files = new List<ItemBase>();

        public Report()
        { }

        public void AddItems(string path)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null)
            {
                System.Type assetType = asset.GetType();
                System.Type itemType = typeof(ItemBase);

                string extension = Path.GetExtension(path).ToLower();
                if (assetType == typeof(Texture2D))
                {
                    Texture2Ds.Add(new ItemTexture2D(path, asset));
                    Resources.UnloadAsset(asset);
                }
                else if (extension == ".mat")
                {
                    Materials.Add(new ItemMaterial(path, asset));
                    Resources.UnloadAsset(asset);
                }
                else if (extension == ".fbx")
                {
                    FBXs.Add(new ItemFBX(path, asset));
                }
                else if (assetType == typeof(AnimationClip))
                {
                    AnimationClips.Add(new ItemAnimationClip(path, asset));
                    Resources.UnloadAsset(asset);
                }
                else if (assetType == typeof(AudioClip))
                {
                    AudioClips.Add(new ItemAudioClip(path, asset));
                    Resources.UnloadAsset(asset);
                }
                else if (extension == ".prefab")
                {
                    Prefabs.Add(new ItemPrefab(path, asset));
                }
                else if (extension == ".unity")
                {
                    Scenes.Add(new ItemBase(path, asset));
                }
                else
                {
                    Files.Add(new ItemBase(path, asset));
                }
            }
        }

    }

    /// <summary>
    /// Convert all path separators to match standard Unity format
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    private static string NormalizePath(string path)
    {
        path = path.Replace('\\', '/').Replace("//", "/");
        return path;
    }

    /// <summary>
    /// Add files to be copied to the new project.
    /// </summary>
    /// <param name="results">Where to save the resulting files</param>
    /// <param name="projectPath">Path to the root of the project</param>
    /// <param name="path">Current path in the project to add files from</param>
    /// <param name="count">Number of files added so far</param>
    private static int AddFiles(Report results, string projectPath, string path, int count)
    {
        // Add all the files in this Directory
        string[] foundFiles = Directory.GetFiles(path);
        foreach (string file in foundFiles)
        {
            if (!file.EndsWith("meta"))
            {
                string trimmedFile = file.Replace(projectPath, "").TrimStart(new char[] { '/', '\\' });
                results.AddItems(NormalizePath(trimmedFile));
                EditorUtility.DisplayProgressBar("Collecting Project Statistics", trimmedFile, 0);
                count++;
            }

            if ((count % 1000) == 0)
            {
                Resources.UnloadUnusedAssets();
            }
        }

        // Now Process sub directories
        string[] foundDirs = Directory.GetDirectories(path);
        foreach (string foundDir in foundDirs)
        {
            count = AddFiles(results, projectPath, foundDir, count);
        }

        return count;
    }

    /// <summary>
    /// Scan the whole project and create a report with information on all the files in the project.
    /// </summary>
    /// <returns></returns>
    public static Report CreateReport()
    {
        Report results = new Report();

        string projectPath = Path.GetDirectoryName(Application.dataPath); // in editor Application.dataPath is <path to project folder>/Assets
        try
        {
            AddFiles(results, projectPath, Path.Combine(projectPath, "Assets"), 0);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        return results;
    }

    public static void SaveReport(Report report, string path)
    {
        string output = JsonUtility.ToJson(report, true);
        File.WriteAllText(path, output);
    }

    public static Report LoadReport(string path)
    {
        string json = File.ReadAllText(path);
        Report result = JsonUtility.FromJson<Report>(json);
        return result;
    }

    [MenuItem("Window/Project Stats/Save")]
    static void SaveStats()
    {
        string savePath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "ProjectStats.json");

        Report report = CreateReport();
        SaveReport(report, savePath);
    }
    [MenuItem("Window/Project Stats/Load")]
    static void LoadStats()
    {
        string savePath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "ProjectStats.json");

        Report report = LoadReport(savePath);

        Debug.LogFormat("Loaded Report {0}", savePath);
    }
}
