using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using System.Collections.ObjectModel;
using System.Windows;
using WPFAIAssistant.Models;
using WPFAIAssistant.Services;

namespace WPFAIAssistant.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IAIService _aiService;
        private readonly ISessionService _sessionService;
        private readonly ISkillService _skillService;
        private readonly IConfiguration _configuration;
        private CancellationTokenSource? _cts;

        private ChatSession _currentSession = new();

        public Action<string>? PushHtmlToConsole { get; set; }
        public Func<IReadOnlyList<ChatMessage>, Task>? ReplayHistory { get; set; }

        // ── Settings ──────────────────────────────────────────────
        [ObservableProperty] private string _apiKey = string.Empty;
        [ObservableProperty] private string _baseUrl = "https://api.deepseek.com/";
        [ObservableProperty] private string _selectedModel = "deepseek-chat";
        [ObservableProperty]
        private ObservableCollection<string> _availableModels = new()
        {
            "deepseek-chat", "deepseek-reasoner"
        };

        // ── Input / state ─────────────────────────────────────────
        [ObservableProperty] private string _inputText = string.Empty;
        [ObservableProperty] private bool _isBusy = false;
        [ObservableProperty] private string _statusText = "Ready";

        // ── Session list ─────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<SessionViewModel> _sessions = new();
        [ObservableProperty] private SessionViewModel? _activeSession;

        // ── Skills ───────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<SkillViewModel> _skills = new();

        // ── Constructor ──────────────────────────────────────────
        public MainWindowViewModel(
            IAIService aiService,
            ISessionService sessionService,
            ISkillService skillService,
            IConfiguration configuration)
        {
            _aiService = aiService;
            _sessionService = sessionService;
            _skillService = skillService;
            _configuration = configuration;

            _apiKey = _configuration["DeepSeek:ApiKey"] ?? string.Empty;
            _baseUrl = _configuration["DeepSeek:BaseUrl"] ?? "https://api.deepseek.com/";
            _selectedModel = _configuration["DeepSeek:ModelId"] ?? "deepseek-v4-flash";
        }

        public async Task InitialiseAsync()
        {
            await LoadSkillsAsync();
            await LoadSessionListAsync();

            if (Sessions.Count == 0)
                await NewSessionAsync();
            else
                await SwitchSessionAsync(Sessions[0]);
        }

        [RelayCommand]
        private async Task SendAsync()
        {
            var text = InputText.Trim();
            if (string.IsNullOrEmpty(text) || IsBusy) return;

            InputText = string.Empty;
            IsBusy = true;
            StatusText = "Thinking...";

            if (_currentSession.Messages.Count == 0)
            {
                _currentSession.Title = text.Length > 40 ? text[..40] + "..." : text;
                if (ActiveSession != null)
                {
                    ActiveSession.Title = _currentSession.Title;
                    ActiveSession.UpdatedAt = DateTime.Now;
                }
            }

            AppendUserBubble(text);

            _cts = new CancellationTokenSource();
            var assistantBuffer = new System.Text.StringBuilder();
            var thinkingBuffer = new System.Text.StringBuilder();

            // Build skill system prompt
            var enabledSkillEntries = new List<SkillEntry>();
            foreach (var sv in Skills.Where(s => s.IsEnabled))
            {
                try
                {
                    var loaded = await _skillService.LoadFileAsync(sv.FilePath);
                    enabledSkillEntries.Add(loaded);
                }
                catch { }
            }
            var systemExtra = _skillService.BuildSystemPrompt(enabledSkillEntries);

            bool hasThinking = false;
            bool assistantStarted = false;

            try
            {
                await foreach (var chunk in _aiService.StreamChatAsync(
                    text, SelectedModel, ApiKey, BaseUrl, _currentSession.Messages,
                    onThinkingChunk: (thinking) =>
                    {
                        if (!hasThinking)
                        {
                            hasThinking = true;
                            PushHtmlToConsole?.Invoke("appendThinkingStart();");
                        }
                        thinkingBuffer.Append(thinking);
                        var escaped = EscapeJsString(thinking);
                        PushHtmlToConsole?.Invoke($"appendThinkingChunk('{escaped}');");
                    },
                    systemPromptExtra: string.IsNullOrEmpty(systemExtra) ? null : systemExtra,
                    cancellationToken: _cts.Token))
                {
                    if (hasThinking && !assistantStarted)
                    {
                        // Close thinking block right before first assistant token
                        PushHtmlToConsole?.Invoke("appendThinkingEnd();");
                    }
                    if (!assistantStarted)
                    {
                        assistantStarted = true;
                        PushHtmlToConsole?.Invoke("appendAssistantStart();");
                    }
                    assistantBuffer.Append(chunk);
                    var escaped = EscapeJsString(chunk);
                    PushHtmlToConsole?.Invoke($"appendAssistantChunk('{escaped}');");
                }

                // If thinking never ended (no content came after thinking)
                if (hasThinking)
                    PushHtmlToConsole?.Invoke("appendThinkingEnd();");

                if (!assistantStarted)
                    PushHtmlToConsole?.Invoke("appendAssistantStart();");

                var escapedMarkdown = EscapeJsString(assistantBuffer.ToString());
                PushHtmlToConsole?.Invoke($"renderAssistantMarkdown('{escapedMarkdown}');");
                PushHtmlToConsole?.Invoke("appendAssistantEnd();");

                _currentSession.Messages.Add(new ChatMessage { Role = MessageRole.User, Content = text });
                _currentSession.Messages.Add(new ChatMessage { Role = MessageRole.Assistant, Content = assistantBuffer.ToString() });

                await _sessionService.SaveAsync(_currentSession);
                if (ActiveSession != null) ActiveSession.UpdatedAt = _currentSession.UpdatedAt;

                var svm = Sessions.FirstOrDefault(s => s.Id == _currentSession.Id);
                if (svm != null && Sessions.IndexOf(svm) != 0)
                {
                    Sessions.Remove(svm);
                    Sessions.Insert(0, svm);
                }

                StatusText = "Ready";
            }
            catch (OperationCanceledException)
            {
                if (hasThinking) PushHtmlToConsole?.Invoke("appendThinkingEnd();");
                if (assistantStarted) PushHtmlToConsole?.Invoke("appendAssistantEnd();");
                PushHtmlToConsole?.Invoke(BuildScript("appendSystem", "<em>Generation cancelled.</em>"));
                StatusText = "Cancelled";
            }
            catch (Exception ex)
            {
                if (hasThinking) PushHtmlToConsole?.Invoke("appendThinkingEnd();");
                if (assistantStarted) PushHtmlToConsole?.Invoke("appendAssistantEnd();");
                var errHtml = "<span style='color:#e74c3c'><b>Error:</b> " + System.Web.HttpUtility.HtmlEncode(ex.Message) + "</span>";
                PushHtmlToConsole?.Invoke(BuildScript("appendSystem", EscapeJsString(errHtml)));
                StatusText = "Error";
            }
            finally
            {
                IsBusy = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        [RelayCommand]
        private void Stop() => _cts?.Cancel();

        [RelayCommand]
        private async Task NewSessionAsync()
        {
            _currentSession = new ChatSession();
            await _sessionService.SaveAsync(_currentSession);

            var svm = new SessionViewModel(_currentSession.Id, _currentSession.Title, _currentSession.UpdatedAt);
            Sessions.Insert(0, svm);

            SetActiveSessionItem(svm);
            PushHtmlToConsole?.Invoke("clearConsole();");
            StatusText = "Ready";
        }

        [RelayCommand]
        private async Task SelectSessionAsync(SessionViewModel? svm)
        {
            if (svm == null || svm.Id == _currentSession.Id) return;
            await SwitchSessionAsync(svm);
        }

        [RelayCommand]
        private async Task DeleteSessionAsync(SessionViewModel? svm)
        {
            if (svm == null) return;
            await _sessionService.DeleteAsync(svm.Id);
            Sessions.Remove(svm);

            if (svm.Id == _currentSession.Id)
            {
                if (Sessions.Count > 0)
                    await SwitchSessionAsync(Sessions[0]);
                else
                    await NewSessionAsync();
            }
        }

        [RelayCommand]
        private async Task ClearChatAsync()
        {
            _currentSession.Messages.Clear();
            await _sessionService.SaveAsync(_currentSession);
            PushHtmlToConsole?.Invoke("clearConsole();");
            StatusText = "Ready";
        }

        [RelayCommand]
        private async Task RefreshModelsAsync()
        {
            try
            {
                var models = await _aiService.GetAvailableModelsAsync(ApiKey, BaseUrl);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableModels.Clear();
                    foreach (var m in models) AvailableModels.Add(m);
                    if (AvailableModels.Count > 0 && !AvailableModels.Contains(SelectedModel))
                        SelectedModel = AvailableModels[0];
                });
            }
            catch (Exception ex) { StatusText = "Failed: " + ex.Message; }
        }

        [RelayCommand]
        private async Task RescanSkillsAsync()
        {
            await LoadSkillsAsync();
            StatusText = $"Skills: {Skills.Count} loaded";
        }

        [RelayCommand]
        private void RemoveSkill(SkillViewModel? skill)
        {
            if (skill != null) Skills.Remove(skill);
        }

        // Called from view after file dialog picks a file
        public async Task AddSkillFileAsync(string filePath)
        {
            if (Skills.Any(s => s.FilePath == filePath)) return;
            try
            {
                var entry = await _skillService.LoadFileAsync(filePath);
                Skills.Add(new SkillViewModel(entry.Name, entry.FilePath, true, "manual"));
            }
            catch (Exception ex) { StatusText = "Failed to load skill: " + ex.Message; }
        }

        private async Task LoadSkillsAsync()
        {
            var existing = Skills.Select(s => s.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var found = await _skillService.ScanAllKnownDirectoriesAsync();
            foreach (var e in found)
            {
                if (!existing.Contains(e.FilePath))
                    Skills.Add(new SkillViewModel(e.Name, e.FilePath, true, e.Source));
            }
        }

        private async Task LoadSessionListAsync()
        {
            var all = await _sessionService.LoadAllAsync();
            Sessions.Clear();
            foreach (var s in all)
                Sessions.Add(new SessionViewModel(s.Id, s.Title, s.UpdatedAt));
        }

        private async Task SwitchSessionAsync(SessionViewModel svm)
        {
            var full = await _sessionService.LoadAsync(svm.Id);
            _currentSession = full ?? new ChatSession { Id = svm.Id, Title = svm.Title };

            SetActiveSessionItem(svm);
            PushHtmlToConsole?.Invoke("clearConsole();");

            if (ReplayHistory != null && _currentSession.Messages.Count > 0)
                await ReplayHistory(_currentSession.Messages);

            StatusText = "Ready";
        }

        private void SetActiveSessionItem(SessionViewModel svm)
        {
            if (ActiveSession != null) ActiveSession.IsActive = false;
            ActiveSession = svm;
            svm.IsActive = true;
        }

        private void AppendUserBubble(string text)
        {
            var htmlText = EscapeJsString(System.Web.HttpUtility.HtmlEncode(text));
            PushHtmlToConsole?.Invoke(BuildScript("appendUser", htmlText));
        }

        private static string BuildScript(string fn, string arg) => fn + "('" + arg + "');";

        private static string EscapeJsString(string s) =>
            s.Replace("\\", "\\\\")
             .Replace("'", "\\'")
             .Replace("\r", "\\r")
             .Replace("\n", "\\n");
    }
}
