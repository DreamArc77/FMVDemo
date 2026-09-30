using System.Collections;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.InputSystem;

namespace FMV
{
    [DisallowMultipleComponent]
    public sealed class FmvCharacterLoop : MonoBehaviour
    {
        [SerializeField] private VideoPlayer player;
        [SerializeField] private VideoPlayer idlePlayer;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private UnityEngine.UI.RawImage character;
        [SerializeField] private Texture poster;
        [SerializeField] private FmvVideoRecipe recipe;
        [SerializeField] private AudioSource idleAudioSource;
        public FmvVideoRecipe Recipe => recipe;
        public enum PlaybackPhase { Preparing, Intro, Idle, Error }
        public PlaybackPhase Phase { get; private set; }
        public int IntroPlayCount { get; private set; }
        public int CompletedLoops { get; private set; }
        public bool FirstFrameReady { get; private set; }
        public bool IsPaused { get; private set; }

        private void OnEnable()
        {
            if (!player || !idlePlayer || !audioSource || !character)
            {
                Phase = PlaybackPhase.Error;
                Debug.LogError("FMV intro/idle player references are missing.", this);
                return;
            }
            player.playOnAwake = idlePlayer.playOnAwake = false;
            if (recipe)
            {
                player.clip = recipe.introClip;
                idlePlayer.clip = recipe.idleClip;
                poster = recipe.poster;
            }
            if (!player.clip || !idlePlayer.clip)
            {
                Phase = PlaybackPhase.Error;
                Debug.LogError("FMV clips are missing. Generate the recipe before playing.", this);
                return;
            }
            player.isLooping = false;
            idlePlayer.isLooping = true;
            ConfigureAudio(player, audioSource, true);
            ConfigureAudio(idlePlayer, idleAudioSource, recipe && recipe.keepIdleAudio);
            player.frameReady += IntroFrame;
            idlePlayer.frameReady += IdleFrame;
            player.loopPointReached += IntroEnded;
            idlePlayer.loopPointReached += IdleLooped;
            player.errorReceived += Failed;
            idlePlayer.errorReceived += Failed;
            ReplayIntro();
        }

        static void ConfigureAudio(VideoPlayer video, AudioSource audio, bool enabled)
        {
            bool hasAudio = enabled && audio && video.clip.audioTrackCount > 0;
            video.audioOutputMode = hasAudio ? VideoAudioOutputMode.AudioSource : VideoAudioOutputMode.None;
            video.controlledAudioTrackCount = (ushort)(hasAudio ? 1 : 0);
            if (!hasAudio) return;
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0f;
            video.EnableAudioTrack(0, true);
            video.SetTargetAudioSource(0, audio);
        }

        public void ReplayIntro()
        {
            StopAllCoroutines();
            player.Stop(); idlePlayer.Stop(); audioSource.Stop();
            if (idleAudioSource) idleAudioSource.Stop();
            IntroPlayCount = CompletedLoops = 0;
            IsPaused = FirstFrameReady = false;
            Phase = PlaybackPhase.Preparing;
            character.texture = poster;
            player.sendFrameReadyEvents = idlePlayer.sendFrameReadyEvents = true;
            StartCoroutine(PrepareAndPlay());
        }

        private IEnumerator PrepareAndPlay()
        {
            yield return null;
            player.Prepare(); idlePlayer.Prepare();
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!player.isPrepared)
            {
                if (Time.realtimeSinceStartup > deadline) { Failed(player,"Intro preparation timed out."); yield break; }
                yield return null;
            }
            yield return null;
            Phase = PlaybackPhase.Intro;
            IntroPlayCount++;
            if (!IsPaused) player.Play();
        }

        private void IntroFrame(VideoPlayer source, long frame)
        {
            if (Phase != PlaybackPhase.Intro) return;
            character.texture = source.targetTexture;
            FirstFrameReady = true;
            source.sendFrameReadyEvents = false;
        }
        private void IntroEnded(VideoPlayer source)
        {
            Phase = PlaybackPhase.Idle;
            StartCoroutine(StartIdle());
        }
        private IEnumerator StartIdle()
        {
            // Hold the final intro frame until the preloaded idle produces its first frame.
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!idlePlayer.isPrepared)
            {
                if (Time.realtimeSinceStartup > deadline) { Failed(idlePlayer,"Idle preparation timed out."); yield break; }
                yield return null;
            }
            if (!IsPaused) idlePlayer.Play();
        }
        private void IdleFrame(VideoPlayer source, long frame)
        {
            if (Phase != PlaybackPhase.Idle) return;
            character.texture = source.targetTexture;
            source.sendFrameReadyEvents = false;
        }
        private void IdleLooped(VideoPlayer source) => CompletedLoops++;
        public void TogglePause()
        {
            IsPaused = !IsPaused;
            var active = Phase == PlaybackPhase.Idle ? idlePlayer : player;
            if (IsPaused) active.Pause();
            else if (active.isPrepared && Phase != PlaybackPhase.Error && Phase != PlaybackPhase.Preparing) active.Play();
        }
        private void Failed(VideoPlayer source, string message)
        {
            Phase = PlaybackPhase.Error;
            StopAllCoroutines();
            player.Pause(); idlePlayer.Pause();
            Debug.LogError("FMV " + source.name + ": " + message, this);
        }
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || Phase == PlaybackPhase.Error) return;
            if (keyboard.spaceKey.wasPressedThisFrame) TogglePause();
            if (keyboard.rKey.wasPressedThisFrame) ReplayIntro();
        }
        private void OnDisable()
        {
            StopAllCoroutines();
            if (player)
            {
                player.frameReady -= IntroFrame;
                player.loopPointReached -= IntroEnded;
                player.errorReceived -= Failed;
                player.Stop();
            }
            if (idlePlayer)
            {
                idlePlayer.frameReady -= IdleFrame;
                idlePlayer.loopPointReached -= IdleLooped;
                idlePlayer.errorReceived -= Failed;
                idlePlayer.Stop();
            }
            if (audioSource) audioSource.Stop();
            if (idleAudioSource) idleAudioSource.Stop();
            if (character) character.texture = poster;
        }
    }
}
