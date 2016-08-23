using UnityEngine;

public enum ReproProjectAssetType
{
    Wildcard,   // Text based wildcard
    Scene,      // Scene files only
    Prefab,     // Prefabs only
    Asset,      // Any asset type
}

/// <summary>
/// Store the current state of the Repro Project Wizard.
/// </summary>
public class ReproProjectSettings : ScriptableObject
{
    [System.Serializable]
    public struct InputItem
    {
        public ReproProjectAssetType AssetType;
        public string AssetPath;
    }

    public string ProjectName;
    public string ProjectPath;
    public bool OpenProject;
    public int TextureScale;
    public InputItem[] InputFiles;
    public InputItem[] ProjectFiles;
}

