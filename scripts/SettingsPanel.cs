using Godot;
using System;

public partial class SettingsPanel : PanelContainer
{
    private LineEdit openRouterKeyLineEdit;
    private LineEdit azureKeyLineEdit;
    private LineEdit azureRegionLineEdit;
    private Button saveButton;
    private CheckButton useLocalCheckButton;

    // Define the path for our saved keys file. user:// is the best place for this.
    public const string ApiKeysFilePath = "user://api_keys.cfg";

    public override void _Ready()
    {
        // Get references to our UI nodes
        openRouterKeyLineEdit = GetNode<LineEdit>("VBoxContainer/OpenRouterKeyLineEdit");
        azureKeyLineEdit = GetNode<LineEdit>("VBoxContainer/AzureKeyLineEdit");
        azureRegionLineEdit = GetNode<LineEdit>("VBoxContainer/AzureRegionLineEdit");
        saveButton = GetNode<Button>("VBoxContainer/SaveButton");
        useLocalCheckButton = GetNode<CheckButton>("VBoxContainer/UseLocalCheckButton");

        // Connect the button's pressed signal to our save method
        saveButton.Pressed += OnSaveButtonPressed;

        // Load any existing keys when the panel opens
        LoadKeys();
    }

    private void OnSaveButtonPressed()
    {
        var config = new ConfigFile();

        // Get the text from the LineEdits and save it
        config.SetValue("user_settings", "prioritize_local", useLocalCheckButton.ButtonPressed);
        config.SetValue("api_keys", "openrouter_key", openRouterKeyLineEdit.Text);
        config.SetValue("api_keys", "azure_speech_key", azureKeyLineEdit.Text);
        config.SetValue("api_keys", "azure_speech_region", azureRegionLineEdit.Text);

        Error err = config.Save(ApiKeysFilePath);
        if (err != Error.Ok)
        {
            GD.PrintErr("Failed to save API keys!");
        }
        else
        {
            GD.Print("API keys saved successfully.");
        }

        // Hide the panel after saving
        Hide();
        // You might want to signal the main scene to reload the keys here if the app is already running.
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

        bool prioritizeLocal = config.GetValue("user_settings", "prioritize_local", false).AsBool();
        useLocalCheckButton.ButtonPressed = prioritizeLocal;

        openRouterKeyLineEdit.Text = config.GetValue("api_keys", "openrouter_key", "").ToString();
        azureKeyLineEdit.Text = config.GetValue("api_keys", "azure_speech_key", "").ToString();
        azureRegionLineEdit.Text = config.GetValue("api_keys", "azure_speech_region", "").ToString();
    }
}