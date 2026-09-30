using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using RawImage = UnityEngine.UI.RawImage;
using UnityEngine.UIElements;
using UnityEngine.Video;
using Object = UnityEngine.Object;
using Image = UnityEngine.UIElements.Image;

namespace FMV.Editor
{
    [FilePath("ProjectSettings/FmvLoopTool.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class FmvLoopToolSettings : ScriptableSingleton<FmvLoopToolSettings>
    {
        public double start = 6, end = 10;
        public string lastRecipeGuid;
        public void Store() => Save(true);
    }

    public sealed partial class FmvLoopWindow : EditorWindow
    {
        [SerializeField] FmvVideoRecipe recipe;
        [SerializeField] RawImage targetImage;
        VideoPlayer preview;
        RenderTexture texture;
        Image image;
        Slider timeline;
        DoubleField currentTime, introStart, introEnd, idleStart, idleEnd;
        ObjectField recipeField, sourceField, targetField;
        IntegerField frameRate;
        Toggle introAudio, idleAudio, greenScreen;
        Slider threshold, softness, despill;
        Label positionLabel, status, outputInfo;
        VisualElement editing;
        Button generate, bind, cancel, import;
        ProgressBar progress;
        bool audition = true, looping, seeking, pendingSeek, resumeAfterSeek;
        double pendingTime, previewStart, previewEnd;
        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        double Rate => recipe && recipe.source ? Math.Max(1, recipe.source.frameRate) : 24;
        double Duration => recipe && recipe.source ? recipe.source.length : 10;
        public string Status => status?.text;
        public double PreviewTime => preview ? preview.time : 0;
        public bool PreviewReady => preview && preview.isPrepared;
        public bool IsExporting => process != null;
        public FmvVideoRecipe ActiveRecipe => recipe;
        public RawImage TargetImage => targetImage;

        [MenuItem("Tools/FMV/素材制作工作台")]
        [MenuItem("Tools/FMV/待机循环调节器")]
        public static void Open()
        {
            var window = GetWindow<FmvLoopWindow>("FMV 素材制作");
            window.minSize = new Vector2(780, 640); window.Show();
        }
        void OnEnable()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            Undo.undoRedoPerformed += UndoChanged;
            if (!recipe && !string.IsNullOrEmpty(FmvLoopToolSettings.instance.lastRecipeGuid))
                recipe = AssetDatabase.LoadAssetAtPath<FmvVideoRecipe>(AssetDatabase.GUIDToAssetPath(FmvLoopToolSettings.instance.lastRecipeGuid));
        }
        void UndoChanged() { if (process == null && recipeField != null) { RefreshFields(); PreparePreview(); } }
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/FMV/Editor/FmvLoopWindow.uss");
            if (sheet) rootVisualElement.styleSheets.Add(sheet);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("workspace"); rootVisualElement.Add(scroll);
            scroll.Add(new Label("FMV 素材制作工作台") { name = "heading" });
            Help(scroll, "每份配置对应一组素材。选择视频 → 调整开场 / 待机区间 → 生成素材 → 配置场景角色。原视频保持不变。");
            editing = new VisualElement(); scroll.Add(editing);
            recipeField = new ObjectField("制作配置") { objectType = typeof(FmvVideoRecipe), allowSceneObjects = false };
            recipeField.RegisterValueChangedCallback(e => SelectRecipe(e.newValue as FmvVideoRecipe)); editing.Add(recipeField);
            Row(editing, Button("新建配置…", () => CreateRecipe(false)), Button("另存为新配置…", () => CreateRecipe(true)),
                Button("读取选中的配置", () => SelectRecipe(Selection.activeObject as FmvVideoRecipe)));
            sourceField = new ObjectField("原视频") { objectType = typeof(VideoClip), allowSceneObjects = false };
            sourceField.RegisterValueChangedCallback(e => ChangeSource(e.newValue as VideoClip)); editing.Add(sourceField);
            import = Button("导入视频文件并转换为兼容格式…", ImportVideo); editing.Add(import);
            var columns = new VisualElement(); columns.AddToClassList("columns"); editing.Add(columns);
            var left = new VisualElement(); left.AddToClassList("preview-column"); columns.Add(left);
            var right = new VisualElement(); right.AddToClassList("range-column"); columns.Add(right);
            Section(left, "1  找到合适的画面");
            image = new Image { scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("preview"); left.Add(image);
            timeline = new Slider("时间轴", 0, 10); timeline.RegisterValueChangedCallback(e => Seek(e.newValue)); left.Add(timeline);
            currentTime = new DoubleField("定位到（秒）") { formatString = "0.000" };
            currentTime.RegisterValueChangedCallback(e => Seek(e.newValue)); left.Add(currentTime);
            positionLabel = new Label(); positionLabel.AddToClassList("help"); left.Add(positionLabel);
            Row(left, Button("上一帧", () => Seek(PreviewTime - 1 / Rate)), Button("下一帧", () => Seek(PreviewTime + 1 / Rate)), Button("暂停", Pause));
            Row(left, Button("预览完整视频", () => PlayRange(0, Duration, false)), Button("预览开场", () => PlayPreview(false)));
            Row(left, Button("循环预览待机", () => PlayPreview(true)), Button("看待机起点", () => Seek(recipe ? recipe.idleStart : 0)),
                Button("看待机末帧", () => Seek(recipe ? recipe.idleEnd - 1 / Rate : 0)));
            var sound = new Toggle("试听原视频声音") { value = audition };
            sound.RegisterValueChangedCallback(e => { audition = e.newValue; UpdateAudio(); }); left.Add(sound);
            Help(left, "预览显示原始画面；跳转接缝可能短暂停顿，生成后的待机片段使用原生循环。终点不包含该帧。");
            Section(right, "2  切分与声音");
            introStart = TimeField(right, "开场起点（秒）", v => recipe.introStart = v);
            introEnd = TimeField(right, "开场终点（秒）", v => recipe.introEnd = v);
            idleStart = TimeField(right, "待机起点（秒）", v => recipe.idleStart = v);
            idleEnd = TimeField(right, "待机终点（秒）", v => recipe.idleEnd = v);
            right.Add(Button("让开场终点接到待机起点", () => EditRecipe(() => recipe.introEnd = recipe.idleStart)));
            introAudio = new Toggle("保留开场声音"); introAudio.RegisterValueChangedCallback(e => EditRecipe(() => recipe.keepIntroAudio = e.newValue)); right.Add(introAudio);
            idleAudio = new Toggle("保留待机声音（每次循环都会播放）"); idleAudio.RegisterValueChangedCallback(e => EditRecipe(() => recipe.keepIdleAudio = e.newValue)); right.Add(idleAudio);
            frameRate = new IntegerField("输出帧率"); frameRate.RegisterValueChangedCallback(e => EditRecipe(() => recipe.outputFrameRate = Math.Clamp(e.newValue, 1, 120))); right.Add(frameRate);
            Help(right, "开场和待机区间独立选择。默认保留开场语音、去除待机声音。每份配置生成独立的 Intro / Idle / Poster 文件。");
            Section(editing, "3  生成素材");
            outputInfo = new Label(); outputInfo.AddToClassList("help"); editing.Add(outputInfo);
            generate = Button("生成 / 更新这份配置的素材", GenerateAssets); generate.AddToClassList("primary"); editing.Add(generate);
            editing.Add(Button("在 Project 中查看生成素材", () => {
                if (recipe && !string.IsNullOrEmpty(recipe.outputFolder)) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(recipe.outputFolder));
            }));
            Section(editing, "4  配置场景角色");
            targetField = new ObjectField("目标角色（RawImage）") { objectType = typeof(RawImage), allowSceneObjects = true };
            targetField.RegisterValueChangedCallback(e => SelectTarget(e.newValue as RawImage)); editing.Add(targetField);
            Row(editing, Button("使用 Hierarchy 中选中的角色", UseSelectedCharacter), Button("在选中的 Canvas 下新建角色", CreateCharacter),
                Button("从目标角色读取制作配置", () => { if (targetImage) SelectRecipe(targetImage.GetComponent<FmvCharacterLoop>()?.Recipe); }));
            greenScreen = new Toggle("使用绿幕抠像"); greenScreen.RegisterValueChangedCallback(e => EditRecipe(() => recipe.greenScreen = e.newValue)); editing.Add(greenScreen);
            threshold = Parameter(editing, "绿幕阈值", 0, 0.5f, v => recipe.threshold = v);
            softness = Parameter(editing, "边缘柔化", 0.001f, 0.5f, v => recipe.softness = v);
            despill = Parameter(editing, "去绿强度", 0, 1, v => recipe.despill = v);
            Help(editing, "选场景中的 RawImage，或选 Canvas 后新建角色。配置会补齐两段视频的播放器、声音和独立画面纹理；保留已有位置与大小。抠像调整需点下面按钮应用。");
            bind = Button("把生成素材配置到目标角色", ConfigureScene); bind.AddToClassList("primary"); editing.Add(bind);
            editing.Add(Button("保存目标角色所在场景", () => {
                if (!targetImage || EditorUtility.IsPersistent(targetImage)) { status.text = "请先选择场景目标角色。"; return; }
                status.text = EditorSceneManager.SaveScene(targetImage.gameObject.scene) ? "场景已保存。" : "场景没有保存。";
            }));
            progress = new ProgressBar { title = "生成进度" }; progress.AddToClassList("progress"); scroll.Add(progress);
            cancel = Button("取消生成", CancelExport); scroll.Add(cancel);
            status = new Label("请选择或新建制作配置。"); status.AddToClassList("status"); scroll.Add(status);
            RefreshFields(); PreparePreview();
        }
        static Button Button(string text, Action action) { var button = new Button(action) { text = text }; button.AddToClassList("tool-button"); return button; }
        static void Row(VisualElement parent, params VisualElement[] children)
        {
            var row = new VisualElement(); row.AddToClassList("row"); foreach (var child in children) row.Add(child); parent.Add(row);
        }
        static void Help(VisualElement parent, string text) { var label = new Label(text); label.AddToClassList("help"); parent.Add(label); }
        static void Section(VisualElement parent, string text) { var label = new Label(text); label.AddToClassList("section"); parent.Add(label); }
        DoubleField TimeField(VisualElement parent, string label, Action<double> set)
        {
            var field = new DoubleField(label) { formatString = "0.000" }; field.AddToClassList("time-field");
            field.RegisterValueChangedCallback(e => EditRecipe(() => { if (Finite(e.newValue)) set(Snap(e.newValue)); }));
            var button = Button("取当前帧", () => EditRecipe(() => set(Snap(PreviewTime)))); button.AddToClassList("mark-button");
            Row(parent, field, button); return field;
        }
        Slider Parameter(VisualElement parent, string label, float min, float max, Action<float> set)
        {
            var slider = new Slider(label, min, max) { showInputField = true };
            slider.RegisterValueChangedCallback(e => EditRecipe(() => set(e.newValue))); parent.Add(slider); return slider;
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        double Snap(double time) => Math.Round(time * Rate) / Rate;
        void EditRecipe(Action edit)
        {
            if (!recipe || process != null) return;
            Undo.RecordObject(recipe, "Edit FMV recipe"); edit(); Normalize();
            EditorUtility.SetDirty(recipe); AssetDatabase.SaveAssetIfDirty(recipe); RefreshFields();
        }
        void Normalize()
        {
            if (!recipe || Duration <= 0) return;
            double duration = Duration, frame = Math.Min(1 / Rate, duration / 2);
            recipe.introStart = Math.Clamp(Finite(recipe.introStart) ? recipe.introStart : 0, 0, duration - frame);
            recipe.introEnd = Math.Clamp(Finite(recipe.introEnd) ? recipe.introEnd : duration, recipe.introStart + frame, duration);
            recipe.idleStart = Math.Clamp(Finite(recipe.idleStart) ? recipe.idleStart : 0, 0, duration - frame);
            recipe.idleEnd = Math.Clamp(Finite(recipe.idleEnd) ? recipe.idleEnd : duration, recipe.idleStart + frame, duration);
            recipe.outputFrameRate = Math.Clamp(recipe.outputFrameRate, 1, 120);
        }
        public void SelectRecipe(FmvVideoRecipe value)
        {
            if (process != null) return;
            recipe = value;
            FmvLoopToolSettings.instance.lastRecipeGuid = value ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value)) : "";
            FmvLoopToolSettings.instance.Store(); RefreshFields(); PreparePreview();
        }
        public void SelectTarget(RawImage value) { targetImage = value; targetField?.SetValueWithoutNotify(value); }
        void ChangeSource(VideoClip value)
        {
            if (!recipe || process != null) { RefreshFields(); return; }
            EditRecipe(() => {
                recipe.source = value;
                double duration = value ? value.length : 10;
                recipe.introStart = 0; recipe.introEnd = Math.Min(6, duration / 2);
                recipe.idleStart = recipe.introEnd; recipe.idleEnd = duration;
                recipe.outputFrameRate = value ? Math.Clamp((int)Math.Round(value.frameRate), 1, 120) : 24;
            }); PreparePreview();
        }
        void RefreshFields()
        {
            if (recipeField == null) return;
            recipeField.SetValueWithoutNotify(recipe); sourceField.SetValueWithoutNotify(recipe ? recipe.source : null); sourceField.SetEnabled(recipe);
            targetField.SetValueWithoutNotify(targetImage);
            introStart.SetValueWithoutNotify(recipe ? recipe.introStart : 0); introEnd.SetValueWithoutNotify(recipe ? recipe.introEnd : 6);
            idleStart.SetValueWithoutNotify(recipe ? recipe.idleStart : 6); idleEnd.SetValueWithoutNotify(recipe ? recipe.idleEnd : 10);
            frameRate.SetValueWithoutNotify(recipe ? recipe.outputFrameRate : 24);
            introAudio.SetValueWithoutNotify(recipe && recipe.keepIntroAudio); idleAudio.SetValueWithoutNotify(recipe && recipe.keepIdleAudio);
            greenScreen.SetValueWithoutNotify(recipe && recipe.greenScreen);
            threshold.SetValueWithoutNotify(recipe ? recipe.threshold : 0.035f); softness.SetValueWithoutNotify(recipe ? recipe.softness : 0.16f); despill.SetValueWithoutNotify(recipe ? recipe.despill : 1);
            timeline.highValue = (float)Duration;
            outputInfo.text = !recipe ? "先新建或选择制作配置。" :
                $"输出目录：{(string.IsNullOrEmpty(recipe.outputFolder) ? "首次生成时创建" : recipe.outputFolder)}\n" +
                $"开场：{(recipe.introClip ? AssetDatabase.GetAssetPath(recipe.introClip) : "尚未生成")}\n待机：{(recipe.idleClip ? AssetDatabase.GetAssetPath(recipe.idleClip) : "尚未生成")}\n首帧：{(recipe.poster ? AssetDatabase.GetAssetPath(recipe.poster) : "尚未生成")}\n" +
                (GeneratedCurrent() ? "素材与当前切分设置一致。" : "切分设置或原视频有变化，请生成素材。") +
                (string.IsNullOrEmpty(recipe.exportedAt) ? "" : "  上次生成：" + recipe.exportedAt);
        }
        public FmvVideoRecipe CreateAt(string path, bool duplicate = false)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var created = duplicate && recipe ? Instantiate(recipe) : CreateInstance<FmvVideoRecipe>();
            created.name = Path.GetFileNameWithoutExtension(path);
            if (!duplicate && Selection.activeObject is VideoClip selected) created.source = selected;
            created.introClip = null; created.idleClip = null; created.poster = null; created.outputFolder = ""; created.exportedSignature = ""; created.exportedAt = "";
            AssetDatabase.CreateAsset(created, AssetDatabase.GenerateUniqueAssetPath(path)); SelectRecipe(created);
            if (!duplicate && created.source) ChangeSource(created.source);
            AssetDatabase.SaveAssets(); EditorGUIUtility.PingObject(created); return created;
        }
        void CreateRecipe(bool duplicate)
        {
            if (duplicate && !recipe) { status.text = "请先选一份要复制的配置。"; return; }
            EnsureFolder("Assets/FMV/Sequences");
            string path = EditorUtility.SaveFilePanelInProject(duplicate ? "另存为制作配置" : "新建制作配置", duplicate ? recipe.name + "_副本" : "新角色", "asset", "选择配置保存位置", "Assets/FMV/Sequences");
            if (!string.IsNullOrEmpty(path)) CreateAt(path, duplicate);
        }
        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path) || path == "Assets") return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        void PreparePreview()
        {
            CleanupPreview();
            if (status == null) return;
            if (!recipe || !recipe.source) { status.text = "选择制作配置后，拖入原视频，或使用“导入视频文件”。"; return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) { status.text = "退出 Play 后可以预览和制作素材。"; return; }
            var clip = recipe.source;
            if (clip.width == 0 || clip.height == 0 || clip.frameCount == 0 || clip.length <= 0)
            { status.text = "Unity 无法读取这个原视频，请用“导入视频文件并转换为兼容格式”。"; return; }
            var go = new GameObject("FMV Loop Preview") { hideFlags = HideFlags.HideAndDontSave };
            preview = go.AddComponent<VideoPlayer>(); preview.playOnAwake = false; preview.source = VideoSource.VideoClip; preview.clip = clip;
            preview.renderMode = VideoRenderMode.RenderTexture;
            int width = Math.Min(960, (int)clip.width), height = Math.Max(1, (int)Math.Round((double)width * clip.height / clip.width));
            texture = new RenderTexture(width, height, 0) { hideFlags = HideFlags.HideAndDontSave };
            preview.aspectRatio = VideoAspectRatio.FitInside;
            texture.Create(); preview.targetTexture = texture; image.image = texture;
            preview.audioOutputMode = VideoAudioOutputMode.Direct;
            preview.controlledAudioTrackCount = (ushort)Math.Min(1, (int)clip.audioTrackCount); UpdateAudio();
            preview.prepareCompleted += p => { status.text = "预览已就绪。调整区间后先生成素材，再配置目标角色。"; Seek(recipe.idleStart); };
            preview.seekCompleted += p => { seeking = false; if (pendingSeek) { pendingSeek = false; QueueSeek(pendingTime, resumeAfterSeek); } else if (resumeAfterSeek) p.Play(); };
            preview.errorReceived += (p, error) => { status.text = "预览失败：" + error + "；可用“导入视频文件”转换原片。"; seeking = false; };
            preview.loopPointReached += p => { if (looping) QueueSeek(previewStart, true); };
            status.text = "正在加载原视频…"; preview.Prepare();
        }
        void UpdateAudio() { if (preview && preview.clip.audioTrackCount > 0) preview.SetDirectAudioMute(0, !audition); }
        public void Seek(double time) { if (!Finite(time)) return; looping = false; QueueSeek(time, false); }
        void QueueSeek(double time, bool resume)
        {
            pendingTime = Math.Clamp(Snap(time), 0, Math.Max(0, Duration - 1 / Rate)); resumeAfterSeek = resume;
            if (!preview || !preview.isPrepared) return;
            if (seeking) { pendingSeek = true; return; }
            preview.Pause(); seeking = true; preview.time = pendingTime;
        }
        public void PlayPreview(bool loop)
        {
            if (!recipe) return;
            PlayRange(loop ? recipe.idleStart : recipe.introStart, loop ? recipe.idleEnd : recipe.introEnd, loop);
        }
        void PlayRange(double start, double end, bool loop)
        {
            if (!preview || !preview.isPrepared) return;
            looping = loop; previewStart = start; previewEnd = end; QueueSeek(start, true);
        }
        void Pause() { looping = false; resumeAfterSeek = false; if (preview) preview.Pause(); }
        void Tick()
        {
            bool editMode = !EditorApplication.isPlayingOrWillChangePlaymode;
            if (editing != null) editing.SetEnabled(editMode && process == null);
            generate?.SetEnabled(recipe && recipe.source && process == null && editMode);
            bind?.SetEnabled(GeneratedCurrent() && targetImage && !EditorUtility.IsPersistent(targetImage) && process == null && editMode);
            import?.SetEnabled(recipe && process == null && editMode);
            cancel?.SetEnabled(process != null); progress?.SetEnabled(process != null);
            if (preview && preview.isPrepared)
            {
                if (preview.isPlaying && !seeking && preview.time >= previewEnd - 0.5 / Rate) { if (looping) QueueSeek(previewStart, true); else Pause(); }
                timeline?.SetValueWithoutNotify((float)preview.time); currentTime?.SetValueWithoutNotify(preview.time);
                if (positionLabel != null) positionLabel.text = $"{preview.time:F3} / {Duration:F3} 秒 · 第 {preview.frame} 帧 · 原片约 {Rate:F2} fps";
                EditorApplication.QueuePlayerLoopUpdate(); image?.MarkDirtyRepaint();
            }
            PollExport();
        }
        void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) { if (process != null) CancelExport(); CleanupPreview(); }
            if (state == PlayModeStateChange.EnteredEditMode && image != null) PreparePreview();
        }
        void CleanupPreview()
        {
            if (preview) { preview.Stop(); DestroyImmediate(preview.gameObject); }
            if (texture) { texture.Release(); DestroyImmediate(texture); }
            if (image != null) image.image = null;
            seeking = pendingSeek = looping = resumeAfterSeek = false;
        }
        void OnDisable()
        {
            EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= PlayModeChanged; Undo.undoRedoPerformed -= UndoChanged;
            CleanupPreview(); CancelProcess(); CleanupStaging();
        }
    }
}
