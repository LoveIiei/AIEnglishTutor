using Godot;
using System;

public partial class MainWindow : Node
{
    // --- NEW: UI Node References ---
    private CheckButton historyCheckBox;
    private RichTextLabel historyLabel;

    private HSlider piperSpeedSlider;

    // Node references from before
    private Character character;
    private Apiservice apiManager;
    private AudioStreamPlayer audioPlayer;
    private Button talkButton;

    // For recording audio
    private AudioStreamWav recording;
    private AudioEffectRecord effectRecordResource;
    private bool isRecording = false;

    // For api settings
    private Button settingsButton;
    private SettingsPanel settingsPanel;

    public override void _Ready()
    {
        historyCheckBox = GetNode<CheckButton>("HistoryCheckButton");
        historyLabel = GetNode<RichTextLabel>("HistoryLabel");

        // Existing node references
        character = GetNode<Character>("Character");
        apiManager = GetNode<Apiservice>("/root/Apiservice");
        audioPlayer = GetNode<AudioStreamPlayer>("AudioStreamPlayer");
        talkButton = GetNode<Button>("TalkButton");

        piperSpeedSlider = GetNode<HSlider>("PiperSpeedSlider");
        piperSpeedSlider.Value = apiManager.PiperSpeed; // Set initial value after settings are loaded

        piperSpeedSlider.ValueChanged += OnPiperSpeedChanged;

        // Get the audio effect for recording
        effectRecordResource = AudioServer.GetBusEffect(1, 1) as AudioEffectRecord;
        if (effectRecordResource == null)
        {
            GD.PrintErr("Could not find the 'AudioEffectRecord' on the MicInput bus.");
            talkButton.Disabled = true;
            return;
        }

        historyCheckBox.Toggled += OnHistoryToggled;
        // Start with the history hidden
        historyLabel.Visible = false;

        // Connect the button signals
        talkButton.ButtonDown += OnTalkButtonDown;
        talkButton.ButtonUp += OnTalkButtonUp;

        audioPlayer.Finished += OnAudioPlayerFinished;

        settingsButton = GetNode<Button>("SettingsButton");
        settingsPanel = GetNode<SettingsPanel>("SettingsPanel");

        settingsButton.Pressed += OnSettingsButtonPressed;

        //// --- NEW: Check if keys are loaded and update UI ---
        //if (!apiManager.AreKeysLoaded)
        //{
        //    talkButton.Disabled = true;
        //    talkButton.Text = "Set API Keys in Settings";
        //}
    }

    private void OnAudioPlayerFinished()
    {
        // When the audio stops, set the character back to idle.
        character.PlayAnimation("idle");
    }

    private void OnHistoryToggled(bool isToggled)
    {
        historyLabel.Visible = isToggled;
    }

    private void AppendToHistory(string speaker, string message)
    {
        // We use BBCode for simple formatting, like bolding the speaker's name
        historyLabel.AppendText($"[b]{speaker}:[/b] {message}\n\n");
    }

    private void StartRecording()
    {
        if (isRecording || effectRecordResource == null) return;
        isRecording = true;
        talkButton.Text = "Listening...";
        effectRecordResource.SetRecordingActive(true);
    }

    private void StopRecording()
    {
        if (!isRecording || effectRecordResource == null) return;
        isRecording = false;
        talkButton.Text = "Hold to Talk";
        effectRecordResource.SetRecordingActive(false);
        recording = effectRecordResource.GetRecording();
    }

    private void OnTalkButtonDown()
    {
        audioPlayer.Stop();

        StartRecording();
        character.PlayAnimation("listening");
    }

    private async void OnTalkButtonUp()
    {
        StopRecording();

        if (recording == null || recording.Data.Length == 0)
        {
            GD.PrintErr("Recording was empty.");
            return;
        }

        talkButton.Disabled = true;
        talkButton.Text = "Thinking...";

        // Transcribe the user's speech using the new router.
        string userText = await apiManager.TranscribeSpeech(recording);

        if (string.IsNullOrWhiteSpace(userText) || userText.StartsWith("Error:"))
        {
            // If it's an error, display it in the history so the user knows what went wrong.
            if (!string.IsNullOrWhiteSpace(userText))
            {
                AppendToHistory("System", userText);
            }

            talkButton.Disabled = false;
            talkButton.Text = "Try Again";
            // Stop the method here. Do not proceed to the LLM.
            return;
        }

        // If we get here, transcription was successful.
        AppendToHistory("You", userText);

        // Get a response from the AI model (LLM).
        string gptResponse = await apiManager.GetChatGPTResponse(userText);
        if (string.IsNullOrWhiteSpace(gptResponse))
        {
            talkButton.Disabled = false;
            talkButton.Text = "Error";
            return;
        }

        AppendToHistory("Jenny", gptResponse);

        // Synthesize the AI's response into speech.
        talkButton.Text = "Speaking...";
        AudioStream synthesizedAudio = await apiManager.SynthesizeSpeech(gptResponse);
        if (synthesizedAudio != null)
        {
            GD.Print("Playing audio");
            audioPlayer.Stop();
            audioPlayer.Stream = synthesizedAudio;
            audioPlayer.Play();
            character.PlayAnimation("talking");
        }

        talkButton.Disabled = false;
        talkButton.Text = "Hold to Talk";
    }

    private void OnSettingsButtonPressed()
    {
        settingsPanel.Show();
    }

    private void OnPiperSpeedChanged(double value)
    {
        // Update the public property on the ApiManager singleton.
        // The value is cast to a float to match the property's type.
        apiManager.PiperSpeed = (float)value;
    }
}