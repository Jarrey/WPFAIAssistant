# WPF AI Assistant

A WPF desktop application providing an AI chat assistant with a rich WebView2-based console, compatible with OpenAI-compatible API providers (DeepSeek, OpenAI, Anthropic Claude, etc.).

## Features

- **WebView2 Console UI** — Markdown rendering with syntax highlighting (highlight.js), thinking/reasoning blocks, streaming cursor, dark theme (Catppuccin Mocha-inspired)
- **Multi-Provider** — DeepSeek, OpenAI, and Claude presets; any OpenAI-compatible API works
- **Streaming Responses** — Real-time token streaming with separate thinking/reasoning display
- **Session Management** — Create, switch, and delete conversations; auto-save and restore
- **Skills System** — Load external `.md` files as custom system prompts to extend AI capabilities
- **File System Agent** — AI can inspect the local file system (list directories, get file metadata) via tool calling
- **Tool Calling** — Automatic function/tool resolution via OpenAI-compatible tool-calling flow for supported models
- **Dark Theme UI** — Collapsible settings panel, session list with timestamps, skill management

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) or later
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (included with Windows 11 / Microsoft Edge)
- Windows (WPF framework dependency)

## Getting Started

1. Clone the repository:
   ```
   git clone <repo-url>
   cd WPFAIAssistant
   ```

2. Restore NuGet packages:
   ```
   dotnet restore
   ```

3. Configure your API key in `WPFAIAssistant/appsettings.json`:
   ```json
   {
     "DeepSeek": {
       "ApiKey": "your-api-key-here",
       "BaseUrl": "https://api.deepseek.com",
       "ModelId": "deepseek-v4-flash"
     }
   }
   ```

4. Build and run:
   ```
   dotnet run --project WPFAIAssistant
   ```

## Usage

- **Chat**: Type a message in the input box and press Enter or click **Send**
- **Sessions**: Use the sidebar to create (＋New), switch, or delete conversations
- **Settings**: Click ⚙ **SETTINGS** to expand the panel and configure API key, base URL, and model
- **Presets**: Quickly switch provider settings with **DeepSeek**, **OpenAI**, or **Claude** preset buttons
- **Skills**: Click 🧩 **SKILLS** to view loaded skills; click **＋Load** to import a `.md` skill file, toggle skills on/off with the checkbox
- **Stop**: Click **⛔ Stop Generation** to cancel an ongoing response
- **App Settings**: API key, base URL, and model are also configurable at runtime via the settings panel

## Project Structure

```
WPFAIAssistant/
├── Agents/
│   ├── AgentRegistry.cs       — IAgent / IAgentRegistry interfaces & agent tool registry
│   └── FileSystemAgent.cs     — File system tool definitions and handlers
├── Bridge/
│   └── WebBridge.cs           — COM-visible JS↔WPF interop object for WebView2
├── Models/
│   ├── AppSettings.cs         — ApiKey, BaseUrl, ModelId configuration model
│   └── ChatMessage.cs         — MessageRole enum & ChatMessage class
├── Resources/
│   └── ConsoleTemplate.html   — Full HTML/CSS/JS console UI (marked.js, highlight.js)
├── Services/
│   ├── IAIService.cs          — AI service interface (streaming chat, model list)
│   ├── DeepSeekAIService.cs   — Implementation: raw HTTP SSE + OpenAI-compatible tool calling
│   └── SpectreConsoleRenderer.cs  — ANSI escape code & Markdown to HTML converter
├── ViewModels/
│   └── MainWindowViewModel.cs — MVVM view model (CommunityToolkit.Mvvm)
├── skills/
│   └── example_skill.md       — Example skill file
├── App.xaml / App.xaml.cs     — Application entry point & DI setup
├── MainWindow.xaml            — Full dark-theme UI layout
├── MainWindow.xaml.cs         — Window code-behind (WebView2 setup, events)
└── appsettings.json           — Default configuration (API key, base URL, model)
```

## Dependencies

| Package | Version |
|---|---|
| CommunityToolkit.Mvvm | 8.4.2 |
| Microsoft.Extensions.DependencyInjection | 10.0.8 |
| Microsoft.Extensions.Configuration | 10.0.8 |
| Microsoft.Extensions.Configuration.Json | 10.0.8 |
| Microsoft.Web.WebView2 | 1.0.3967.48 |

## AI Providers

The application is pre-configured with presets for three providers:

- **DeepSeek**: `https://api.deepseek.com/v1` — models: `deepseek-v4-flash`, `deepseek-v4-pro` (with thinking/reasoning support)
- **OpenAI**: `https://api.openai.com/v1` — models: `gpt-4o`, `gpt-4o-mini`
- **Claude (Anthropic)**: `https://api.anthropic.com/v1` — models: `claude-opus-4-5`, `claude-sonnet-4-5`, `claude-haiku-3-5`

## Key Technical Details

- **Reasoning Models**: When using `deepseek-v4-pro` (or any model ID containing "pro"/"reasoner"), the service enables thinking mode and disables tool calling (API limitation)
- **Tool Calling**: For non-reasoning models, the app resolves tool calls through an OpenAI-compatible non-streaming pass before streaming the final answer
- **Session Persistence**: Sessions are auto-saved to the `sessions/` directory as JSON files
- **Skills**: `.md` files placed in the `skills/` directory or loaded via the UI are injected into the system prompt

## License

MIT — see [LICENSE](LICENSE)
