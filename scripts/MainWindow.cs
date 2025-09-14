using Godot;
using System;

public partial class MainWindow : Node
{
    // --- NEW: UI Node References ---
    private CheckButton historyCheckBox;
    private RichTextLabel historyLabel;

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
        // --- NEW: Get references to the UI nodes ---
        historyCheckBox = GetNode<CheckButton>("HistoryCheckButton");
        historyLabel = GetNode<RichTextLabel>("HistoryLabel");

        // Existing node references
        character = GetNode<Character>("Character");
        apiManager = GetNode<Apiservice>("/root/Apiservice");
        audioPlayer = GetNode<AudioStreamPlayer>("AudioStreamPlayer");
        talkButton = GetNode<Button>("TalkButton");

        // Get the audio effect for recording
        effectRecordResource = AudioServer.GetBusEffect(1, 1) as AudioEffectRecord;
        if (effectRecordResource == null)
        {
            GD.PrintErr("Could not find the 'AudioEffectRecord' on the MicInput bus.");
            talkButton.Disabled = true;
            return;
        }

        // --- NEW: Connect the CheckBox's 'toggled' signal ---
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

    // --- NEW: A method to handle showing/hiding the history label ---
    private void OnHistoryToggled(bool isToggled)
    {
        historyLabel.Visible = isToggled;
    }

    // --- NEW: A helper function to add text to our history box ---
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

    // In MainWindow.cs (or MainScene.cs)

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

        // 1. Transcribe the user's speech using the new router.
        string userText = await apiManager.TranscribeSpeech(recording);

        // --- MODIFIED AND IMPROVED ERROR CHECK ---
        // We now check for two types of failure:
        // 1. A completely empty response (from Azure NoMatch).
        // 2. A string that starts with "Error:" (from our new router logic).
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

        // 2. Get a response from the AI model (LLM).
        string gptResponse = await apiManager.GetChatGPTResponse(userText);
        if (string.IsNullOrWhiteSpace(gptResponse))
        {
            talkButton.Disabled = false;
            talkButton.Text = "Error";
            return;
        }

        AppendToHistory("Jenny", gptResponse);

        // 3. Synthesize the AI's response into speech.
        talkButton.Text = "Speaking...";
        AudioStream synthesizedAudio = await apiManager.SynthesizeSpeech(gptResponse);
        if (synthesizedAudio != null)
        {
            GD.Print("Playing audio");
            audioPlayer.Stop();
            audioPlayer.Stream = synthesizedAudio;
            audioPlayer.Play();
            character.PlayAnimation("talking");
            // We NO LONGER wait here. The method finishes, and the audio plays.
            // The "Finished" signal will handle the animation change later.
        }

        talkButton.Disabled = false;
        talkButton.Text = "Hold to Talk";
    }

    private void OnSettingsButtonPressed()
    {
        settingsPanel.Show();
    }
}