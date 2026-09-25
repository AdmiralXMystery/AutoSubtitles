using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AutoSubtitles
{
    // Читает JSON-файл с субтитрами и превращает его в список ViewModel.
    public static class SubtitleImporter
    {
        // Читает файл и возвращает список субтитров.
        // Бросает JsonException, если файл повреждён или имеет неверный формат.
        // Бросает IOException, если файл не удаётся прочитать.
        public static List<ViewModel> ImportFromJson(string filePath)
        {
            string jsonString = File.ReadAllText(filePath, Encoding.UTF8);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonTimeSpanConverter() }
            };

            var imported = JsonSerializer.Deserialize<List<ViewModel>>(jsonString, jsonOptions);

            return imported ?? new List<ViewModel>();
        }
    }
}