using UnityEngine;
using UnityEngine.Video;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class UseVideo16
{
    public static string Main()
    {
        if (EditorApplication.isPlaying) throw new System.Exception("Exit Play Mode first.");
        var loop = Object.FindFirstObjectByType<FMV.FmvCharacterLoop>();
        if (!loop) throw new System.Exception("FMV character not found in active scene.");
        var player = loop.GetComponent<VideoPlayer>();
        var image = loop.GetComponent<UnityEngine.UI.RawImage>();
        var audio = loop.GetComponent<AudioSource>();
        var child = loop.transform.Find("IdlePlayer");
        if (!child)
        {
            child = new GameObject("IdlePlayer", typeof(VideoPlayer)).transform;
            child.SetParent(loop.transform, false);
        }
        var idle = child.GetComponent<VideoPlayer>();
        player.clip = AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/FMV/Video16Intro.mp4");
        idle.clip = AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/FMV/Video16Idle.mp4");
        if (!player.clip || !idle.clip) throw new System.Exception("Import both Video16 segments first.");
        var rt = player.targetTexture;
        rt.Release(); rt.width = 1920; rt.height = 1080;
        EditorUtility.SetDirty(rt);
        const string idlePath = "Assets/FMV/CharacterIdle.renderTexture";
        var idleTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>(idlePath);
        if (!idleTexture)
        {
            idleTexture = new RenderTexture(1920,1080,0,RenderTextureFormat.ARGB32);
            idleTexture.name = "CharacterIdle";
            idleTexture.filterMode = FilterMode.Bilinear;
            idleTexture.wrapMode = TextureWrapMode.Clamp;
            AssetDatabase.CreateAsset(idleTexture, idlePath);
        }
        idle.source = VideoSource.VideoClip;
        idle.renderMode = VideoRenderMode.RenderTexture;
        idle.targetTexture = idleTexture;
        idle.aspectRatio = VideoAspectRatio.NoScaling;
        idle.isLooping = true;
        idle.playOnAwake = false;
        idle.audioOutputMode = VideoAudioOutputMode.None;
        player.isLooping = false;
        player.playOnAwake = false;
        player.audioOutputMode = VideoAudioOutputMode.AudioSource;
        player.SetTargetAudioSource(0, audio);
        var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/FMV/Video16Poster.png");
        importer.maxTextureSize = 2048;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        var poster = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/FMV/Video16Poster.png");
        image.texture = poster;
        var serialized = new SerializedObject(loop);
        serialized.FindProperty("idlePlayer").objectReferenceValue = idle;
        serialized.FindProperty("poster").objectReferenceValue = poster;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(loop.gameObject.scene);
        EditorSceneManager.SaveScene(loop.gameObject.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = loop.gameObject;
        return "Intro="+player.clip.length+"s; Idle="+idle.clip.length+"s; Intro audio="+player.clip.audioTrackCount;
    }
}
