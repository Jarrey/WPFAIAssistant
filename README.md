# WPF AI Assistant

A WPF desktop application that provides an AI chat assistant interface powered by Semantic Kernel and various LLM providers (DeepSeek, OpenAI, Claude).

## Features

- Chat with AI models through a rich WebView2-based console UI
- Markdown rendering with syntax highlighting
- Session management (create, switch, delete conversations)
- Skills system — load external prompt files to extend AI capabilities
- File system agent — AI can inspect the local file system
- Dark theme UI with collapsible settings panel
- Streaming responses with thinking/reasoning display

## Prerequisites

- .NET 10.0 SDK or later
- WebView2 runtime (included with Windows 11 / Edge)

## Getting Started

1. Clone the repository
2. Open `WPFAIAssistant.slnx` or `WPFAIAssistant/WPFAIAssistant.csproj` in your IDE
3. Restore NuGet packages:
   ```
   dotnet restore
   ```
4. Configure your API key in `WPFAIAssistant/appsettings.json`:
   ```json
   {
     "DeepSeek": {
       "ApiKey": "your-api-key-here",
       "BaseUrl": "https://api.deepseek.com",
       "ModelId": "deepseek-v4-flash"
     }
   }
   ```
5. Build and run:
   ```
   dotnet run --project WPFAIAssistant
   ```

## Usage

- Type your message in the input box and press Enter or click Send
- Use the sidebar to manage chat sessions
- Expand Settings to configure API key, base URL, and model
- Load skills (.md files) to provide the AI with custom system prompts

## Project Structure

- `Agents/` — AI agents and agent registry (Semantic Kernel plugins)
- `Bridge/` — WebView2 JavaScript interop
- `Models/` — Data models (ChatMessage, AppSettings)
- `Resources/` — HTML/JS console template
- `Services/` — AI service interfaces and implementations
- `ViewModels/` — MVVM view models

## License

MIT
