using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AutoSubtitles
{
    public static class SettingsService
    {
        // Возвращает путь к settings.json рядом с exe
        public static string GetSettingsFilePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "settings.json");
        }

        // Загружает настройки с диска. Если файла нет — возвращает дефолтные.
        // Применяет защиту от нулевых значений (как было в оригинале).
        public static AppSettings Load()
        {
            string path = GetSettingsFilePath();

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, options);

                    if (loaded != null)
                    {
                        // ЗАЩИТА: если в файле нули (из-за прошлых неудачных сохранений) — заменяем на дефолты
                        if (loaded.min_silence_duration_ms == 0) loaded.min_silence_duration_ms = 2000;
                        if (loaded.min_speech_duration_ms == 0) loaded.min_speech_duration_ms = 250;
                        if (loaded.speech_pad_ms == 0) loaded.speech_pad_ms = 400;
                        if (loaded.max_speech_duration_s == 0) loaded.max_speech_duration_s = double.PositiveInfinity;
                        if (loaded.max_pause == 0) loaded.max_pause = 1.0;
                        if (loaded.vad_threshold == 0) loaded.vad_threshold = 0.5;
                        if (loaded.beam_size == 0) loaded.beam_size = 3;

                        return loaded;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Settings loading error: {ex.Message}");
                }
            }

            // Файла нет или он битый — возвращаем дефолт
            return CreateDefault();
        }

        // Сохраняет настройки на диск. Ошибки глушим в Debug (как в оригинале).
        public static void Save(AppSettings settings)
        {
            try
            {
                string path = GetSettingsFilePath();
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        // Дефолтные настройки (было в конце LoadSettings)
        public static AppSettings CreateDefault()
        {
            return new AppSettings
            {
                GGMLmodelDirectory = Path.Combine(AppContext.BaseDirectory, "models"),
                huggingface_hub_path = GetHuggingFaceHubPath(),
                HF_TOKEN = "HF_TOKEN",
                OfflineMode = false,
                beam_size = 3,
                max_pause = 1.0,
                vad_filter = false,
                vad_threshold = 0.5,
                min_silence_duration_ms = 2000,
                min_speech_duration_ms = 250,
                speech_pad_ms = 400,
                max_speech_duration_s = double.PositiveInfinity,
                MultiThreadedRendering = true
            };
        }

        // Определяет путь к кэшу HuggingFace (было в SettingsWindow.xaml.cs)
        public static string GetHuggingFaceHubPath()
        {
            // 1. Проверяем самый приоритетный прямой путь к кэшу хаба
            string hfHubCache = Environment.GetEnvironmentVariable("HF_HUB_CACHE")
                                ?? Environment.GetEnvironmentVariable("TRANSFORMERS_CACHE");
            if (!string.IsNullOrEmpty(hfHubCache))
            {
                return hfHubCache;
            }

            // 2. Проверяем общую домашнюю папку Hugging Face
            string hfHome = Environment.GetEnvironmentVariable("HF_HOME");
            if (!string.IsNullOrEmpty(hfHome))
            {
                return Path.Combine(hfHome, "hub");
            }

            // 3. Проверяем переменную XDG_CACHE_HOME (встречается на Linux/WSL)
            string xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrEmpty(xdgCache))
            {
                return Path.Combine(xdgCache, "huggingface", "hub");
            }

            // 4. Если переменные не заданы — стандартный путь в профиле пользователя
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, ".cache", "huggingface", "hub");
        }
    }
}