// ApiManager.cs
using Godot;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HttpClient = System.Net.Http.HttpClient;

public partial class Apiservice : Node
{
    private readonly Dictionary<string, string> personas = new Dictionary<string, string>
    {
        {
            "Simple English Tutor",
            @"You are Jenny, a friendly, patient, and encouraging English tutor. Your primary goal is to have a simple, confidence-building conversation with a beginner. You will be given the entire conversation history with every turn.

            **Your Core Principles:**
            1.  **Use the History:** Pay close attention to the conversation history. Do not repeat questions or topics. Refer to things the user has said previously to make the conversation feel connected.
            2.  **Keep it Simple:** Always use simple words and short sentences. Avoid complex grammar, idioms, or jargon.
            3.  **Gently Correct:** If the user makes a clear and simple grammatical mistake (e.g., 'I go to store yesterday'), gently correct it in your response. Do not lecture them. For example: 'That's great! When I went to the store yesterday, I bought some apples. What did you see?'
            4.  **Stay on Topic (Mostly):** Try to continue the user's topic. If they talk about food, ask another question about food. Only change the topic if the conversation naturally ends.
            5.  **Always Encourage:** End your responses with a positive comment and a simple, open-ended question to keep the conversation going.

            **Example Interaction:**
            User: 'I like play game.'
            Your Response: 'Me too! Playing games is a lot of fun. What is your favorite game to play?'"
        },
        {
            "IELTS Exam Tutor",
            @"You are Jenny, an expert IELTS (International English Language Testing System) examiner preparing a student for the academic speaking test. Your tone is professional, structured, and helpful. You will guide the user through different practice modes.

            **Your Main Loop:**
            1.  On your very first turn, greet the user, introduce yourself as Jenny, and ask them what they would like to practice today: **Speaking, Vocabulary, or Listening**.
            2.  After completing any practice task, you must always return to this main loop by asking: **'What would you like to practice next: Speaking, Vocabulary, or Listening?'**

            **--- Practice Modes ---**

            ### Speaking Practice Mode
            When the user says they want to practice **Speaking**:
            1.  Acknowledge their choice.
            2.  Give them one complex, open-ended discussion topic similar to those in IELTS Part 3 (e.g., 'Discuss the effects of globalization on local cultures.').
            3.  Wait for their spoken response.
            4.  After they respond, you **must** provide a detailed evaluation of their answer. The evaluation must include a rating from 1 to 9 for each of the following official IELTS criteria:
                *   **Fluency and Coherence:** (How smoothly they spoke and connected their ideas).
                *   **Lexical Resource:** (Their use of vocabulary).
                *   **Grammatical Range and Accuracy:** (Their use of sentence structures and grammar).
            5.  Conclude by returning to the **Main Loop**.

            ### Vocabulary & Grammar Practice Mode
            When the user says they want to practice **Vocabulary, Words, or Grammar**:
            1.  Acknowledge their choice.
            2.  Provide a list of **10 random academic words** that frequently appear on the IELTS exam.
            3.  For each word, you **must** provide a simple definition and a clear example sentence.
            4.  Conclude by asking the user to try using one of the words in a sentence, and then return to the **Main Loop**.

            ### Listening Practice Mode
            **Important:** As a text-based AI, you cannot create audio. When the user says they want to practice **Listening**, you will simulate a listening test using text.
            1.  Acknowledge their choice and explain that you will provide a written transcript of a short academic lecture.
            2.  Provide a short paragraph (3-5 sentences) of an academic-style text. This text **must** have 3-5 words missing, indicated by numbered blanks like **[BLANK 1]**, **[BLANK 2]**, etc.
            3.  After providing the transcript, ask the user to fill in the blanks.
            4.  Wait for their response.
            5.  After they respond, reveal the correct words for the blanks and briefly explain why they fit the context.
            6.  Conclude by returning to the **Main Loop**."
        }
    };

    private static readonly HttpClient client = new HttpClient();

    private const string OpenRouterUrl = "https://openrouter.ai/api/v1/chat/completions";
    private const string LocalLlmUrl = "http://localhost:11434/v1/chat/completions";
    private const string LocalSttUrl = "http://localhost:8080/inference";

    private string ollamaModelName;
    private string piperPath;
    private string piperModelPath;
    private string whisperPath;
    private string whisperModelPath;

    private string openRouterApiKey;
    private string AzureSpeechKey;
    private string AzureSpeechRegion;

    private bool useLocalLlm = false;
    private bool useLocalStt = false;
    private bool useLocalTts = false;
    private bool canUseRemoteSpeech = false;
    private bool canUseRemoteLlm = false;

    private Process whisperServerProcess;

    public float PiperSpeed { get; set; } = 1.0f;

    private List<Dictionary<string, string>> messageHistory;

    public IEnumerable<string> PersonaNames => personas.Keys;
    private string currentSystemPrompt;

    public override void _Ready()
    {
        ReloadSettings();
        GetTree().Root.CloseRequested += OnAppQuitting;
    }

    public void ReloadSettings()
    {
        StopWhisperServer();
        var config = new ConfigFile();
        if (config.Load("user://api_keys.cfg") != Error.Ok)
        {
            GD.Print("Settings file not found. Using local-only defaults.");
        }

        // --- Load all settings from the file ---
        ollamaModelName = config.GetValue("local_paths", "ollama_model", "").ToString();
        piperPath = config.GetValue("local_paths", "piper_path", "").ToString();
        piperModelPath = config.GetValue("local_paths", "piper_model_path", "").ToString();
        whisperPath = config.GetValue("local_paths", "whisper_path", "").ToString();
        whisperModelPath = config.GetValue("local_paths", "whisper_model_path", "").ToString();
        this.PiperSpeed = config.GetValue("local_paths", "piper_speed", 1.0f).AsSingle();

        openRouterApiKey = config.GetValue("api_keys", "openrouter_key", "").ToString();
        AzureSpeechKey = config.GetValue("api_keys", "azure_speech_key", "").ToString();
        AzureSpeechRegion = config.GetValue("api_keys", "azure_speech_region", "").ToString();

        // --- Determine final logic ---
        canUseRemoteSpeech = !string.IsNullOrWhiteSpace(AzureSpeechKey) && !string.IsNullOrWhiteSpace(AzureSpeechRegion);
        canUseRemoteLlm = !string.IsNullOrWhiteSpace(openRouterApiKey);
        useLocalLlm = !string.IsNullOrWhiteSpace(ollamaModelName);
        useLocalTts = !string.IsNullOrWhiteSpace(piperPath) && !string.IsNullOrWhiteSpace(piperModelPath);
        useLocalStt = !string.IsNullOrWhiteSpace(whisperPath) && !string.IsNullOrEmpty(whisperModelPath);

        string selectedPersonaName = config.GetValue("user_settings", "ai_persona", "Simple English Tutor").ToString();
        if (!personas.TryGetValue(selectedPersonaName, out currentSystemPrompt))
        {
            personas.TryGetValue("Simple English Tutor", out currentSystemPrompt);
        }
        this.messageHistory = new List<Dictionary<string, string>>();

        if (useLocalLlm || !canUseRemoteLlm)
        {
            GD.Print("LLM Mode: Local (Ollama)");
            useLocalLlm = true; // Ensure it's true if keys are missing
            client.DefaultRequestHeaders.Authorization = null;
        }
        else
        {
            GD.Print("LLM Mode: Remote (OpenRouter)");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openRouterApiKey);
            client.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost");
            client.DefaultRequestHeaders.Add("X-Title", "Godot AI Chatbot");
        }

        if (useLocalStt && !string.IsNullOrWhiteSpace(whisperPath) && !string.IsNullOrWhiteSpace(whisperModelPath))
        {
            StartWhisperServer();
        }

        GD.Print($"STT Mode: {(useLocalStt ? "Local (Whisper)" : "Remote (Azure)")}");
        GD.Print($"TTS Mode: {(useLocalTts ? "Local (Piper)" : "Remote (Azure)")}");
    }

    private void StartWhisperServer()
    {
        if (whisperServerProcess != null && !whisperServerProcess.HasExited)
        {
            GD.Print("Whisper server is already running. Skipping start.");
            return;
        }
        GD.Print("Attempting to auto-start local Whisper server...");
        try
        {
            whisperServerProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = whisperPath,
                    Arguments = $"--model \"{whisperModelPath}\" --host 0.0.0.0 --port 8080",
                    UseShellExecute = false,
                    CreateNoWindow = true, // Run completely in the background
                    RedirectStandardOutput = true, // Capture output to prevent blocking
                    RedirectStandardError = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(whisperPath)
                }
            };
            whisperServerProcess.Start();
            GD.Print($"Whisper server process started with ID: {whisperServerProcess.Id}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"Failed to start Whisper server process: {e.Message}");
            whisperServerProcess = null;
        }
    }

    private void StopWhisperServer()
    {
        if (whisperServerProcess != null && !whisperServerProcess.HasExited)
        {
            GD.Print($"Shutting down old Whisper server (PID: {whisperServerProcess.Id})...");
            try
            {
                whisperServerProcess.Kill(true); // Kill the entire process tree
                whisperServerProcess.Dispose();
            }
            catch (Exception e)
            {
                GD.PrintErr($"Exception while trying to kill Whisper server: {e.Message}");
            }
            finally
            {
                whisperServerProcess = null; // CRITICAL: Set to null to indicate it's gone.
            }
        }
    }

    private void OnAppQuitting()
    {
        StopWhisperServer();
    }

    public async Task<string> GetChatGPTResponse(string promptText)
    {
        messageHistory.Add(new Dictionary<string, string> { { "role", "user" }, { "content", promptText } });

        string responseText;
        if (useLocalLlm)
        {
            responseText = await GetOllamaResponse();
        }
        else if (canUseRemoteLlm)
        {
            responseText = await GetOpenRouterResponse();
        }
        else
        {
            responseText = "Error: No LLM has been configured.";
        }

        if (!string.IsNullOrWhiteSpace(responseText) && !responseText.StartsWith("Error:"))
        {
            messageHistory.Add(new Dictionary<string, string> { { "role", "assistant" }, { "content", responseText } });
        }

        return responseText;
    }

    public async Task<AudioStream> SynthesizeSpeech(string text)
    {
        // Check if the user wants to use the local service.
        if (useLocalTts)
        {
            GD.Print("Routing to: Local TTS (Piper)");
            return await SynthesizeSpeechLocal(text);
        }
        // If not, check if we are ABLE to use the remote service.
        else if (canUseRemoteSpeech)
        {
            GD.Print("Routing to: Remote TTS (Azure)");
            return await SynthesizeSpeechAzure(text);
        }

        // If neither of the above is true, we have no valid TTS service.
        GD.PrintErr("SynthesizeSpeech called, but no valid TTS service is configured.");
        return null; // Return null to prevent a crash.
    }

    public async Task<string> TranscribeSpeech(AudioStreamWav recording)
    {
        // Check if the user wants to use the local service.
        if (useLocalStt)
        {
            GD.Print("Routing to: Local STT (Whisper)");
            return await TranscribeSpeechLocal(recording);
        }
        // If not, check if we are ABLE to use the remote service.
        else if (canUseRemoteSpeech)
        {
            GD.Print("Routing to: Remote STT (Azure)");
            return await TranscribeSpeechAzure(recording);
        }

        // If neither is true, we have no valid STT service.
        GD.PrintErr("TranscribeSpeech called, but no valid STT service is configured.");
        return "Error: Speech-to-Text service is not configured.";
    }

    public async Task<string> GetOpenRouterResponse()
    {
        var messagesToSend = new List<object>
        {
            new { role = "system", content = this.currentSystemPrompt }
        };
        messagesToSend.AddRange(this.messageHistory);
        var requestBody = new
        {
            model = ollamaModelName, // The free model you specified
            messages = messagesToSend,
        };
        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        try
        {
            HttpResponseMessage response = await client.PostAsync(OpenRouterUrl, content);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();

            using (JsonDocument doc = JsonDocument.Parse(responseBody))
            {
                var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                //GD.Print("AI Tutor says: ", message);
                return message;
            }
        }
        catch (HttpRequestException e)
        {
            GD.PrintErr($"Error calling OpenRouter. This is a network or server-side issue.");
            GD.PrintErr($"Status Code: {e.StatusCode}"); // This will show codes like 429, 500, etc.
            GD.PrintErr($"Full Error Message: {e.Message}");
            return "Sorry, I had a problem thinking of a response.";
        }
    }

    private async Task<string> GetOllamaResponse()
    {
        GD.Print("Requesting response from local Ollama model...");
        var messagesToSend = new List<object>
        {
            new { role = "system", content = this.currentSystemPrompt }
        };
        messagesToSend.AddRange(this.messageHistory);
        var requestBody = new
        {
            model = ollamaModelName,
            messages = messagesToSend,
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        try
        {
            HttpResponseMessage response = await client.PostAsync(LocalLlmUrl, content);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            using (JsonDocument doc = JsonDocument.Parse(responseBody))
            {
                return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            }
        }
        catch (HttpRequestException e)
        {
            GD.PrintErr($"Error calling local Ollama: {e.Message}");
            GD.PrintErr("Is the Ollama server running? Run 'ollama run llama3:8b' in your terminal.");
            return "Sorry, I couldn't connect to my local brain. Is Ollama running?";
        }
    }

    private async Task<string> TranscribeSpeechLocal(AudioStreamWav recording)
    {
        GD.Print("Transcribing with local Whisper server...");

        // Define a temporary path in the user:// directory.
        string tempWavPath = "user://temp_stt_audio.wav";

        // Save the recording to this path. This method adds the crucial WAV header.
        Error err = recording.SaveToWav(tempWavPath);
        if (err != Error.Ok)
        {
            GD.PrintErr("Failed to save temporary WAV file for STT.");
            return "Error: Could not prepare audio for transcription.";
        }

        // Read the complete, correct WAV data back from the file.
        var file = FileAccess.Open(tempWavPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr("Failed to read back temporary WAV file.");
            return "Error: Could not read audio for transcription.";
        }
        var completeWavData = file.GetBuffer((long)file.GetLength());
        file.Close();

        // Send THIS data to the Whisper server.
        using var formData = new MultipartFormDataContent();
        formData.Add(new ByteArrayContent(completeWavData), "file", "audio.wav");

        try
        {
            HttpResponseMessage response = await client.PostAsync(LocalSttUrl, formData);
            if (!response.IsSuccessStatusCode)
            {
                GD.PrintErr($"Whisper server returned error: {response.StatusCode}");
                return "Error: Whisper server connection failed.";
            }
            string responseBody = await response.Content.ReadAsStringAsync();
            using (JsonDocument doc = JsonDocument.Parse(responseBody))
            {
                return doc.RootElement.GetProperty("text").GetString().Trim();
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"Error calling local Whisper: {e.Message}. Is the server running?");
            return "Error: Could not connect to local Whisper.";
        }
    }

    private async Task<AudioStream> SynthesizeSpeechLocal(string text)
    {
        if (string.IsNullOrWhiteSpace(piperPath) || string.IsNullOrWhiteSpace(piperModelPath))
        {
            GD.PrintErr("SynthesizeSpeechLocal called, but Piper paths are not configured.");
            return null;
        }

        GD.Print("Synthesizing with local Piper (Deadlock-proof method)...");

        string godotOutputPath = "user://temp_voice.wav";
        string absoluteOutputPath = ProjectSettings.GlobalizePath(godotOutputPath);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = piperPath, // We can run piper.exe directly
                Arguments = $"--model \"{piperModelPath}\" --input-file - --length-scale {this.PiperSpeed} --output_file \"{absoluteOutputPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }
        };

        process.Start();

        // Asynchronously start the task to read the entire error stream.
        //    We don't 'await' it yet. We just tell it to start listening.
        Task<string> errorTask = process.StandardError.ReadToEndAsync();

        // Now, write the text to Piper's input stream.
        using (var streamWriter = process.StandardInput)
        {
            await streamWriter.WriteAsync(text);
        } // The 'using' block closes the stream, signaling to Piper that we're done sending text.

        // Now, wait for the Piper process to finish its work and exit.
        await process.WaitForExitAsync();

        // Finally, get the result from our listening task.
        string errorOutput = await errorTask;
        // --- The rest of the method is the same ---

        //if (!string.IsNullOrWhiteSpace(errorOutput))
        //{
        //    GD.PrintErr($"Piper process returned an error: {errorOutput}");
        //    return null;
        //}
        if (!FileAccess.FileExists(godotOutputPath))
        {
            GD.PrintErr("Piper process finished, but the output WAV file was not created.");
            return null;
        }
        var outputFile = FileAccess.Open(godotOutputPath, FileAccess.ModeFlags.Read);
        if (outputFile == null) return null;
        var audioData = outputFile.GetBuffer((long)outputFile.GetLength());
        outputFile.Close();

        var audioStream = new AudioStreamWav();
        audioStream.Data = audioData;
        audioStream.Format = AudioStreamWav.FormatEnum.Format16Bits;
        audioStream.MixRate = 22050;
        audioStream.Stereo = false;

        return audioStream;
    }

    public async Task<AudioStream> SynthesizeSpeechAzure(string text)
    {
        // --- NEW SAFETY CHECK ---
        // If we don't have valid Azure keys, don't even try to call the SDK.
        if (!canUseRemoteSpeech)
        {
            GD.PrintErr("SynthesizeSpeech called, but remote speech is disabled. Check Azure keys.");
            return null; // Return null to prevent a crash.
        }

        var speechConfig = SpeechConfig.FromSubscription(AzureSpeechKey, AzureSpeechRegion);
        speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Riff24Khz16BitMonoPcm);
        using (var synthesizer = new SpeechSynthesizer(speechConfig, null))
        {
            var result = await synthesizer.SpeakTextAsync(text);
            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                var audioData = result.AudioData;
                var audioStream = new AudioStreamWav();
                audioStream.Data = audioData;
                audioStream.Format = AudioStreamWav.FormatEnum.Format16Bits;
                audioStream.MixRate = 24000;
                audioStream.Stereo = false;
                return audioStream;
            }
        }
        return null;
    }

    public async Task<string> TranscribeSpeechAzure(AudioStreamWav recording)
    {
        if (!canUseRemoteSpeech)
        {
            GD.PrintErr("TranscribeSpeech called, but remote speech is disabled. Check Azure keys.");
            return "Error: Speech services are not configured."; // Return an error message.
        }
        byte[] audioData = recording.Data;
        bool isStereo = recording.Stereo;
        int mixRate = recording.MixRate;
        var speechConfig = SpeechConfig.FromSubscription(AzureSpeechKey, AzureSpeechRegion);
        speechConfig.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, "5000");

        // --- CORRECTED VARIABLE TYPES ---
        uint samplesPerSecond = (uint)mixRate;
        // Azure SDK requires byte for these parameters.
        byte bitsPerSample = 16;
        byte channelCount = (byte)(isStereo ? 2 : 1);

        // This call will now succeed because the types match the method's signature.
        var audioFormat = AudioStreamFormat.GetWaveFormatPCM(samplesPerSecond, bitsPerSample, channelCount);

        using (var pushStream = AudioInputStream.CreatePushStream(audioFormat))
        {
            pushStream.Write(audioData);
            pushStream.Close();

            using (var audioConfig = AudioConfig.FromStreamInput(pushStream))
            using (var recognizer = new SpeechRecognizer(speechConfig, audioConfig))
            {
                //GD.Print("Transcribing speech from user (with correct format)...");
                var result = await recognizer.RecognizeOnceAsync();

                if (result.Reason == ResultReason.RecognizedSpeech)
                {
                    //GD.Print($"Transcription successful: '{result.Text}'");
                    return result.Text;
                }
                else if (result.Reason == ResultReason.NoMatch)
                {
                    GD.PrintErr($"Azure could not recognize speech. Reason: {result.Reason}");
                    // Let's get more details if possible
                    var noMatchDetails = NoMatchDetails.FromResult(result);
                    GD.PrintErr($"NoMatchDetails: {noMatchDetails.Reason}");
                    return string.Empty;
                }
                else if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = CancellationDetails.FromResult(result);
                    GD.PrintErr($"Speech Recognition canceled. Reason: {cancellation.Reason}");
                    if (cancellation.Reason == CancellationReason.Error)
                    {
                        GD.PrintErr($"CancellationErrorCode={cancellation.ErrorCode}");
                        GD.PrintErr($"CancellationErrorDetails={cancellation.ErrorDetails}");
                    }
                    return string.Empty;
                }
            }
        }
        return string.Empty;
    }
}