// ApiManager.cs
using Godot;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System;
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
    private static readonly HttpClient client = new HttpClient();

    private const string OpenRouterUrl = "https://openrouter.ai/api/v1/chat/completions";
    private const string LocalLlmUrl = "http://localhost:11434/v1/chat/completions";
    private const string LocalSttUrl = "http://localhost:8080/inference";

    private string ollamaModelName;
    private string piperPath;
    private string piperModelPath;

    private string openRouterApiKey;
    private string AzureSpeechKey;
    private string AzureSpeechRegion;

    private bool useLocalLlm = false;
    private bool useLocalStt = false;
    private bool useLocalTts = false;
    private bool canUseRemoteSpeech = false;
    private bool canUseRemoteLlm = false;

    public override void _Ready()
    {
        var config = new ConfigFile();
        if (config.Load("user://api_keys.cfg") != Error.Ok)
        {
            GD.Print("Settings file not found. Using local-only defaults.");
        }

        // --- Load all settings from the file ---
        ollamaModelName = config.GetValue("local_paths", "ollama_model", "").ToString();
        piperPath = config.GetValue("local_paths", "piper_path", "").ToString();
        piperModelPath = config.GetValue("local_paths", "piper_model_path", "").ToString();
        useLocalStt = config.GetValue("user_settings", "use_local_stt", true).AsBool();

        openRouterApiKey = config.GetValue("api_keys", "openrouter_key", "").ToString();
        AzureSpeechKey = config.GetValue("api_keys", "azure_speech_key", "").ToString();
        AzureSpeechRegion = config.GetValue("api_keys", "azure_speech_region", "").ToString();

        // --- Determine final logic ---
        canUseRemoteSpeech = !string.IsNullOrWhiteSpace(AzureSpeechKey) && !string.IsNullOrWhiteSpace(AzureSpeechRegion);
        canUseRemoteLlm = !string.IsNullOrWhiteSpace(openRouterApiKey);
        useLocalLlm = !string.IsNullOrWhiteSpace(ollamaModelName);
        useLocalTts = !string.IsNullOrWhiteSpace(piperPath) && !string.IsNullOrWhiteSpace(piperModelPath);

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

        GD.Print($"STT Mode: {(useLocalStt ? "Local (Whisper)" : "Remote (Azure)")}");
        GD.Print($"TTS Mode: {(useLocalTts ? "Local (Piper)" : "Remote (Azure)")}");
    }

    public async Task<string> GetChatGPTResponse(string promptText)
    {
        // --- THIS IS THE CORE OF THE NEW LOGIC ---
        if (useLocalLlm) return await GetOllamaResponse(promptText);
        if (canUseRemoteLlm) return await GetOpenRouterResponse(promptText);
        return "Error: No LLM has been configured.";
    }

    public async Task<AudioStream> SynthesizeSpeech(string text)
    {
        // 1. Check if the user wants to use the local service.
        if (useLocalTts)
        {
            GD.Print("Routing to: Local TTS (Piper)");
            return await SynthesizeSpeechLocal(text);
        }
        // 2. If not, check if we are ABLE to use the remote service.
        else if (canUseRemoteSpeech)
        {
            GD.Print("Routing to: Remote TTS (Azure)");
            return await SynthesizeSpeechAzure(text);
        }

        // 3. If neither of the above is true, we have no valid TTS service.
        GD.PrintErr("SynthesizeSpeech called, but no valid TTS service is configured.");
        return null; // Return null to prevent a crash.
    }

    public async Task<string> TranscribeSpeech(AudioStreamWav recording)
    {
        // 1. Check if the user wants to use the local service.
        if (useLocalStt)
        {
            GD.Print("Routing to: Local STT (Whisper)");
            return await TranscribeSpeechLocal(recording);
        }
        // 2. If not, check if we are ABLE to use the remote service.
        else if (canUseRemoteSpeech)
        {
            GD.Print("Routing to: Remote STT (Azure)");
            return await TranscribeSpeechAzure(recording);
        }

        // 3. If neither is true, we have no valid STT service.
        GD.PrintErr("TranscribeSpeech called, but no valid STT service is configured.");
        return "Error: Speech-to-Text service is not configured.";
    }

    public async Task<string> GetOpenRouterResponse(string promptText)
    {
        const string systemPrompt = @"
You are Jenny, a friendly and encouraging English tutor. Your goal is to help beginners learn and feel confident.

Follow these rules for every response:
1.  **Be Welcoming:** Always start with a warm and friendly tone.
2.  **Keep it Simple:** Use simple words and short sentences that a beginner can easily understand. Do not use complex grammar or idioms.
3.  **Be Short:** Keep your answers brief and to the point.
4.  **Encourage Conversation:** After answering, always ask a simple question back to the user to keep them talking.
5.  **Change your level as needed:** If user wants to practice something difficult like IELTs, then use more advanced and appropriate words and sentences.

For example, if a user asks ""What means happy?"", a perfect answer would be: ""Happy means feeling good or pleased. For example, a sunny day makes me happy! What makes you happy?""";


        var requestBody = new
        {
            model = "gpt-oss:20b", // The free model you specified
            messages = new[] {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = promptText }
            }
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

    private async Task<string> GetOllamaResponse(string promptText)
    {
        GD.Print("Requesting response from local Ollama model...");
        const string systemPrompt = @"
You are Jenny, a friendly and encouraging English tutor. Your goal is to help beginners learn and feel confident.

Follow these rules for every response:
1.  **Be Welcoming:** Always start with a warm and friendly tone.
2.  **Keep it Simple:** Use simple words and short sentences that a beginner can easily understand. Do not use complex grammar or idioms.
3.  **Be Short:** Keep your answers brief and to the point.
4.  **Encourage Conversation:** After answering, always ask a simple question back to the user to keep them talking.
5.  **Change your level as needed:** If user wants to practice something difficult like IELTs, then use more advanced and appropriate words and sentences.

For example, if a user asks ""What means happy?"", a perfect answer would be: ""Happy means feeling good or pleased. For example, a sunny day makes me happy! What makes you happy?""";
        
        var requestBody = new
        {
            model = ollamaModelName,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = promptText }
            }
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

        // 1. Define a temporary path in the user:// directory.
        string tempWavPath = "user://temp_stt_audio.wav";

        // 2. Save the recording to this path. This method adds the crucial WAV header.
        Error err = recording.SaveToWav(tempWavPath);
        if (err != Error.Ok)
        {
            GD.PrintErr("Failed to save temporary WAV file for STT.");
            return "Error: Could not prepare audio for transcription.";
        }

        // 3. Read the complete, correct WAV data back from the file.
        var file = FileAccess.Open(tempWavPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr("Failed to read back temporary WAV file.");
            return "Error: Could not read audio for transcription.";
        }
        var completeWavData = file.GetBuffer((long)file.GetLength());
        file.Close();

        // 4. Send THIS data to the Whisper server.
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
                Arguments = $"--model \"{piperModelPath}\" --input-file - --output_file \"{absoluteOutputPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }
        };
        GD.Print("Process starting");

        process.Start();

        // --- THIS IS THE NEW, DEADLOCK-PROOF LOGIC ---

        // 1. Asynchronously start the task to read the entire error stream.
        //    We don't 'await' it yet. We just tell it to start listening.
        Task<string> errorTask = process.StandardError.ReadToEndAsync();

        // 2. Now, write the text to Piper's input stream.
        using (var streamWriter = process.StandardInput)
        {
            await streamWriter.WriteAsync(text);
        } // The 'using' block closes the stream, signaling to Piper that we're done sending text.

        // 3. Now, wait for the Piper process to finish its work and exit.
        await process.WaitForExitAsync();
        GD.Print("Process Finishing");

        // 4. Finally, get the result from our listening task.
        string errorOutput = await errorTask;
        GD.Print("error Finishing");
        // --- The rest of the method is the same ---

        //if (!string.IsNullOrWhiteSpace(errorOutput))
        //{
        //    GD.PrintErr($"Piper process returned an error: {errorOutput}");
        //    return null;
        //}
        GD.Print("Error has problem");
        if (!FileAccess.FileExists(godotOutputPath))
        {
            GD.PrintErr("Piper process finished, but the output WAV file was not created.");
            return null;
        }
        GD.Print("File opening");
        var outputFile = FileAccess.Open(godotOutputPath, FileAccess.ModeFlags.Read);
        if (outputFile == null) return null;
        GD.Print("getting file");
        var audioData = outputFile.GetBuffer((long)outputFile.GetLength());
        outputFile.Close();
        GD.Print("Output is ready");

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