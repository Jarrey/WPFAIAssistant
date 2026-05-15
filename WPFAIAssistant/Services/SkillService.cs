using System.IO;
using WPFAIAssistant.Models;

namespace WPFAIAssistant.Services
{
    public class SkillService : ISkillService
    {
        public string SkillsDirectory { get; } =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skills");

        public async Task<IReadOnlyList<SkillEntry>> ScanDirectoryAsync(string directory)
        {
            var result = new List<SkillEntry>();
            if (!Directory.Exists(directory)) return result;

            foreach (var file in Directory.GetFiles(directory, "*.md", SearchOption.AllDirectories))
            {
                try { result.Add(await LoadFileAsync(file)); }
                catch { /* skip unreadable files */ }
            }
            return result;
        }

        public async Task<IReadOnlyList<SkillEntry>> ScanAllKnownDirectoriesAsync()
        {
            var all = new List<SkillEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TryAddFile(string path, string source)
            {
                if (!File.Exists(path) || !seen.Add(path)) return;
                try
                {
                    var content = File.ReadAllText(path);
                    all.Add(new SkillEntry
                    {
                        Name = Path.GetFileNameWithoutExtension(path),
                        FilePath = path,
                        Content = content,
                        IsEnabled = true,
                        Source = source,
                    });
                }
                catch { }
            }

            async Task ScanDir(string dir, string source)
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir, "*.md", SearchOption.AllDirectories))
                    TryAddFile(f, source);
                await Task.CompletedTask;
            }

            // Local skills directory shipped with the app
            await ScanDir(SkillsDirectory, "local");

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Common AI tool skill/instruction directories
            foreach (var probe in BuildKnownProbes(home))
                TryAddFile(probe.path, probe.source);

            return all;
        }

        private static IEnumerable<(string path, string source)> BuildKnownProbes(string home)
        {
            // Claude Code / Anthropic
            yield return (Path.Combine(home, ".claude", "CLAUDE.md"), "claude-code");
            yield return (Path.Combine(home, ".config", "claude", "CLAUDE.md"), "claude-code");

            // OpenCode
            yield return (Path.Combine(home, ".opencode", "instructions.md"), "opencode");
            yield return (Path.Combine(home, ".config", "opencode", "instructions.md"), "opencode");

            // Cursor
            yield return (Path.Combine(home, ".cursor", "rules"), "cursor");
            yield return (Path.Combine(home, ".cursorrules"), "cursor");

            // GitHub Copilot
            yield return (Path.Combine(home, ".github", "copilot-instructions.md"), "copilot");

            // Windsurf
            yield return (Path.Combine(home, ".windsurf", "instructions.md"), "windsurf");
            yield return (Path.Combine(home, ".windsurfrules"), "windsurf");

            // Cline
            yield return (Path.Combine(home, ".clinerules"), "cline");
            yield return (Path.Combine(home, ".cline", "instructions.md"), "cline");

            // Continue.dev
            yield return (Path.Combine(home, ".continue", "config.md"), "continue");

            // Aider
            yield return (Path.Combine(home, ".aider.conf.yml"), "aider");
            yield return (Path.Combine(home, ".aiderignore"), "aider");

            // Zed
            yield return (Path.Combine(home, ".config", "zed", "prompts.md"), "zed");

            // Generic home-folder fallback
            yield return (Path.Combine(home, "AGENTS.md"), "agents-md");
            yield return (Path.Combine(home, "instructions.md"), "instructions-md");
        }

        public async Task<SkillEntry> LoadFileAsync(string filePath)
        {
            var content = await File.ReadAllTextAsync(filePath);
            return new SkillEntry
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                Content = content,
                IsEnabled = true,
                Source = "manual",
            };
        }

        public string BuildSystemPrompt(IEnumerable<SkillEntry> skills)
        {
            var enabled = skills.Where(s => s.IsEnabled && !string.IsNullOrWhiteSpace(s.Content)).ToList();
            if (enabled.Count == 0) return string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Active Skills / Instructions");
            sb.AppendLine();
            foreach (var skill in enabled)
            {
                sb.AppendLine($"### {skill.Name}");
                sb.AppendLine(skill.Content.Trim());
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }
    }
}
