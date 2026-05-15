using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace WPFAIAssistant.Agents
{
    /// <summary>
    /// Agent that lets the AI write output to local files.
    /// </summary>
    public class FileOutputAgent : IAgent
    {
        public string PluginName => "FileOutput";
        public string Description => "Writes or appends text content to local files.";

        public void Register(Kernel kernel) =>
            kernel.ImportPluginFromObject(this, PluginName);

        [KernelFunction("write_text_file")]
        [Description("Write text content to a local file. Creates parent folders if needed.")]
        public string WriteTextFile(
            [Description("Absolute or relative file path.")] string path,
            [Description("Text content to write.")] string content,
            [Description("Whether to overwrite if the file exists.")] bool overwrite = true,
            [Description("Text encoding. Supported: utf-8, utf-16, ascii.")] string encoding = "utf-8")
        {
            try
            {
                path = ResolvePath(path);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                if (File.Exists(path) && !overwrite)
                    return $"[Error] File already exists and overwrite=false: {path}";

                File.WriteAllText(path, content ?? string.Empty, ResolveEncoding(encoding));
                var info = new FileInfo(path);
                return $"[OK] Wrote {info.Length:N0} bytes to: {info.FullName}";
            }
            catch (Exception ex)
            {
                return $"[Error] Failed to write file: {ex.Message}";
            }
        }

        [KernelFunction("append_text_file")]
        [Description("Append text content to a local file. Creates parent folders and file if needed.")]
        public string AppendTextFile(
            [Description("Absolute or relative file path.")] string path,
            [Description("Text content to append.")] string content,
            [Description("Text encoding. Supported: utf-8, utf-16, ascii.")] string encoding = "utf-8")
        {
            try
            {
                path = ResolvePath(path);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.AppendAllText(path, content ?? string.Empty, ResolveEncoding(encoding));
                var info = new FileInfo(path);
                return $"[OK] Appended content. File size is now {info.Length:N0} bytes: {info.FullName}";
            }
            catch (Exception ex)
            {
                return $"[Error] Failed to append file: {ex.Message}";
            }
        }

        private static string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Path.GetFullPath("output.txt");

            path = Environment.ExpandEnvironmentVariables(path.Trim());
            path = path.Replace('/', Path.DirectorySeparatorChar);
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
        }

        private static Encoding ResolveEncoding(string encoding)
        {
            if (string.IsNullOrWhiteSpace(encoding)) return Encoding.UTF8;

            return encoding.Trim().ToLowerInvariant() switch
            {
                "utf-8" or "utf8" => new UTF8Encoding(false),
                "utf-16" or "utf16" or "unicode" => Encoding.Unicode,
                "ascii" => Encoding.ASCII,
                _ => Encoding.UTF8
            };
        }
    }
}
