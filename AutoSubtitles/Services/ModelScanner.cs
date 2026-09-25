using System.Collections.ObjectModel;
using System.IO;

namespace AutoSubtitles
{
    public static class ModelScanner
    {
        // Сканирует папку с GGML-моделями (Whisper.Net) и заполняет целевую коллекцию.
        // Если папки нет — создаёт её и оставляет все модели со статусом "не скачано".
        public static void ScanGgmlModels(string modelDirectory, ObservableCollection<WhisperNetModel> target)
        {
            target.Clear();

            if (!Directory.Exists(modelDirectory))
            {
                try
                {
                    Directory.CreateDirectory(modelDirectory);
                }
                catch
                {
                    // Если создать папку не удалось — просто выходим,
                    // модели будут показаны как "не скачано" при следующем вызове
                }

                // Даже если папки нет, показываем список моделей как "не скачано"
                AddGgmlModels(target, modelDirectory);
                return;
            }

            AddGgmlModels(target, modelDirectory);
        }

        private static void AddGgmlModels(ObservableCollection<WhisperNetModel> target, string modelDirectory)
        {
            string[] modelNames = { "tiny", "base", "small", "medium", "large" };

            foreach (var name in modelNames)
            {
                var model = new WhisperNetModel { Name = name };
                string fullPath = Path.Combine(modelDirectory, model.FileName);
                model.IsDownloaded = File.Exists(fullPath);
                target.Add(model);
            }
        }

        // Сканирует кэш HuggingFace Hub и заполняет коллекцию FasterWhisper-моделей.
        // Модель считается доступной, если существует папка snapshots с хотя бы одним подкаталогом.
        public static void ScanFasterWhisperModels(string hfRootPath, ObservableCollection<FasterWhisperModel> target)
        {
            // Предустановленный список размеров моделей faster-whisper
            string[] modelNames = { "tiny", "base", "small", "medium", "large-v1", "large-v2", "large-v3" };

            target.Clear();

            // Если путь пустой или директория физически не существует,
            // добавляем модели со статусом "Не скачано"
            if (string.IsNullOrEmpty(hfRootPath) || !Directory.Exists(hfRootPath))
            {
                foreach (var name in modelNames)
                {
                    target.Add(new FasterWhisperModel { Name = name, IsDownloaded = false });
                }
                return;
            }

            foreach (var name in modelNames)
            {
                var model = new FasterWhisperModel { Name = name };

                // Формируем полный ожидаемый путь к папке модели:
                // Например: C:\Users\User\.cache\huggingface\hub\models--Systran--faster-whisper-medium
                string expectedModelFolderPath = Path.Combine(hfRootPath, model.HuggingFaceFolderName);

                // Модель считается доступной (скачанной), если папка существует
                // и внутри нее есть папка "snapshots" (признак успешной загрузки через HF Hub)
                if (Directory.Exists(expectedModelFolderPath))
                {
                    string snapshotsPath = Path.Combine(expectedModelFolderPath, "snapshots");

                    // Проверяем, что snapshots существует и не пустой
                    if (Directory.Exists(snapshotsPath) && Directory.GetDirectories(snapshotsPath).Length > 0)
                    {
                        model.IsDownloaded = true;
                    }
                }

                target.Add(model);
            }
        }
    }
}