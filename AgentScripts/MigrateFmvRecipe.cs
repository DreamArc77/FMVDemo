using UnityEngine;
using UnityEngine.Video;
using UnityEditor;
using FMV;
using FMV.Editor;

public static class MigrateFmvRecipe
{
    public static string Main()
    {
        FmvLoopWindow.Open();
        var window = EditorWindow.GetWindow<FmvLoopWindow>();
        const string path = "Assets/FMV/Sequences/视频16.asset";
        var recipe = AssetDatabase.LoadAssetAtPath<FmvVideoRecipe>(path);
        if (!recipe)
        {
            recipe = window.CreateAt(path);
            recipe.source = AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Resource/clips/视频 16.mp4");
            recipe.introStart = 0;
            recipe.introEnd = recipe.idleStart = FmvLoopToolSettings.instance.start;
            recipe.idleEnd = FmvLoopToolSettings.instance.end;
            var current = Object.FindFirstObjectByType<FmvCharacterLoop>();
            var material = current ? current.GetComponent<UnityEngine.UI.RawImage>().material : null;
            if (material && material.HasProperty("_Threshold"))
            {
                recipe.threshold = material.GetFloat("_Threshold");
                recipe.softness = material.GetFloat("_Softness");
                recipe.despill = material.GetFloat("_Despill");
            }
            EditorUtility.SetDirty(recipe); AssetDatabase.SaveAssets();
        }
        window.SelectRecipe(recipe);
        var loop = Object.FindFirstObjectByType<FmvCharacterLoop>();
        window.SelectTarget(loop ? loop.GetComponent<UnityEngine.UI.RawImage>() : null);
        window.GenerateAssets();
        return "Migrated prior range to " + path + "; exporting.";
    }
}
