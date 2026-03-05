using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Whisper;
using Whisper.Utils;


/// <summary>
/// Optimized voice command controller that processes audio in short chunks
/// and only recognizes complete commands without intermediate transcription updates.
/// </summary>
public class WhisperCommands
{
    private readonly WhisperWrapper _whisperWrapper;
    private WhisperParams _params;
    private readonly CommandMatcher _commandMatcher;
    private List<string> _availableCommands;
    private readonly MicrophoneRecord _microphone;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly float _silenceTimeout = 1.5f;
    private bool _isRunning;
    private List<float> _audioBuffer = new List<float>();
    private DateTime _lastVoiceDetectedTime;
    private string _currentTranscription = "";
    private DateTime _lastBufferProcessedTime;
    private readonly float _maxBufferAge = 2.0f;
    private string _lastProcessedTranscription = "";
    private DateTime _lastCommandTime = DateTime.MinValue;
    private const double MinTimeBetweenCommandsSec = 1.0;
    private HashSet<string> _processedChunks = new HashSet<string>();
    private bool _isProcessing = false;
    private readonly SemaphoreSlim _processingLock = new SemaphoreSlim(1, 1);
    private int _pendingChunks = 0;

    /// <summary>
    /// Event triggered when a command is recognized.
    /// </summary>
    public event Action<CommandMatchResult> OnCommandRecognized;

    /// <summary>
    /// Initializes a new instance of the WhisperCommands.
    /// </summary>
    public WhisperCommands(
        WhisperWrapper whisperWrapper,
        WhisperParams wparams,
        List<string> availableCommands,
        float lcsSimilarityThreshold,
        int maxLevenshteinDistance,
        MicrophoneRecord microphone,
        int channels = 1)
    {
        if (microphone.useVad != true)
        {
            throw new ArgumentException("Microphone VAD must be enabled for WhisperCommands to work.");
        }

        _whisperWrapper = whisperWrapper;
        _params = wparams;
        _commandMatcher = new CommandMatcher(lcsSimilarityThreshold, maxLevenshteinDistance);
        _availableCommands = availableCommands;
        _sampleRate = microphone.frequency;
        _channels = channels;
        _microphone = microphone;
    }

    /// <summary>
    /// Starts listening for voice commands.
    /// </summary>
    public void Start()
    {
        if (!_isRunning)
        {
            _isRunning = true;
            _audioBuffer.Clear();
            _currentTranscription = "";
            _lastVoiceDetectedTime = DateTime.Now;
            _isProcessing = false;
            _pendingChunks = 0;
            _microphone.OnChunkReady += OnAudioChunkReady;
            _microphone.StartRecord();
        }
    }

    /// <summary>
    /// Stops listening for voice commands.
    /// </summary>
    public void Stop()
    {
        if (_isRunning)
        {
            _isRunning = false;
            _microphone.OnChunkReady -= OnAudioChunkReady;
            _microphone.StopRecord();
        }
    }

    /// <summary>
    /// Recognizes commands from text input.
    /// </summary>
    /// <param name="text">Text to analyze for commands.</param>
    /// <returns>List of recognized command matches.</returns>
    public Task<List<CommandMatchResult>> GetCommandsFromTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Task.FromResult(new List<CommandMatchResult>());
        return Task.FromResult(_commandMatcher.FindBestMatchingCommand(text, _availableCommands));
    }

    /// <summary>
    /// Recognizes commands from audio clip.
    /// </summary>
    /// <param name="clip">Audio clip to analyze.</param>
    /// <returns>List of recognized command matches.</returns>
    public async Task<List<CommandMatchResult>> GetCommandsFromAudioAsync(AudioClip clip)
    {
        var result = await _whisperWrapper.GetTextAsync(clip, _params);
        if (string.IsNullOrEmpty(result?.Result))
            return new List<CommandMatchResult>();

        return await GetCommandsFromTextAsync(result.Result);
    }

    /// <summary>
    /// Recognizes commands from raw audio samples.
    /// </summary>
    /// <param name="samples">Audio samples to analyze.</param>
    /// <param name="frequency">Sample rate of the audio.</param>
    /// <param name="channels">Number of audio channels.</param>
    /// <returns>List of recognized command matches.</returns>
    public async Task<List<CommandMatchResult>> GetCommandsFromAudioAsync(float[] samples, int frequency, int channels)
    {
        var result = await _whisperWrapper.GetTextAsync(samples, frequency, channels, _params);
        if (string.IsNullOrEmpty(result?.Result))
            return new List<CommandMatchResult>();

        return await GetCommandsFromTextAsync(result.Result);
    }

    /// <summary>
    /// Handles audio chunks from the microphone.
    /// </summary>
    private async void OnAudioChunkReady(AudioChunk chunk)
    {
        if (!_isRunning) return;

        string chunkHash = ComputeChunkHash(chunk.Data);
        
        if (_processedChunks.Contains(chunkHash))
        {
            LogUtils.Log("Duplicate chunk detected, skipping.");
            return;
        }

        if (chunk.IsVoiceDetected)
        {
            _lastVoiceDetectedTime = DateTime.Now;
            _audioBuffer.AddRange(chunk.Data);
            _processedChunks.Add(chunkHash);
            
            Interlocked.Increment(ref _pendingChunks);
            
            if (!_isProcessing)
            {
                await ProcessAudioBufferSafe();
            }
            else
            {
                LogUtils.Log($"Processing in progress, queuing chunk (pending: {_pendingChunks})");
            }
        }
        else
        {
            if ((DateTime.Now - _lastVoiceDetectedTime).TotalSeconds > _silenceTimeout)
            {
                if (_isProcessing)
                {
                    LogUtils.Log("Waiting for processing to finish before clearing...");
                    await _processingLock.WaitAsync();
                    _processingLock.Release();
                }
                
                if (!string.IsNullOrEmpty(_currentTranscription.Trim()))
                {
                    await ProcessAudioBufferSafe();
                }
                
                ClearAudioState();
            }
        }

        if ((DateTime.Now - _lastBufferProcessedTime).TotalSeconds > _maxBufferAge && 
            !string.IsNullOrEmpty(_currentTranscription))
        {
            if (!_commandMatcher.ContainsCommandPrefix(_currentTranscription, _availableCommands))
            {
                LogUtils.Log("No activity buffer cleanup!");
                ClearAudioState();
            }
        }
    }

    /// <summary>
    /// NUEVO: Calcula hash de un chunk de forma más eficiente
    /// </summary>
    private string ComputeChunkHash(float[] data)
    {
        using (var sha256 = SHA256.Create())
        {
            byte[] dataBytes = new byte[data.Length * 4];
            Buffer.BlockCopy(data, 0, dataBytes, 0, dataBytes.Length);
            byte[] hashBytes = sha256.ComputeHash(dataBytes);
            return BitConverter.ToString(hashBytes);
        }
    }

    /// <summary>
    /// NUEVO: Wrapper seguro para ProcessAudioBuffer con control de concurrencia
    /// </summary>
    private async Task ProcessAudioBufferSafe()
    {
        // Intentar adquirir el lock sin bloquear
        bool lockAcquired = await _processingLock.WaitAsync(0);
        
        if (!lockAcquired)
        {
            LogUtils.Log("ProcessAudioBuffer already running, skipping duplicate call");
            return;
        }

        try
        {
            _isProcessing = true;
            await ProcessAudioBuffer();
        }
        finally
        {
            _isProcessing = false;
            _pendingChunks = 0; // Reset contador
            _processingLock.Release();
        }
    }

    /// <summary>
    /// Limpia todo el estado de audio de forma centralizada
    /// </summary>
    private void ClearAudioState()
    {
        _audioBuffer.Clear();
        _currentTranscription = "";
        _processedChunks.Clear();
        _lastProcessedTranscription = "";
        _pendingChunks = 0;
    }

    /// <summary>
    /// Processes the audio buffer to recognize commands.
    /// </summary>
    private async Task ProcessAudioBuffer()
    {
        try
        {
            if (_audioBuffer == null || _audioBuffer.Count == 0)
            {
                LogUtils.Warning("No audio data to process");
                return;
            }

            float audioDuration = _audioBuffer.Count / (float)_sampleRate / _channels;

            LogUtils.Log($"Processing audio buffer: {audioDuration:F2}s, {_audioBuffer.Count} samples");

            float[] audioData = _audioBuffer.ToArray();
            
            if (audioDuration < 1.0f)
            {
                int paddingSamples = (int)(0.5f * _sampleRate * _channels);
                float[] paddedAudio = new float[audioData.Length + paddingSamples];
                Array.Copy(audioData, paddedAudio, audioData.Length);
                audioData = paddedAudio;
                LogUtils.Log($"Added {paddingSamples} samples of padding to short audio ({audioDuration:F2}s)");
            }

            var result = await _whisperWrapper.GetTextAsync(
                audioData,
                _sampleRate,
                _channels,
                _params
            );

            if (!string.IsNullOrEmpty(result?.Result))
            {
                string newText = result.Result.Trim();
                LogUtils.Log($"Transcription result: '{newText}'");

                if (newText != _lastProcessedTranscription && !string.IsNullOrWhiteSpace(newText))
                {
                    _currentTranscription = newText;
                    _lastBufferProcessedTime = DateTime.Now;
                    _lastProcessedTranscription = newText;
                    
                    bool commandRecognized = RecognizeCommandsInTranscription();
                    
                    if (!commandRecognized)
                    {
                        LogUtils.Log("No command recognized, keeping buffer for accumulation");
                    }
                }
                else if (newText == _lastProcessedTranscription)
                {
                    LogUtils.Log("Same transcription as last time, clearing buffer to avoid repetition");
                    _audioBuffer.Clear();
                }
                else
                {
                    LogUtils.Log("Empty or whitespace transcription, ignoring");
                }
            }
            else
            {
                LogUtils.Log("Whisper returned empty result");
            }
        }
        catch (Exception ex)
        {
            LogUtils.Error($"Error processing audio: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Attempts to recognize commands in the accumulated transcription.
    /// </summary>
    /// <returns>True if a command was recognized and processed</returns>
    private bool RecognizeCommandsInTranscription()
    {
        if (string.IsNullOrWhiteSpace(_currentTranscription))
            return false;

        var commandResults = _commandMatcher.FindBestMatchingCommand(_currentTranscription, _availableCommands);
        if (commandResults.Count > 0)
        {
            var bestCommand = commandResults.OrderByDescending(c => c.similarityScore).First();
            
            LogUtils.Log($"Best match: '{bestCommand.command}' with score {bestCommand.similarityScore:F2} (threshold: {_commandMatcher.lcsSimilarityThreshold * 0.8f:F2})");
            
            if (bestCommand.similarityScore >= _commandMatcher.lcsSimilarityThreshold * 0.8f)
            {
                bool isDifferentCommand = !bestCommand.command.Equals(_lastProcessedTranscription, StringComparison.OrdinalIgnoreCase);
                bool enoughTimePassed = (DateTime.Now - _lastCommandTime).TotalSeconds >= MinTimeBetweenCommandsSec;
                
                if (isDifferentCommand || enoughTimePassed)
                {
                    LogUtils.Log($"✓ Command recognized: '{bestCommand.command}' (score: {bestCommand.similarityScore:F2})");
                    
                    OnCommandRecognized?.Invoke(bestCommand);
                    _lastCommandTime = DateTime.Now;
                    
                    ClearAudioState();
                    
                    return true;
                }
                else
                {
                    LogUtils.Log($"Command '{bestCommand.command}' repeated too soon or is duplicate, ignoring.");
                    ClearAudioState();
                    return true;
                }
            }
        }
        
        return false;
    }

    /// <summary>
    /// Update whisper parameters.
    /// </summary>
    public void UpdateParams(WhisperParams wparams)
    {
        _params = wparams;
    }
    
    /// <summary>
    /// Update available commands.
    /// </summary>
    public void UpdateCommands(List<string> availableCommands)
    {
        _availableCommands = availableCommands;
    }
}