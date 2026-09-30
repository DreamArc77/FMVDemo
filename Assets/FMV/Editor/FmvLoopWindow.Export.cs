using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace FMV.Editor
{
    public sealed partial class FmvLoopWindow
    {
        Process process;
        string staging, operationError = "", importDestination;
        int job;
        bool importing;
        ExportPlan plan;
        readonly object errorLock = new object();
        sealed class ExportPlan
        {
            public FmvVideoRecipe recipe;
            public string input, folder, signature;
            public double introStart, introEnd, idleStart, idleEnd;
            public int fps;
            public bool introAudio, idleAudio;
        }
        static bool AssetFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            string full = Path.GetFullPath(Path.Combine(ProjectRoot, path));
            return full.StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        static string Signature(FmvVideoRecipe r)
        {
            if (!r || !r.source) return "";
            return string.Join("|", new[] { AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(r.source)).ToString(),
                Number(r.introStart), Number(r.introEnd), Number(r.idleStart), Number(r.idleEnd), r.outputFrameRate.ToString(), r.keepIntroAudio.ToString(), r.keepIdleAudio.ToString() });
        }
        bool GeneratedCurrent() => recipe && recipe.introClip && recipe.idleClip && recipe.poster && recipe.exportedSignature == Signature(recipe);
        bool CheckEncoder()
        {
            if (File.Exists(Path.Combine(ProjectRoot, "Tools/Video/ffmpeg.exe"))) return true;
            status.text = "缺少项目自带的 Tools/Video/ffmpeg.exe。"; return false;
        }
        public void GenerateAssets()
        {
            if (process != null || EditorApplication.isPlayingOrWillChangePlaymode || !recipe || !recipe.source || !CheckEncoder()) return;
            if (Duration <= 0 || recipe.source.frameCount == 0) { status.text = "Unity 无法读取原片，请先导入兼容格式副本。"; return; }
            Normalize();
            if (!AssetFolder(recipe.outputFolder))
            {
                string directory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(recipe)).Replace('\\', '/');
                recipe.outputFolder = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + recipe.name + "_Media");
                EditorUtility.SetDirty(recipe); AssetDatabase.SaveAssetIfDirty(recipe);
            }
            var owners = AssetDatabase.FindAssets("t:FmvVideoRecipe").Select(g => AssetDatabase.LoadAssetAtPath<FmvVideoRecipe>(AssetDatabase.GUIDToAssetPath(g)));
            if (owners.Any(r => r != recipe && r.outputFolder == recipe.outputFolder)) { status.text = "输出目录被另一份配置使用，请另存为新配置再生成。"; return; }
            plan = new ExportPlan { recipe = recipe, input = Path.Combine(ProjectRoot, AssetDatabase.GetAssetPath(recipe.source)), folder = recipe.outputFolder,
                introStart = recipe.introStart, introEnd = recipe.introEnd, idleStart = recipe.idleStart, idleEnd = recipe.idleEnd,
                fps = recipe.outputFrameRate, introAudio = recipe.keepIntroAudio, idleAudio = recipe.keepIdleAudio, signature = Signature(recipe) };
            if (!File.Exists(plan.input)) { status.text = "原视频文件不存在。"; return; }
            if (new[] { "Intro.mp4", "Idle.mp4", "Poster.png" }.Any(f => string.Equals(Path.GetFullPath(plan.input), Path.GetFullPath(Path.Combine(ProjectRoot, plan.folder, f)), StringComparison.OrdinalIgnoreCase)))
            { status.text = "原视频不能放在这份配置的输出文件位置。请更换源文件。"; return; }
            importing = false; job = 0; BeginOperation();
            try { StartJob(); } catch (Exception e) { AbortFailed(e); }
        }
        void BeginOperation()
        {
            Pause(); staging = Path.Combine(ProjectRoot, "Temp/FmvLoopExport", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
            editing.SetEnabled(false); progress.value = 0;
        }
        public void ImportFromFile(string source, string assetPath)
        {
            if (!recipe || process != null || EditorApplication.isPlayingOrWillChangePlaymode || !CheckEncoder()) return;
            if (!File.Exists(source) || !AssetFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'))) { status.text = "请选择有效视频文件和 Assets 下的输出位置。"; return; }
            EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            importDestination = AssetDatabase.GenerateUniqueAssetPath(assetPath); importing = true; BeginOperation();
            string args = "-y -hide_banner -loglevel error -i " + Quote(source) + " -map 0:v:0 -map 0:a:0? -vf " + Quote("fps=" + recipe.outputFrameRate + ",scale=trunc(iw/2)*2:trunc(ih/2)*2,setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709") + " -c:v libx264 -preset fast -crf 18 -pix_fmt yuv420p -color_primaries bt709 -color_trc bt709 -colorspace bt709 -c:a aac -b:a 160k -movflags +faststart " + Quote(Path.Combine(staging, "Source.mp4"));
            try { StartProcess(args); status.text = "正在导入并转换完整视频；原文件不会被修改。"; progress.title = "兼容化原视频"; }
            catch (Exception e) { AbortFailed(e); }
        }
        void ImportVideo()
        {
            if (!recipe) return;
            string file = EditorUtility.OpenFilePanel("选择原视频（将生成兼容格式副本）", "", "mp4,mov,mkv,avi,webm,m4v");
            if (string.IsNullOrEmpty(file)) return;
            EnsureFolder("Assets/FMV/Sources");
            string output = EditorUtility.SaveFilePanelInProject("保存兼容视频副本", Path.GetFileNameWithoutExtension(file) + "_Compatible", "mp4", "不会覆盖原文件", "Assets/FMV/Sources");
            if (!string.IsNullOrEmpty(output)) ImportFromFile(file, output);
        }
        void CommitImport()
        {
            EnsureFolder(Path.GetDirectoryName(importDestination).Replace('\\', '/'));
            File.Copy(Path.Combine(staging, "Source.mp4"), Path.Combine(ProjectRoot, importDestination), false);
            AssetDatabase.ImportAsset(importDestination, ImportAssetOptions.ForceSynchronousImport);
            var source = AssetDatabase.LoadAssetAtPath<VideoClip>(importDestination);
            if (!source || source.frameCount == 0) throw new Exception("转换副本未能被 Unity 识别，请查看 Console。");
            ChangeSource(source); Finish("视频已导入并选为原素材：" + importDestination); PreparePreview();
        }
        static string Quote(string value) => "\"" + value + "\"";
        static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
        void StartProcess(string args)
        {
            lock (errorLock) operationError = "";
            process = new Process { StartInfo = new ProcessStartInfo { FileName = Path.Combine(ProjectRoot, "Tools/Video/ffmpeg.exe"), Arguments = args,
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true } };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errorLock) operationError += e.Data + "\n"; };
            process.Start(); process.BeginErrorReadLine();
        }
        void StartJob()
        {
            string args = "-y -hide_banner -loglevel error -i " + Quote(plan.input);
            if (job == 2) args += " -ss " + Number(plan.introStart) + " -frames:v 1 -update 1 " + Quote(Path.Combine(staging, "Poster.png"));
            else
            {
                double start = job == 0 ? plan.introStart : plan.idleStart, end = job == 0 ? plan.introEnd : plan.idleEnd;
                bool audio = job == 0 ? plan.introAudio : plan.idleAudio;
                args += " -ss " + Number(start) + " -t " + Number(end - start) + " -map 0:v:0 " + (audio ? "-map 0:a:0? -c:a aac -b:a 160k" : "-an") +
                    " -vf " + Quote("fps=" + plan.fps + ",scale=trunc(iw/2)*2:trunc(ih/2)*2,setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709") + " -c:v libx264 -preset fast -crf 18 -pix_fmt yuv420p -color_primaries bt709 -color_trc bt709 -colorspace bt709 -movflags +faststart " + Quote(Path.Combine(staging, job == 0 ? "Intro.mp4" : "Idle.mp4"));
            }
            StartProcess(args); progress.value = job * 33; progress.title = new[] { "生成开场", "生成待机", "生成首帧" }[job]; status.text = progress.title + "…";
        }
        void PollExport()
        {
            if (process == null || !process.HasExited) return;
            process.WaitForExit(); int code = process.ExitCode; process.Dispose(); process = null;
            if (code != 0) { Finish("生成失败，现有素材未替换：" + operationError); return; }
            try { if (importing) CommitImport(); else if (++job < 3) StartJob(); else CommitExport(); }
            catch (Exception e) { Finish("未完成：" + e.Message); UnityEngine.Debug.LogException(e); }
        }
        void CommitExport()
        {
            if (!plan.recipe || Signature(plan.recipe) != plan.signature) throw new Exception("制作配置已改变，请重新生成。");
            EnsureFolder(plan.folder);
            var files = new[] { "Intro.mp4", "Idle.mp4", "Poster.png" };
            foreach (var file in files) if (!File.Exists(Path.Combine(staging, file)) || new FileInfo(Path.Combine(staging, file)).Length == 0) throw new Exception("没有生成有效的 " + file);
            var existed = files.ToDictionary(f => f, f => File.Exists(Path.Combine(ProjectRoot, plan.folder, f)));
            foreach (var file in files) if (existed[file]) File.Copy(Path.Combine(ProjectRoot, plan.folder, file), Path.Combine(staging, "Backup_" + file), true);
            try
            {
                AssetDatabase.StartAssetEditing();
                try { foreach (var file in files) File.Copy(Path.Combine(staging, file), Path.Combine(ProjectRoot, plan.folder, file), true); }
                finally { AssetDatabase.StopAssetEditing(); }
                foreach (var file in files) AssetDatabase.ImportAsset(plan.folder + "/" + file, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var intro = AssetDatabase.LoadAssetAtPath<VideoClip>(plan.folder + "/Intro.mp4");
                var idle = AssetDatabase.LoadAssetAtPath<VideoClip>(plan.folder + "/Idle.mp4");
                var poster = AssetDatabase.LoadAssetAtPath<Texture2D>(plan.folder + "/Poster.png");
                if (!intro || !idle || !poster || intro.frameCount == 0 || idle.frameCount == 0) throw new Exception("Unity 无法读取生成的素材，旧素材将恢复。");
                var importer = (TextureImporter)AssetImporter.GetAtPath(plan.folder + "/Poster.png");
                importer.mipmapEnabled = false; importer.maxTextureSize = 4096; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
                plan.recipe.introClip = intro; plan.recipe.idleClip = idle; plan.recipe.poster = poster;
                plan.recipe.exportedSignature = plan.signature; plan.recipe.exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                EditorUtility.SetDirty(plan.recipe); AssetDatabase.SaveAssets();
                Finish("素材已生成。现在选择目标角色，点击“把生成素材配置到目标角色”。");
            }
            catch
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var file in files)
                    {
                        string destination = Path.Combine(ProjectRoot, plan.folder, file);
                        if (existed[file]) File.Copy(Path.Combine(staging, "Backup_" + file), destination, true);
                        else if (File.Exists(destination)) File.Delete(destination);
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.Refresh(); throw;
            }
        }
        void Finish(string result)
        {
            if (process != null) { process.Dispose(); process = null; }
            editing?.SetEnabled(true); progress.value = 100; progress.title = "已结束";
            RefreshFields(); if (status != null) status.text = result;
            CleanupStaging();
        }
        void AbortFailed(Exception e) { CancelProcess(); Finish("生成失败：" + e.Message); }
        void CancelProcess()
        {
            if (process == null) return;
            try { if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); } }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); process = null; }
        }
        void CleanupStaging()
        {
            if (string.IsNullOrEmpty(staging)) return;
            string root = Path.GetFullPath(Path.Combine(ProjectRoot, "Temp/FmvLoopExport")) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(staging);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
            try { if (Directory.Exists(path)) { foreach (string file in Directory.GetFiles(path)) File.Delete(file); Directory.Delete(path, false); } }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            staging = null;
        }
        public void CancelExport() { CancelProcess(); Finish("已取消；现有素材和原视频保持不变。"); }
    }
}

