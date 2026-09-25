using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using static System.Windows.Forms.Design.AxImporter;

namespace AutoSubtitles
{
    // Формат экспорта
    public enum SubtitleExportFormat
    {
        Txt,
        Json
    }

    // Разделитель между частями строки в TXT
    public enum TxtSeparator
    {
        NewLine,
        Tab,
        Space
    }

    public class ExportOptions
    {
        public SubtitleExportFormat Format { get; set; } = SubtitleExportFormat.Txt;
        public bool IncludeTimestamps { get; set; } = true;
        public bool IncludeSpeaker { get; set; } = true;
        public bool IncludeText { get; set; } = true;
        public TxtSeparator Separator { get; set; } = TxtSeparator.NewLine;

        // Соответствие "оригинальное имя" -> "новое имя".
        // Пустое/отсутствующее значение означает "не переименовывать".
        public Dictionary<string, string> SpeakerRenames { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

    }
    public static class SubtitleExporter
    {

        public static string Generate(
            IReadOnlyList<ViewModel> items,
            ExportOptions options,
            bool isPreview)
        {
            if (items == null || items.Count == 0)
                return "There is no data to display.";

            return options.Format == SubtitleExportFormat.Json
                ? GenerateJson(items, options, isPreview)
                : GenerateTxt(items, options, isPreview);
        }

        private static string GenerateJson(IReadOnlyList<ViewModel> items, ExportOptions options, bool isPreview)
        {
            var jsonPrepared = items.Select(s =>
            {
                var dict = new Dictionary<string, object>();
                if (options.IncludeTimestamps)
                {
                    dict["StartTime"] = s.StartTime;
                    dict["EndTime"] = s.EndTime;
                }
                if (options.IncludeSpeaker)
                {
                    //dict["Speaker"] = s.Speaker;
                    dict["Speaker"] = GetDisplaySpeaker(s.Speaker, options);
                }
                dict["Text"] = s.Text;
                return dict;
            });

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            string json = JsonSerializer.Serialize(jsonPrepared, jsonOptions);

            return isPreview && items.Count > 3
                ? json + "\n\n... [The remaining data is hidden in the preview] ..."
                : json;
        }

        private static string GenerateTxt(
            IReadOnlyList<ViewModel> items,
            ExportOptions options,
            bool isPreview)
        {
            var sb = new StringBuilder();
            string separator = GetSeparator(options.Separator);

            foreach (var item in items)
            {
                var parts = new List<string>();

                if (options.IncludeTimestamps)
                {
                    parts.Add($"[{item.StartTime:hh\\:mm\\:ss} - {item.EndTime:hh\\:mm\\:ss}]");
                }
                if (options.IncludeSpeaker && !string.IsNullOrWhiteSpace(item.Speaker))
                {
                    //parts.Add($"{item.Speaker}:");
                    parts.Add($"{GetDisplaySpeaker(item.Speaker, options)}:");
                }
                parts.Add(item.Text);

                sb.AppendLine(string.Join(separator, parts));
            }

            if (isPreview && items.Count > 3)
            {
                sb.AppendLine().AppendLine("... [The remaining data is hidden in the preview] ...");
            }

            return sb.ToString();
        }

        private static string GetSeparator(TxtSeparator separator) => separator switch
        {
            TxtSeparator.Tab => "\t",
            TxtSeparator.Space => " ",
            _ => "\r\n"
        };

        private static string GetDisplaySpeaker(string original, ExportOptions options)
        {
            if (string.IsNullOrWhiteSpace(original))
                return original ?? string.Empty;

            if (options.SpeakerRenames != null &&
                options.SpeakerRenames.TryGetValue(original, out var renamed) &&
                !string.IsNullOrWhiteSpace(renamed))
            {
                return renamed;
            }
            return original;
        }
    }
}