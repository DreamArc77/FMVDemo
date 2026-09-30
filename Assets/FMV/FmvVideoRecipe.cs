using UnityEngine;
using UnityEngine.Video;

namespace FMV
{
    [CreateAssetMenu(menuName = "FMV/视频制作配置", fileName = "FmvVideoRecipe")]
    public sealed class FmvVideoRecipe : ScriptableObject
    {
        public VideoClip source;
        public double introStart, introEnd = 6, idleStart = 6, idleEnd = 10;
        public int outputFrameRate = 24;
        public bool keepIntroAudio = true, keepIdleAudio;
        public bool greenScreen = true;
        public float threshold = 0.035f, softness = 0.16f, despill = 1;
        [HideInInspector] public string outputFolder;
        [HideInInspector] public string exportedSignature;
        [HideInInspector] public string exportedAt;
        public VideoClip introClip, idleClip;
        public Texture2D poster;
    }
}
