using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace WPFAIAssistant.Agents
{
    /// <summary>
    /// Agent that lets the AI inspect the local file system.
    /// Tools are registered as Microsoft.Extensions.AI AIFunction instances
    /// so the IChatClient pipeline (with UseFunctionInvocation) can invoke them automatically.
    /// </summary>
    public class FileSystemAgent : IAgent
    {
        public string PluginName => "FileSystem";
        public string Description => "Provides access to local file system: list directories and file metadata.";

        public IReadOnlyList<AIFunction> GetAIFunctions() =>
        [
            AIFunctionFactory.Create(ListDirectory,    name: "list_directory"),
            AIFunctionFactory.Create(GetFileInfo,      name: "get_file_info"),
            AIFunctionFactory.Create(GetDirectoryInfo, name: "get_directory_info"),
        ];

        [Description("List the contents (sub-folders and files) of a local directory. Returns a formatted text summary.")]
        private string ListDirectory(
            [Description("Absolute or relative path of the directory to list.")] string path,
            [Description("Include hidden files and folders (starting with '.').")] bool includeHidden = false)
        {
            path = ResolvePath(path);
            if (!Directory.Exists(path))
                return $"[Error] Directory not found: {path}";

            var sb = new StringBuilder();
            var info = new DirectoryInfo(path);
            sb.AppendLine($"📁 {info.FullName}");
            sb.AppendLine($"   Created : {info.CreationTime:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"   Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm}");
            sb.AppendLine();

            var dirs = info.GetDirectories()
                           .Where(d => includeHidden || !d.Name.StartsWith('.'))
                           .OrderBy(d => d.Name).ToList();
            if (dirs.Count > 0)
            {
                sb.AppendLine($"### Folders ({dirs.Count})");
                foreach (var d in dirs)
                    sb.AppendLine($"  {d.Name}/ [{d.LastWriteTime:yyyy-MM-dd HH:mm}]");
                sb.AppendLine();
            }

            var files = info.GetFiles()
                            .Where(f => includeHidden || !f.Name.StartsWith('.'))
                            .OrderBy(f => f.Name).ToList();
            if (files.Count > 0)
            {
                sb.AppendLine($"### Files ({files.Count})");
                foreach (var f in files)
                    sb.AppendLine($"  {f.Name}  {FormatSize(f.Length)}  [{f.LastWriteTime:yyyy-MM-dd HH:mm}]");
            }
            else
            {
                sb.AppendLine("(no files)");
            }

            return sb.ToString();
        }

        [Description("Get detailed metadata about a single file: size, dates, extension, read-only flag.")]
        private string GetFileInfo(
            [Description("Absolute or relative path of the file.")] string path)
        {
            path = ResolvePath(path);
            if (!File.Exists(path))
                return $"[Error] File not found: {path}";

            var f = new FileInfo(path);
            var sb = new StringBuilder();
            sb.AppendLine($"📄 {f.FullName}");
            sb.AppendLine($"   Size     : {FormatSize(f.Length)} ({f.Length:N0} bytes)");
            sb.AppendLine($"   Extension: {f.Extension}");
            sb.AppendLine($"   Created  : {f.CreationTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"   Modified : {f.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"   Read-only: {f.IsReadOnly}");
            return sb.ToString();
        }

        [Description("Get metadata for a directory: total size, file count, sub-folder count.")]
        private string GetDirectoryInfo(
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
            catch (UnauthorizedAccessException) { }

            var sb = new StringBuilder();
            sb.AppendLine($"📁 {info.FullName}");
            sb.AppendLine($"   Total size  : {FormatSize(totalSize)}");
            sb.AppendLine($"   Files       : {fileCount:N0}");
            sb.AppendLine($"   Sub-folders : {dirCount:N0}");
            sb.AppendLine($"   Created     : {info.CreationTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"   Modified    : {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        private static string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return Environment.CurrentDirectory;
            path = Environment.ExpandEnvironmentVariables(path.Trim());
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
