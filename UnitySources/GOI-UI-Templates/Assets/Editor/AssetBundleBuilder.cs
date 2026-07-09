using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;

public class AssetBundleBuilder : Editor
{
    [MenuItem("Assets/Build to Level Asset Bundle")]
    static void BuildLevelAssetBundle()
    {
        BuildPipeline.BuildAssetBundles("Assets/Bundles", BuildAssetBundleOptions.ChunkBasedCompression, EditorUserBuildSettings.activeBuildTarget);
    }
}
