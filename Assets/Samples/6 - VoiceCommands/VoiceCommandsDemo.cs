using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using Whisper.Utils;
using Button = UnityEngine.UI.Button;
using Toggle = UnityEngine.UI.Toggle;

namespace Whisper.Samples
{
    /// <summary>
    /// Record audio clip from microphone and make a transcription.
    /// </summary>
    public class VoiceCommandsDemo : MonoBehaviour
    {
        public WhisperManager whisper;
        public MicrophoneRecord microphoneRecord;
        public bool streamSegments = true;
        public bool printLanguage = true;

        [Header("UI")]
        public Button startButton;
        public Button recordButton;
        public Text startButtonText;
        public Text recordButtonText;
        public Text outputText;
        public Text timeText;
        public Dropdown languageDropdown;
        public Toggle translateToggle;
        public ScrollRect scroll;

        private void Awake()
        {
            whisper.OnProgress += OnProgressHandler;
            whisper.OnCommandRecognized += OnCommandRecognized;

            microphoneRecord.OnRecordStop += OnRecordStop;

            startButton.onClick.AddListener(OnStartButtonPressed);
            recordButton.onClick.AddListener(OnRecordButtonPressed);

            languageDropdown.value = languageDropdown.options
                .FindIndex(op => op.text == whisper.language);
            languageDropdown.onValueChanged.AddListener(OnLanguageChanged);

            translateToggle.isOn = whisper.translateToEnglish;
            translateToggle.onValueChanged.AddListener(OnTranslateChanged);
        }

        private async void Start()
        {
            await whisper.InitCommandSystem(microphoneRecord);
        }

        private void OnVadChanged(bool vadStop)
        {
            microphoneRecord.vadStop = vadStop;
        }

        private void OnStartButtonPressed()
        {
            if (!microphoneRecord.IsRecording)
            {
                whisper.StartListeningCommands();
                startButtonText.text = "Stop";
                recordButton.interactable = false;
            }
            else
            {
                whisper.StopListeningCommands();
                startButtonText.text = "Start";
                recordButton.interactable = true;
            }
        }

        private void OnRecordButtonPressed()
        {
            if (!microphoneRecord.IsRecording)
            {
                microphoneRecord.StartRecord();
                recordButtonText.text = "Stop";
                startButton.interactable = false;
            }
            else
            {
                microphoneRecord.StopRecord();
                recordButtonText.text = "Record";
                outputText.text = "";
                UiUtils.ScrollDown(scroll);
                startButton.interactable = true;
            }
        }

        private async void OnRecordStop(AudioChunk recordedAudio)
        {
            var sw = new Stopwatch();
            sw.Start();

            var res = await whisper.GetCommandsFromTextAsync(recordedAudio.Data, recordedAudio.Frequency, recordedAudio.Channels);
            if (res == null || !outputText)
                return;

            var time = sw.ElapsedMilliseconds;
            var rate = recordedAudio.Length / (time * 0.001f);
            timeText.text = $"Time: {time} ms\nRate: {rate:F1}x";

            var text = "Command not recognized";
            if (res.Count > 0)
            {
                text = "";
                foreach (CommandMatchResult recognizedCommand in res)
                {
                    text += "- Recognized command: " + recognizedCommand.command + " ";
                }
            }

            outputText.text = text;
            UiUtils.ScrollDown(scroll);
        }

        private void OnLanguageChanged(int ind)
        {
            var opt = languageDropdown.options[ind];
            whisper.language = opt.text;
        }

        private void OnTranslateChanged(bool translate)
        {
            whisper.translateToEnglish = translate;
        }

        private void OnProgressHandler(int progress)
        {
            if (!timeText)
                return;
            timeText.text = $"Progress: {progress}%";
        }

        private void OnCommandRecognized(CommandMatchResult commandResult)
        {
            var text = "Command not recognized";
            if (commandResult != null)
            {
                text = "- Recognized command: " + commandResult.command;
            }
            outputText.text = text;
        }

    }
}