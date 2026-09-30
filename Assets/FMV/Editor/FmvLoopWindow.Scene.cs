using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace FMV.Editor
{
    public sealed partial class FmvLoopWindow
    {
        void UseSelectedCharacter()
        {
            var go = Selection.activeGameObject; var raw = go ? go.GetComponent<RawImage>() : null;
            if (!raw) { status.text = "请在 Hierarchy 中选一个带 RawImage 的角色。"; return; }
            SelectTarget(raw);
        }
        public void CreateCharacter()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var selected = Selection.activeGameObject;
            var canvas = selected ? selected.GetComponentInParent<Canvas>() : null;
            if (!canvas || EditorUtility.IsPersistent(canvas)) { status.text = "请先在 Hierarchy 中选中 Canvas 或它的子物体。"; return; }
            var go = new GameObject(recipe ? recipe.name : "FMV Character", typeof(RectTransform), typeof(RawImage));
            Undo.RegisterCreatedObjectUndo(go, "Create FMV character"); go.transform.SetParent(selected.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.pivot = new Vector2(0.5f, 0.5f);
            float aspect = recipe && recipe.source ? (float)recipe.source.width / recipe.source.height : 16f / 9;
            rect.sizeDelta = new Vector2(600 * aspect, 600); rect.anchoredPosition = Vector2.zero;
            var raw = go.GetComponent<RawImage>(); raw.raycastTarget = false;
            SelectTarget(raw); Selection.activeGameObject = go; EditorSceneManager.MarkSceneDirty(go.scene);
            status.text = "角色已创建。生成素材后，点击“把生成素材配置到目标角色”。";
        }
        static T Component<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing ? existing : Undo.AddComponent<T>(go);
        }
        static RenderTexture TargetTexture(VideoPlayer player, VideoClip clip, string role)
        {
            var rt = player.targetTexture;
            bool shared = rt && Object.FindObjectsByType<VideoPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(p => p != player && p.targetTexture == rt);
            if (!rt || shared || !AssetDatabase.Contains(rt))
            {
                EnsureFolder("Assets/FMV/Players");
                rt = new RenderTexture((int)clip.width, (int)clip.height, 0, RenderTextureFormat.ARGB32) { name = role, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                AssetDatabase.CreateAsset(rt, AssetDatabase.GenerateUniqueAssetPath("Assets/FMV/Players/" + role + ".renderTexture"));
            }
            else if (rt.width != clip.width || rt.height != clip.height) { rt.Release(); rt.width = (int)clip.width; rt.height = (int)clip.height; EditorUtility.SetDirty(rt); }
            return rt;
        }
        public void ConfigureScene()
        {
            if (process != null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!GeneratedCurrent()) { status.text = "请先生成当前切分设置的素材。"; return; }
            if (!targetImage || EditorUtility.IsPersistent(targetImage) || !targetImage.gameObject.scene.IsValid()) { status.text = "请选择当前场景中的 RawImage 角色。"; return; }
            int undoGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Configure FMV character");
            var go = targetImage.gameObject;
            var loop = Component<FmvCharacterLoop>(go); var main = Component<VideoPlayer>(go); var audio = Component<AudioSource>(go);
            var data = new SerializedObject(loop);
            var idle = data.FindProperty("idlePlayer").objectReferenceValue as VideoPlayer;
            if (!idle || idle == main)
            {
                var child = new GameObject("IdlePlayer"); Undo.RegisterCreatedObjectUndo(child, "Create FMV idle player"); child.transform.SetParent(go.transform, false);
                idle = Undo.AddComponent<VideoPlayer>(child);
            }
            var idleAudioSource = Component<AudioSource>(idle.gameObject);
            Undo.RecordObjects(new Object[] { loop, main, idle, audio, idleAudioSource, targetImage }, "Configure FMV character");
            main.clip = recipe.introClip; idle.clip = recipe.idleClip;
            foreach (var player in new[] { main, idle })
            {
                player.source = VideoSource.VideoClip; player.playOnAwake = false; player.renderMode = VideoRenderMode.RenderTexture;
                player.aspectRatio = VideoAspectRatio.FitInside; player.waitForFirstFrame = true;
            }
            main.targetTexture = TargetTexture(main, main.clip, "Intro"); idle.targetTexture = TargetTexture(idle, idle.clip, "Idle");
            main.isLooping = false; idle.isLooping = true;
            foreach (var source in new[] { audio, idleAudioSource }) { source.playOnAwake = false; source.loop = false; source.spatialBlend = 0; }
            if ((recipe.keepIntroAudio && main.clip.audioTrackCount > 0 || recipe.keepIdleAudio && idle.clip.audioTrackCount > 0) &&
                !Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Any(listener => listener.isActiveAndEnabled))
            {
                var listener = new GameObject("FMV Audio Listener", typeof(AudioListener));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(listener, go.scene);
                Undo.RegisterCreatedObjectUndo(listener, "Create FMV audio listener");
            }
            main.audioOutputMode = recipe.keepIntroAudio && main.clip.audioTrackCount > 0 ? VideoAudioOutputMode.AudioSource : VideoAudioOutputMode.None;
            idle.audioOutputMode = recipe.keepIdleAudio && idle.clip.audioTrackCount > 0 ? VideoAudioOutputMode.AudioSource : VideoAudioOutputMode.None;
            if (main.audioOutputMode == VideoAudioOutputMode.AudioSource) { main.controlledAudioTrackCount = 1; main.SetTargetAudioSource(0, audio); }
            if (idle.audioOutputMode == VideoAudioOutputMode.AudioSource) { idle.controlledAudioTrackCount = 1; idle.SetTargetAudioSource(0, idleAudioSource); }
            if (recipe.greenScreen)
            {
                var shader = Shader.Find("FMV/Character Chroma Key"); if (!shader) throw new Exception("找不到 FMV 绿幕 Shader。");
                EnsureFolder(recipe.outputFolder); string path = recipe.outputFolder + "/ChromaKey.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                Undo.RecordObject(material, "Adjust FMV chroma key"); material.SetFloat("_Threshold", recipe.threshold); material.SetFloat("_Softness", recipe.softness); material.SetFloat("_Despill", recipe.despill);
                EditorUtility.SetDirty(material); targetImage.material = material;
            }
            else targetImage.material = null;
            targetImage.texture = recipe.poster;
            data.Update();
            data.FindProperty("player").objectReferenceValue = main; data.FindProperty("idlePlayer").objectReferenceValue = idle;
            data.FindProperty("audioSource").objectReferenceValue = audio; data.FindProperty("idleAudioSource").objectReferenceValue = idleAudioSource;
            data.FindProperty("character").objectReferenceValue = targetImage; data.FindProperty("poster").objectReferenceValue = recipe.poster; data.FindProperty("recipe").objectReferenceValue = recipe;
            data.ApplyModifiedProperties();
            foreach (var item in new Object[] { loop, main, idle, audio, idleAudioSource, targetImage })
            {
                EditorUtility.SetDirty(item);
                if (PrefabUtility.IsPartOfPrefabInstance(item)) PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            }
            Undo.CollapseUndoOperations(undoGroup); EditorSceneManager.MarkSceneDirty(go.scene); AssetDatabase.SaveAssets(); Selection.activeGameObject = go;
            status.text = "已配置角色“" + go.name + "”。位置与大小保留；场景已标记修改，请 Ctrl+S 保存，然后 Play 验证。";
        }
    }
}
