using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CIE1931xy.Core
{
    internal static class SettingsStore
    {
        public const string FileName = "CIE1931xy.settings.json";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly JsonSerializerOptions Options = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true, PropertyNameCaseInsensitive = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip
            };
            options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            return options;
        }

        public static AppSettings Load(string path, out string warning)
        {
            warning = null;
            if (path == null || !File.Exists(path)) return new AppSettings();
            try
            {
                if (new FileInfo(path).Length > 256 * 1024) throw new IOException("配置文件过大。");
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path, Utf8), Options);
                if (settings == null) throw new JsonException("配置文件内容为空。");
                if (settings.Version != 1) throw new JsonException("不支持该配置文件版本。");
                settings.Normalize();
                return settings;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is JsonException || ex is DecoderFallbackException || ex is ArgumentException)
            {
                warning = "读取配置失败，已使用默认设置：" + ex.Message;
                return new AppSettings();
            }
        }

        public static string Serialize(AppSettings settings)
        {
            return JsonSerializer.Serialize(settings, Options) + Environment.NewLine;
        }

        public static void Save(string path, string content)
        {
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Utf8.GetBytes(content);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                // 同目录替换：旧配置只在完整内容写入成功后才被替换。
                File.Move(temporary, path, true);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
