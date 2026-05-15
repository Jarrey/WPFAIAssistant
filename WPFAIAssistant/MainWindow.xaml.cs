using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WPFAIAssistant.Models;
using WPFAIAssistant.ViewModels;

namespace WPFAIAssistant
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _vm;
        private bool _webViewReady = false;

        public MainWindow(MainWindowViewModel viewModel)
        {
            InitializeComponent();
            _vm = viewModel;
            DataContext = _vm;

            _vm.PushHtmlToConsole = async (script) =>
            {
                if (!_webViewReady) return;
                // Marshal to UI thread and fully await the WebView2 call.
                // InvokeAsync(Func<Task>) returns DispatcherOperation<Task>; .Result unwraps
                // to the inner Task so we can await completion of ExecuteScriptAsync.
                var op = Dispatcher.InvokeAsync(async () =>
                {
                    try { await ConsoleWebView.ExecuteScriptAsync(script); }
                    catch { }
                });
                await (await op.Task);
            };

            _vm.ReplayHistory = async (messages) =>
            {
                if (!_webViewReady) return;
                foreach (var msg in messages)
                {
                    if (msg.Role == MessageRole.User)
                    {
                        var escaped = EscapeJs(System.Web.HttpUtility.HtmlEncode(msg.Content));
                        await ExecAsync($"appendUser('{escaped}');");
                    }
                    else if (msg.Role == MessageRole.Assistant)
                    {
                        await ExecAsync("appendAssistantStart();");
                        var escaped = EscapeJs(msg.Content);
                        await ExecAsync($"renderAssistantMarkdown('{escaped}');");
                        await ExecAsync("appendAssistantEnd();");
                    }
                }
            };

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await ConsoleWebView.EnsureCoreWebView2Async();

            ConsoleWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            ConsoleWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            ConsoleWebView.CoreWebView2.WebMessageReceived += (s, args) => { };

            var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                        "Resources", "ConsoleTemplate.html");
            if (File.Exists(htmlPath))
                ConsoleWebView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
            else
                ConsoleWebView.CoreWebView2.NavigateToString("<body style='color:red'>ConsoleTemplate.html not found</body>");

            // Wait for the page to finish loading before initialising sessions
            ConsoleWebView.CoreWebView2.DOMContentLoaded += async (s, args) =>
            {
                _webViewReady = true;
                await Dispatcher.InvokeAsync(async () =>
                {
                    await _vm.InitialiseAsync();
                });
            };
        }

        private async Task ExecAsync(string script)
        {
            var op = Dispatcher.InvokeAsync(async () =>
            {
                try { await ConsoleWebView.ExecuteScriptAsync(script); }
                catch { }
            });
            await (await op.Task);
        }

        private static string EscapeJs(string s) =>
            s.Replace("\\", "\\\\")
             .Replace("'", "\\'")
             .Replace("\r", "\\r")
             .Replace("\n", "\\n");

        private void SessionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SessionListBox.SelectedItem is SessionViewModel svm)
                _vm.SelectSessionCommand.Execute(svm);
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                if (_vm.SendCommand.CanExecute(null))
                    _vm.SendCommand.Execute(null);
            }
        }

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _vm.ApiKey = ApiKeyBox.Password;
        }

        private void Preset_DeepSeek(object sender, RoutedEventArgs e)
        {
            _vm.BaseUrl = "https://api.deepseek.com/";
            _vm.AvailableModels.Clear();
            _vm.AvailableModels.Add("deepseek-v4-flash");
            _vm.AvailableModels.Add("deepseek-v4-pro");
            _vm.SelectedModel = "deepseek-v4-flash";
        }

        private void Preset_OpenAI(object sender, RoutedEventArgs e)
        {
            _vm.BaseUrl = "https://api.openai.com/v1";
            _vm.AvailableModels.Clear();
            _vm.AvailableModels.Add("gpt-4o");
            _vm.AvailableModels.Add("gpt-4o-mini");
            _vm.SelectedModel = "gpt-4o";
        }

        private void Preset_Claude(object sender, RoutedEventArgs e)
        {
            _vm.BaseUrl = "https://api.anthropic.com/v1";
            _vm.AvailableModels.Clear();
            _vm.AvailableModels.Add("claude-opus-4-5");
            _vm.AvailableModels.Add("claude-sonnet-4-5");
            _vm.AvailableModels.Add("claude-haiku-3-5");
            _vm.SelectedModel = "claude-sonnet-4-5";
        }

        // ── Settings / Skills panel collapse ────────────────────────

        private bool _settingsExpanded = false;
        private bool _skillsExpanded = false;

        private void SettingsHeader_Click(object sender, MouseButtonEventArgs e)
        {
            _settingsExpanded = !_settingsExpanded;
            SettingsContent.Visibility = _settingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            SettingsToggleArrow.Text = _settingsExpanded ? "▼" : "▶";
        }

        private void SkillsHeader_Click(object sender, MouseButtonEventArgs e)
        {
            _skillsExpanded = !_skillsExpanded;
            SkillsContent.Visibility = _skillsExpanded ? Visibility.Visible : Visibility.Collapsed;
            SkillsToggleArrow.Text = _skillsExpanded ? "▼" : "▶";
        }

        private async void LoadSkillFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Load Skill File",
                Filter = "Skill files (*.md;*.txt;*.yaml;*.yml)|*.md;*.txt;*.yaml;*.yml|All files (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;
            foreach (var file in dlg.FileNames)
                await _vm.AddSkillFileAsync(file);
        }
    }
}
