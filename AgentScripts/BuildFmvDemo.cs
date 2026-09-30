using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Video;

public static class BuildFmvDemo
{
    const string Folder = "Assets/FMV";
    public static string Main()
    {
        if (EditorApplication.isPlaying) throw new Exception("Exit Play mode before building.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/FMVDemo.unity"))
            throw new Exception("FMVDemo already exists. Edit it in place.");
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (previous.isDirty) EditorSceneManager.SaveScene(previous);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        foreach (var path in new[] { "Assets/Resource/images/BG.png", "Assets/Resource/images/UI.png", Folder+"/Video16Poster.png" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.transform.position = new Vector3(0,0,-10);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100;

        var canvasObject = new GameObject("FMVCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 10;
        var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2736,1536);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;

        var composition = Rect("Composition_16x9", canvasObject.transform,0,0,2736,1536);
        composition.anchorMin = composition.anchorMax = new Vector2(.5f,.5f);
        composition.pivot = new Vector2(.5f,.5f);
        composition.anchoredPosition = Vector2.zero;
        composition.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var background = Rect("ForestBackground",composition,0,0,2736,1536).gameObject.AddComponent<UnityEngine.UI.RawImage>();
        background.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resource/images/BG.png");
        background.raycastTarget=false;

        var renderTexture = new RenderTexture(1920,1080,0,RenderTextureFormat.ARGB32) {name="CharacterVideo",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        AssetDatabase.CreateAsset(renderTexture,Folder+"/CharacterVideo.renderTexture");
        var material=new Material(Shader.Find("FMV/Character Chroma Key")) {name="CharacterChromaKey"};
        AssetDatabase.CreateAsset(material,Folder+"/CharacterChromaKey.mat");
        var character = Rect("CharacterLoop",composition,1090,165,2000,1125).gameObject.AddComponent<UnityEngine.UI.RawImage>();
        character.raycastTarget=false;
        character.material=material;
        var poster=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Video16Poster.png");
        character.texture=poster;
        var player=character.gameObject.AddComponent<VideoPlayer>();
        player.source=VideoSource.VideoClip;
        player.clip=AssetDatabase.LoadAssetAtPath<VideoClip>(Folder+"/Video16Intro.mp4");
        player.renderMode=VideoRenderMode.RenderTexture;
        player.targetTexture=renderTexture;
        player.aspectRatio=VideoAspectRatio.NoScaling;
        player.isLooping=false;
        player.playOnAwake=false;
        var audioSource=character.gameObject.AddComponent<AudioSource>();
        player.audioOutputMode=VideoAudioOutputMode.AudioSource;
        player.controlledAudioTrackCount=1;
        player.SetTargetAudioSource(0,audioSource);
        player.EnableAudioTrack(0,true);
        var idleObject=new GameObject("IdlePlayer",typeof(VideoPlayer));
        idleObject.transform.SetParent(character.transform,false);
        var idle=idleObject.GetComponent<VideoPlayer>();
        idle.clip=AssetDatabase.LoadAssetAtPath<VideoClip>(Folder+"/Video16Idle.mp4");
        var idleTexture=new RenderTexture(1920,1080,0,RenderTextureFormat.ARGB32) {name="CharacterIdle",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        AssetDatabase.CreateAsset(idleTexture,Folder+"/CharacterIdle.renderTexture");
        idle.targetTexture=idleTexture;
        idle.renderMode=VideoRenderMode.RenderTexture;
        idle.isLooping=true;
        idle.playOnAwake=false;
        idle.audioOutputMode=VideoAudioOutputMode.None;
        var loop=character.gameObject.AddComponent<FMV.FmvCharacterLoop>();
        var serialized=new SerializedObject(loop);
        serialized.FindProperty("player").objectReferenceValue=player;
        serialized.FindProperty("idlePlayer").objectReferenceValue=idle;
        serialized.FindProperty("audioSource").objectReferenceValue=audioSource;
        serialized.FindProperty("character").objectReferenceValue=character;
        serialized.FindProperty("poster").objectReferenceValue=poster;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        var hud=Rect("Interface",composition,0,0,2736,1536);
        var sprites=AssetDatabase.LoadAllAssetsAtPath("Assets/Resource/images/UI.png").OfType<Sprite>().OrderBy(s=>s.name).ToArray();
        AddImage("StatusPanel",hud,sprites[0],10,16,1250,403);
        AddImage("CouncilCountdown",hud,sprites[5],10,408,451,260);
        AddImage("Information",hud,sprites[1],1977,38,184,221);
        AddImage("Functions",hud,sprites[2],2165,38,184,221);
        AddImage("Settings",hud,sprites[3],2353,38,184,221);
        AddImage("Help",hud,sprites[4],2541,38,184,221);
        AddImage("DialoguePanel",hud,sprites[6],1368,1075,1355,405);

        new GameObject("EventSystem",typeof(UnityEngine.EventSystems.EventSystem),typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/FMVDemo.unity");
        var buildScenes=EditorBuildSettings.scenes.ToList();
        buildScenes.RemoveAll(s=>s.path==scene.path);
        buildScenes.Insert(0,new EditorBuildSettingsScene(scene.path,true));
        EditorBuildSettings.scenes=buildScenes.ToArray();
        PlayerSettings.runInBackground=true;
        AssetDatabase.SaveAssets();
        Selection.activeGameObject=canvasObject;
        return "Created " + scene.path + " | UI slices=" + sprites.Length + " | video="+player.clip.width+"x"+player.clip.height;
    }
    static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
    {
        var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
        r.SetParent(parent,false);
        r.anchorMin=r.anchorMax=new Vector2(0,1);
        r.pivot=new Vector2(0,1);
        r.anchoredPosition=new Vector2(x,-y);
        r.sizeDelta=new Vector2(w,h);
        return r;
    }
    static void AddImage(string name,Transform parent,Sprite sprite,float x,float y,float w,float h)
    {
        var image=Rect(name,parent,x,y,w,h).gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite=sprite;
        image.raycastTarget=false;
    }
}
