using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
using Whisper.Native;
using Whisper.Utils;

namespace Whisper
{
    /// <summary>
    /// Manages Whisper model lifecycle in Unity scene.
    /// </summary>
    public class WhisperManager : MonoBehaviour
    {
        [Tooltip("Log level for whisper loading and inference")]
        public LogLevel logLevel = LogLevel.Log;

        [Header("Model")]
        [SerializeField]
        [Tooltip("Path to model weights file")]
        private string modelPath = "Whisper/ggml-tiny.bin";

        [SerializeField]
        [Tooltip("Determines whether the StreamingAssets folder should be prepended to the model path")]
        private bool isModelPathInStreamingAssets = true;

        [SerializeField]
        [Tooltip("Should model weights be loaded on awake?")]
        private bool initOnAwake = true;

        [Header("Inference")]
        [Tooltip("Try to load whisper in GPU for faster inference")]
        [SerializeField]
        private bool useGpu;

        [Tooltip("Use the Flash Attention algorithm for faster inference")]
        [SerializeField]
        private bool flashAttention;

        [Header("Language")]
        [Tooltip("Output text language. Use empty or \"auto\" for auto-detection.")]
        public string language = "en";

        [Tooltip("Force output text to English translation. Improves translation quality.")]
        public bool translateToEnglish;

        [Header("Command recognition")]
        [Tooltip("Force whisper to try to recognize commands from the transcription.")]
        public bool enableCommandRecognition = false;

        [Tooltip("Threshold for LCS. Higher requires more accurate transcription to match a command.")]
        [Range(0f, 1f)]
        public float lcsSimilarityThreshold = 0.6f;

        [Tooltip("Maximum allowed Levenshtein distance. Lower requires more accourate transcription to match a command.")]
        [Range(0, 10)]
        public int maxLevenshteinDistance = 2;

        [Tooltip("List of commands to recognize, if commands are active")]
        public List<string> availableCommands = new();

        [Header("Advanced settings")]
        [SerializeField]
        private WhisperSamplingStrategy strategy = WhisperSamplingStrategy.WHISPER_SAMPLING_GREEDY;

        [Tooltip("Do not use past transcription (if any) as initial prompt for the decoder.")]
        public bool noContext = true;

        [Tooltip("Force single segment output (useful for streaming).")]
        public bool singleSegment;

        [Tooltip("Output tokens with their confidence in each segment.")]
        public bool enableTokens;

        [Tooltip("Initial prompt as a string variable. " +
                 "It should improve transcription quality or guide it to the right direction.")]
        [TextArea]
        public string initialPrompt;

        [Header("Streaming settings")]
        [Tooltip("Minimal portions of audio that will be processed by whisper stream in seconds.")]
        public float stepSec = 3f;

        [Tooltip("How many seconds of previous segment will be used for current segment.")]
        public float keepSec = 0.2f;

        [Tooltip("How many seconds of audio will be recurrently transcribe until context update.")]
        public float lengthSec = 10f;

        [Tooltip("Should stream modify whisper prompt for better context handling?")]
        public bool updatePrompt = true;

        [Tooltip("If false stream will use all information from previous iteration.")]
        public bool dropOldBuffer;

        [Tooltip("If true stream will ignore audio chunks with no detected speech.")]
        public bool useVad = true;

        [Header("Experimental settings")]
        [Tooltip("[EXPERIMENTAL] Output timestamps for each token. Need enabled tokens to work.")]
        public bool tokensTimestamps;

        [Tooltip("[EXPERIMENTAL] Overwrite the audio context size (0 = use default). " +
                 "These can significantly reduce the quality of the output.")]
        public int audioCtx;

        /// <summary>
        /// Raised when whisper transcribed a new text segment from audio. 
        /// </summary>
        public event OnNewSegmentDelegate OnNewSegment;

        /// <summary>
        /// Raised when whisper made some progress in transcribing audio.
        /// Progress changes from 0 to 100 included.
        /// </summary>
        public event OnProgressDelegate OnProgress;

        /// <summary>
        /// Raised when command recognized by command system.
        /// </summary>
        public event Action<CommandMatchResult> OnCommandRecognized;

        private WhisperWrapper _whisper;
        private WhisperParams _params;
        private WhisperCommands _whisperCommands;
        private readonly MainThreadDispatcher _dispatcher = new MainThreadDispatcher();

        public string ModelPath
        {
            get => modelPath;
            set
            {
                if (IsLoaded || IsLoading)
                {
                    throw new InvalidOperationException("Cannot change model path after loading the model");
                }

                modelPath = value;
            }
        }

        public bool IsModelPathInStreamingAssets
        {
            get => isModelPathInStreamingAssets;
            set
            {
                if (IsLoaded || IsLoading)
                {
                    throw new InvalidOperationException("Cannot change model path after loading the model");
                }

                isModelPathInStreamingAssets = value;
            }
        }

        /// <summary>
        /// Checks if whisper weights are loaded and ready to be used.
        /// </summary>
        public bool IsLoaded => _whisper != null;

        /// <summary>
        /// Checks if whisper weights are still loading and not ready.
        /// </summary>
        public bool IsLoading { get; private set; }

        private async void Awake()
        {
            LogUtils.Level = logLevel;

            if (!initOnAwake)
                return;
            await InitModel();
        }

        private void OnValidate()
        {
            LogUtils.Level = logLevel;
        }

        private void Update()
        {
            _dispatcher.Update();
        }

        /// <summary>
        /// Initialize command system.
        /// </summary>
        public async Task InitCommandSystem(MicrophoneRecord microphoneRecord, int timeout = 10000, int delay = 250)
        {
            int elapsed = 0;
            while (!IsLoaded && elapsed < timeout)
            {
                await Task.Delay(delay);
                elapsed += delay;
            }

            if (enableCommandRecognition)
            {
                _whisperCommands = new WhisperCommands(
                    _whisper,
                    _params,
                    availableCommands,
                    lcsSimilarityThreshold,
                    maxLevenshteinDistance,
                    microphoneRecord,
                    1
                );

                _whisperCommands.OnCommandRecognized += OnCommandRecognizedHandler;
            }
        }

        /// <summary>
        /// Load model and default parameters. Prepare it for text transcription.
        /// </summary>
        public async Task InitModel()
        {
            // check if model is already loaded or actively loading
            if (IsLoaded)
            {
                LogUtils.Warning("Whisper model is already loaded and ready for use!");
                return;
            }

            if (IsLoading)
            {
                LogUtils.Warning("Whisper model is already loading!");
                return;
            }

            // load model and default params
            IsLoading = true;
            try
            {
                var path = isModelPathInStreamingAssets
                    ? Path.Combine(Application.streamingAssetsPath, modelPath)
                    : modelPath;

                var context = CreateContextParams();
                _whisper = await WhisperWrapper.InitFromFileAsync(path, context);
                _params = WhisperParams.GetDefaultParams(strategy);
                UpdateParams();

                _whisper.OnNewSegment += OnNewSegmentHandler;
                _whisper.OnProgress += OnProgressHandler;
            }
            catch (Exception e)
            {
                LogUtils.Exception(e);
            }

            IsLoading = false;
        }

        /// <summary>
        /// Checks if currently loaded whisper model supports multilingual transcription.
        /// </summary>
        public bool IsMultilingual()
        {
            if (!IsLoaded)
            {
                LogUtils.Error("Whisper model isn't loaded! Init Whisper model first!");
                return false;
            }

            return _whisper.IsMultilingual;
        }

        // Métodos públicos para controlar el sistema de comandos
        public void StartListeningCommands()
        {
            if (_whisperCommands != null)
            {
                _whisperCommands.Start();
            }
        }

        public void StopListeningCommands()
        {
            if (_whisperCommands != null)
            {
                _whisperCommands.Stop();
            }
        }

        /// <summary>
        /// Start async transcription of audio clip.
        /// </summary>
        /// <returns>Full audio transcript. Null if transcription failed.</returns>
        public async Task<WhisperResult> GetTextAsync(AudioClip clip)
        {
            var isLoaded = await CheckIfLoaded();
            if (!isLoaded)
                return null;

            UpdateParams();
            var res = await _whisper.GetTextAsync(clip, _params);
            return res;
        }

        /// <summary>
        /// Start async transcription of audio clip and recognize all non-overlapping commands in order.
        /// </summary>
        /// <returns>List with command matches. Empty list if transcription is empty.</returns>
        public async Task<List<CommandMatchResult>> GetCommandsFromTextAsync(AudioClip clip)
        {
            var isLoaded = await CheckIfLoaded();
            if (!isLoaded || !enableCommandRecognition || _whisperCommands == null)
                return null;

            UpdateParams();
            _whisperCommands.UpdateParams(_params);
            return await _whisperCommands.GetCommandsFromAudioAsync(clip);
        }

        /// <summary>
        /// Start async transcription of audio buffer.
        /// </summary>
        /// <param name="samples">Raw audio buffer.</param>
        /// <param name="frequency">Audio sample rate.</param>
        /// <param name="channels">Audio channels count.</param>
        /// <returns>Full audio transcript. Null if transcription failed.</returns>
        public async Task<WhisperResult> GetTextAsync(float[] samples, int frequency, int channels)
        {
            var isLoaded = await CheckIfLoaded();
            if (!isLoaded)
                return null;

            UpdateParams();
            var res = await _whisper.GetTextAsync(samples, frequency, channels, _params);
            return res;
        }

        /// <summary>
        /// Start async transcription of audio buffer and recognize all non-overlapping commands in order.
        /// </summary>
        /// <param name="samples">Raw audio buffer.</param>
        /// <param name="frequency">Audio sample rate.</param>
        /// <param name="channels">Audio channels count.</param>
        /// <returns>List with command matches. Empty list if transcription is empty.</returns>
        public async Task<List<CommandMatchResult>> GetCommandsFromTextAsync(float[] samples, int frequency, int channels)
        {
            var isLoaded = await CheckIfLoaded();
            if (!isLoaded || !enableCommandRecognition || _whisperCommands == null)
                return null;

            UpdateParams();
            _whisperCommands.UpdateParams(_params);
            return await _whisperCommands.GetCommandsFromAudioAsync(samples, frequency, channels);
        }

        /// <summary>
        /// Create a new instance of Whisper streaming transcription.
        /// </summary>
        /// <param name="frequency">Audio sample rate.</param>
        /// <param name="channels">Audio channels count.</param>
        /// <returns>New streaming transcription. Null if failed.</returns>
        public async Task<WhisperStream> CreateStream(int frequency, int channels)
        {
            var isLoaded = await CheckIfLoaded();
            if (!isLoaded)
            {
                LogUtils.Error("Model weights aren't loaded! Load model first!");
                return null;
            }

            var param = new WhisperStreamParams(_params,
                frequency, channels, stepSec, keepSec, lengthSec, updatePrompt,
                dropOldBuffer, useVad);
            var stream = new WhisperStream(_whisper, param);
            return stream;
        }

        /// <summary>
        /// Create a new instance of Whisper streaming transcription from microphone input.
        /// </summary>
        /// <returns>New streaming transcription. Null if failed.</returns>
        public async Task<WhisperStream> CreateStream(MicrophoneRecord microphone)
        {
            var isLoaded = await CheckIfLoaded();
            if (!isLoaded)
            {
                LogUtils.Error("Model weights aren't loaded! Load model first!");
                return null;
            }

            // TODO: unity support only single input channel for microphone
            var channels = 1;
            var frequency = microphone.frequency;
            var param = new WhisperStreamParams(_params,
                frequency, channels, stepSec, keepSec, lengthSec, updatePrompt,
                dropOldBuffer, useVad);
            var stream = new WhisperStream(_whisper, param, microphone);
            return stream;
        }

        private void UpdateParams()
        {
            _params.Language = language;
            _params.Translate = translateToEnglish;
            _params.NoContext = noContext;
            _params.SingleSegment = singleSegment;
            _params.AudioCtx = audioCtx;
            _params.EnableTokens = enableTokens;
            _params.TokenTimestamps = tokensTimestamps;
            _params.InitialPrompt = initialPrompt;
        }

        private WhisperContextParams CreateContextParams()
        {
            var context = WhisperContextParams.GetDefaultParams();
            context.UseGpu = useGpu;
            context.FlashAttn = flashAttention;
            return context;
        }

        private async Task<bool> CheckIfLoaded()
        {
            if (!IsLoaded && !IsLoading)
            {
                LogUtils.Error("Whisper model isn't loaded! Init Whisper model first!");
                return false;
            }

            // wait while model still loading
            while (IsLoading)
            {
                await Task.Yield();
            }

            return IsLoaded;
        }

        private void OnNewSegmentHandler(WhisperSegment segment)
        {
            _dispatcher.Execute(() =>
            {
                OnNewSegment?.Invoke(segment);
            });
        }

        private void OnProgressHandler(int progress)
        {
            _dispatcher.Execute(() =>
            {
                OnProgress?.Invoke(progress);
            });
        }
        
        private void OnCommandRecognizedHandler(CommandMatchResult commandResult)
        {
            _dispatcher.Execute(() =>
            {
                OnCommandRecognized?.Invoke(commandResult);
            });
        }
    }

}