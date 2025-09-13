// ApiManager.cs
using Godot;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HttpClient = System.Net.Http.HttpClient;

public partial class Apiservice : Node
{
    private static readonly HttpClient client = new HttpClient();

    private const string OpenRouterUrl = "https://openrouter.ai/api/v1/chat/completions";
    private const string LocalLlmUrl = "http://localhost:11434/v1/chat/completions";

    private string openRouterApiKey;
    private string AzureSpeechKey;
    private string AzureSpeechRegion;

    public bool AreKeysLoaded { get; private set; } = false;
    public bool UseRemoteLlm { get; private set; } = false;
    public bool UseRemoteSpeech { get; private set; } = false;


    public override void _Ready()
    {
        var config = new ConfigFile();
        Error err = config.Load("user://api_keys.cfg");

        // --- NEW, ROBUST LOGIC ---

        // 1. Load all potential keys and settings from the file.
        bool prioritizeLocal = false;
        if (err == Error.Ok)
        {
            prioritizeLocal = config.GetValue("user_settings", "prioritize_local", false).AsBool();
            openRouterApiKey = config.GetValue("api_keys", "openrouter_key", "").ToString();
            AzureSpeechKey = config.GetValue("api_keys", "azure_speech_key", "").ToString();
            AzureSpeechRegion = config.GetValue("api_keys", "azure_speech_region", "").ToString();
        }

        // 2. Decide if we can use the remote SPEECH services.
        // This is independent of the LLM choice.
        if (!string.IsNullOrWhiteSpace(AzureSpeechKey) && !string.IsNullOrWhiteSpace(AzureSpeechRegion))
        {
            UseRemoteSpeech = true;
            GD.Print("Azure keys are valid. Remote speech services are enabled.");
        }
        else
        {
            UseRemoteSpeech = false;
            GD.Print("Azure keys are missing or incomplete. Remote speech services are disabled.");
        }

        // 3. Decide if we should use the remote LLM.
        bool isRemoteLlmKeyValid = !string.IsNullOrWhiteSpace(openRouterApiKey);
        if (!prioritizeLocal && isRemoteLlmKeyValid)
        {
            GD.Print("Using remote OpenRouter API. (Keys are valid and local override is off)");
            UseRemoteLlm = true;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openRouterApiKey);
            client.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost");
            client.DefaultRequestHeaders.Add("X-Title", "Godot AI Chatbot");
        }
        else
        {
            if (prioritizeLocal) GD.Print("Using local Ollama model. (User override is on)");
            else GD.Print("Using local Ollama model. (LLM API key is missing or incomplete)");
            UseRemoteLlm = false;
            client.DefaultRequestHeaders.Authorization = null;
        }
    }

    public async Task<string> GetChatGPTResponse(string promptText)
    {
        // --- THIS IS THE CORE OF THE NEW LOGIC ---
        if (UseRemoteLlm)
        {
            return await GetOpenRouterResponse(promptText);
        }
        else
        {
            return await GetOllamaResponse(promptText);
        }
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
            model = "openai/gpt-oss-20b:free", // The free model you specified
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
            model = "gpt-oss:20b",
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

    public async Task<AudioStream> SynthesizeSpeech(string text)
    {
        // --- NEW SAFETY CHECK ---
        // If we don't have valid Azure keys, don't even try to call the SDK.
        if (!UseRemoteSpeech)
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

    public async Task<string> TranscribeSpeech(byte[] audioData, bool isStereo, int mixRate)
    {
        if (!UseRemoteSpeech)
        {
            GD.PrintErr("TranscribeSpeech called, but remote speech is disabled. Check Azure keys.");
            return "Error: Speech services are not configured."; // Return an error message.
        }
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