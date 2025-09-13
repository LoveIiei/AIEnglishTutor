# AI English Tutor

This is the initial release of the AI English Tutor, a desktop application built with Godot and C# that allows users to have spoken conversations with an animated AI character. The project is designed for flexibility, supporting both powerful local LLMs and cloud-based APIs.

## ✨ Features

*   **Interactive Voice Conversations:** Engage in real-time, spoken conversations. The app uses Microsoft Azure for high-quality Speech-to-Text and Text-to-Speech services.

*   **Animated 2D Character:** The tutor is represented by an animated character with `idle`, `listening`, and `talking` states for clear visual feedback.

*   **Dual AI Engine:** You choose the "brain" of the AI!
    *   ✅ **Local Mode (Default):** Utilizes a local LLM server via **Ollama** for a completely offline, private, and free experience. Perfect for powerful machines running models like `Llama-3:8b`.
    *   ✅ **Cloud Mode (Optional):** Supports cloud-based models through **OpenRouter**. Users can provide their own API keys to access a wide range of models.

*   **On-Screen UI:**
    *   A **conversation history log** lets you review your and the AI's responses.
    *   A **"Show History"** checkbox to toggle the log's visibility.
    *   A **Settings Panel** to securely manage API keys and app preferences.

*   **User-Controlled Logic:**
    *   A "Prioritize Local Model" checkbox gives you full control, allowing you to switch to your local Ollama instance even if you have API keys saved.

*   **Customizable AI Persona:** The AI's personality is defined by a detailed **system prompt**, instructing it to act as a friendly, encouraging, and simple English tutor. Model parameters like `temperature` and `max_tokens` are also configured for optimal performance.

*   **Secure "Bring Your Own Key" Model:** No API keys are hardcoded. All keys are saved securely in the `user://` directory, which is kept separate from the project and Git repository.

*   **Cross-Platform:** Built in Godot, with platform-specific configurations (like macOS microphone permissions) to ensure it runs on both Windows and Mac.

## 🛠️ Tech Stack

*   **Game Engine:** Godot 4 (with .NET/C#)
*   **Local LLM Server:** Ollama
*   **Cloud LLM Service:** OpenRouter (OpenAI-compatible API)
*   **Speech Services:** Microsoft Azure Cognitive Services for Speech
*   **Language:** C#

## 🚀 Getting Started

1.  **(For Local Mode)** Ensure [Ollama](https://ollama.com/) is installed and running a model in the terminal (e.g., `ollama run llama3:8b`).
2.  Launch the application.
3.  Click the **"Settings"** button.
4.  **To use Local Mode:** Check the "Prioritize Local Model" box and click "Save".
5.  **To use Cloud Mode:** Uncheck the local model box and enter your OpenRouter and Azure API keys, then click "Save".
6.  **Restart the application** for the new settings to take effect.
7.  Press and hold the "Hold to Talk" button to start a conversation!
