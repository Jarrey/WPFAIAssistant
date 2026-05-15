using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;

namespace WPFAIAssistant.Agents
{
    /// <summary>
    /// Agent that lets the AI inspect the local file system.
    /// Exposed functions:
    ///   - list_directory   : list sub-folders and files in a directory
    ///   - get_file_info    : metadata for a single file
    ///   - get_directory_info: metadata for a directory
    /// </summary>
    public class FileSystemAgent : IAgent
    {
        public string PluginName => "FileSystem";
        public string Description => "Provides access to local file system: list directories and file metadata.";

        public IReadOnlyList<AgentToolDefinition> GetToolDefinitions()
        {
            return
            [
                new AgentToolDefinition
                {
                    Name = "list_directory",
                    Description = "List the contents (sub-folders and files) of a local directory. Returns a formatted text summary.",
                    ParametersSchema = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["path"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "Absolute or relative path of the directory to list."
                            },
                            ["includeHidden"] = new Dictionary<string, object>
                            {
                                ["type"] = "boolean",
                                ["description"] = "Include hidden files and folders (starting with '.')."
                            }
                        },
                        ["required"] = new[] { "path" },
                        ["additionalProperties"] = false
                    }
                },
                new AgentToolDefinition
                {
                    Name = "get_file_info",
                    Description = "Get detailed metadata about a single file: size, dates, extension, read-only flag.",
                    ParametersSchema = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["path"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "Absolute or relative path of the file."
                            }
                        },
                        ["required"] = new[] { "path" },
                        ["additionalProperties"] = false
                    }
                },
                new AgentToolDefinition
                {
                    Name = "get_directory_info",
                    Description = "Get metadata for a directory: total size, file count, sub-folder count.",
                    ParametersSchema = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["path"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "Absolute or relative path of the directory."
                            }
                        },
                        ["required"] = new[] { "path" },
                        ["additionalProperties"] = false
                    }
                }
            ];
        }

        public string Invoke(string toolName, JsonElement arguments)
        {
            return toolName switch
            {
                "list_directory" => ListDirectory(
                    GetRequiredString(arguments, "path"),
                    GetOptionalBool(arguments, "includeHidden") ?? false),
                "get_file_info" => GetFileInfo(GetRequiredString(arguments, "path")),
                "get_directory_info" => GetDirectoryInfo(GetRequiredString(arguments, "path")),
                _ => $"[Error] Unknown tool: {toolName}"
            };
        }

        // ── Tool Implementations ─────────────────────────────────────

        [Description("List the contents (sub-folders and files) of a local directory. Returns a formatted text summary.")]
        public string ListDirectory(
            [Description("Absolute or relative path of the directory to list.")] string path,
            [Description("Include hidden files and folders (starting with '.').")] bool includeHidden = false)
        {
            path = ResolvePath(path);

            if (!Directory.Exists(path))
                return $"[Error] Directory not found: {path}";

            var sb = new StringBuilder();
            var info = new DirectoryInfo(path);

            sb.AppendLine($"📁 **{info.FullName}**");
            sb.AppendLine($"   Created : {info.CreationTime:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"   Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm}");
            sb.AppendLine();

            // Sub-directories
            var dirs = info.GetDirectories()
                           .Where(d => includeHidden || !d.Name.StartsWith('.'))
                           .OrderBy(d => d.Name)
                           .ToList();

            if (dirs.Count > 0)
            {
                sb.AppendLine($"### Folders ({dirs.Count})");
                foreach (var d in dirs)
                    sb.AppendLine($"  📂 {d.Name}/   [{d.LastWriteTime:yyyy-MM-dd HH:mm}]");
                sb.AppendLine();
            }

            // Files
            var files = info.GetFiles()
                            .Where(f => includeHidden || !f.Name.StartsWith('.'))
                            .OrderBy(f => f.Name)
                            .ToList();

            if (files.Count > 0)
            {
                sb.AppendLine($"### Files ({files.Count})");
                foreach (var f in files)
                    sb.AppendLine($"  📄 {f.Name,-40} {FormatSize(f.Length),10}   [{f.LastWriteTime:yyyy-MM-dd HH:mm}]");
            }
            else
            {
                sb.AppendLine("_(no files)_");
            }

            return sb.ToString();
        }

        [Description("Get detailed metadata about a single file: size, dates, extension, read-only flag.")]
        public string GetFileInfo(
            [Description("Absolute or relative path of the file.")] string path)
        {
            path = ResolvePath(path);

            if (!File.Exists(path))
                return $"[Error] File not found: {path}";

            var f = new FileInfo(path);
            var sb = new StringBuilder();
            sb.AppendLine($"📄 **{f.FullName}**");
            sb.AppendLine($"   Size     : {FormatSize(f.Length)} ({f.Length:N0} bytes)");
            sb.AppendLine($"   Extension: {f.Extension}");
            sb.AppendLine($"   Created  : {f.CreationTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"   Modified : {f.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"   Read-only: {f.IsReadOnly}");
            return sb.ToString();
        }

        [Description("Get metadata for a directory: total size, file count, sub-folder count.")]
        public string GetDirectoryInfo(
            [Description("Absolute or relative path of the directory.")] string path)
        {
            path = ResolvePath(path);

            if (!Directory.Exists(path))
                return $"[Error] Directory not found: {path}";

            var info = new DirectoryInfo(path);

            long totalSize = 0;
            int fileCount = 0;
            int dirCount = 0;

            try
            {
                foreach (var f in info.EnumerateFiles("*", SearchOption.AllDirectories))
                { totalSize += f.Length; fileCount++; }
                dirCount = info.EnumerateDirectories("*", SearchOption.AllDirectories).Count();
            }
            catch (UnauthorizedAccessException) { /* skip inaccessible */ }

            var sb = new StringBuilder();
            sb.AppendLine($"📁 **{info.FullName}**");
            sb.AppendLine($"   Total size  : {FormatSize(totalSize)}");
            sb.AppendLine($"   Files       : {fileCount:N0}");
            sb.AppendLine($"   Sub-folders : {dirCount:N0}");
            sb.AppendLine($"   Created     : {info.CreationTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"   Modified    : {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static string GetRequiredString(JsonElement obj, string property)
        {
            if (obj.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? string.Empty;

            throw new ArgumentException($"Missing required argument: {property}");
        }

        private static bool? GetOptionalBool(JsonElement obj, string property)
        {
            if (obj.TryGetProperty(property, out var value) &&
                (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
                return value.GetBoolean();

            return null;
        }

        private static string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return Environment.CurrentDirectory;
            path = Environment.ExpandEnvironmentVariables(path.Trim());
            // Replace forward slash so Windows handles both separators
            path = path.Replace('/', Path.DirectorySeparatorChar);
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
        }

        private static string FormatSize(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:F1} MB",
            _ => $"{bytes / 1024.0 / 1024 / 1024:F2} GB"
        };
    }
}
