using Godot;
using System;

public partial class SettingsPanel : PanelContainer
{
    private LineEdit openRouterKeyLineEdit;
    private LineEdit azureKeyLineEdit;
    private LineEdit azureRegionLineEdit;
    private Button saveButton;
    private LineEdit whisperPathLineEdit;
    private LineEdit whisperModelLineEdit;
    private LineEdit ollamaModelLineEdit;
    private LineEdit piperPathLineEdit;
    private LineEdit piperModelLineEdit;
    private OptionButton personaOptionButton;

    private Apiservice apiManager;

    // Define the path for our saved keys file. user:// is the best place for this.
    public const string ApiKeysFilePath = "user://api_keys.cfg";

    public override void _Ready()
    {
        apiManager = GetNode<Apiservice>("/root/Apiservice");
        // Get references to our UI nodes
        openRouterKeyLineEdit = GetNode<LineEdit>("VBoxContainer/OpenRouterKeyLineEdit");
        azureKeyLineEdit = GetNode<LineEdit>("VBoxContainer/AzureKeyLineEdit");
        azureRegionLineEdit = GetNode<LineEdit>("VBoxContainer/AzureRegionLineEdit");
        saveButton = GetNode<Button>("VBoxContainer/SaveButton");
        whisperPathLineEdit = GetNode<LineEdit>("VBoxContainer/WhisperPathLineEdit");
        whisperModelLineEdit = GetNode<LineEdit>("VBoxContainer/WhisperModelLineEdit");
        ollamaModelLineEdit = GetNode<LineEdit>("VBoxContainer/OllamaModelLineEdit");
        piperPathLineEdit = GetNode<LineEdit>("VBoxContainer/PiperPathLineEdit");
        piperModelLineEdit = GetNode<LineEdit>("VBoxContainer/PiperModelLineEdit");
        personaOptionButton = GetNode<OptionButton>("VBoxContainer/PersonaOptionButton");


        PopulatePersonas();
        // Connect the button's pressed signal to our save method
        saveButton.Pressed += OnSaveButtonPressed;

        // Load any existing keys when the panel opens
        LoadKeys();
    }

    private void PopulatePersonas()
    {
        personaOptionButton.Clear();
        foreach (string personaName in apiManager.PersonaNames)
        {
            personaOptionButton.AddItem(personaName);
        }
    }

    private void OnSaveButtonPressed()
    {
        var config = new ConfigFile();
        if (personaOptionButton.Selected >= 0)
        {
            string selectedPersona = personaOptionButton.GetItemText(personaOptionButton.Selected);
            config.SetValue("user_settings", "ai_persona", selectedPersona);
        }

        // Get the text from the LineEdits and save it
        config.SetValue("local_paths", "ollama_model", ollamaModelLineEdit.Text);
        config.SetValue("local_paths", "piper_path", piperPathLineEdit.Text);
        config.SetValue("local_paths", "piper_model_path", piperModelLineEdit.Text);
        config.SetValue("local_paths", "whisper_path", whisperPathLineEdit.Text);
        config.SetValue("local_paths", "whisper_model_path", whisperModelLineEdit.Text);

        config.SetValue("api_keys", "openrouter_key", openRouterKeyLineEdit.Text);
        config.SetValue("api_keys", "azure_speech_key", azureKeyLineEdit.Text);
        config.SetValue("api_keys", "azure_speech_region", azureRegionLineEdit.Text);

        config.SetValue("local_paths", "piper_speed", apiManager.PiperSpeed);
        Error err = config.Save(ApiKeysFilePath);
        if (err != Error.Ok)
        {
            GD.PrintErr("Failed to save API keys!");
        }
        else
        {
            GD.Print("API keys saved successfully.");
        }

        config.Save(ApiKeysFilePath);
        apiManager.ReloadSettings();
        Hide();
    }

    private void LoadKeys()
    {
        var config = new ConfigFile();
        Error err = config.Load(ApiKeysFilePath);
        if (err != Error.Ok)
        {
            // This is normal if the user hasn't saved keys yet.
            GD.Print("No API key file found. A new one will be created on save.");
            return;
        }
        string savedPersona = config.GetValue("user_settings", "ai_persona", "Simple English Tutor").ToString();
        for (int i = 0; i < personaOptionButton.ItemCount; i++)
        {
            if (personaOptionButton.GetItemText(i) == savedPersona)
            {
                personaOptionButton.Select(i);
                break;
            }
        }

        bool prioritizeLocal = config.GetValue("user_settings", "prioritize_local", false).AsBool();
        ollamaModelLineEdit.Text = config.GetValue("local_paths", "ollama_model", "").ToString();
        piperPathLineEdit.Text = config.GetValue("local_paths", "piper_path", "").ToString();
        piperModelLineEdit.Text = config.GetValue("local_paths", "piper_model_path", "").ToString();
        whisperPathLineEdit.Text = config.GetValue("local_paths", "whisper_path", "").ToString();
        whisperModelLineEdit.Text = config.GetValue("local_paths", "whisper_model_path", "").ToString();

        openRouterKeyLineEdit.Text = config.GetValue("api_keys", "openrouter_key", "").ToString();
        azureKeyLineEdit.Text = config.GetValue("api_keys", "azure_speech_key", "").ToString();
        azureRegionLineEdit.Text = config.GetValue("api_keys", "azure_speech_region", "").ToString();
    }
}