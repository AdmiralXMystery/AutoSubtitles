using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net.Ggml;

namespace AutoSubtitles
{
    public readonly record struct ModelDownloadProgress(long BytesRead, long TotalBytes);

    public class ModelDownloader
    {

        public async Task DownloadAsync(
            WhisperNetModel model,
            string targetDirectory,
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken token)
        {
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            string modelPath = Path.Combine(targetDirectory, model.FileName);
            GgmlType modelType = MapToGgmlType(model.Name);
            long estimatedSize = GetEstimatedModelSize(modelType);

            using var httpClient = new HttpClient();

            using var modelStream = await new WhisperGgmlDownloader(httpClient)
                .GetGgmlModelAsync(modelType, cancellationToken: token);

            using var fileStream = File.OpenWrite(modelPath);
            byte[] buffer = new byte[8192];
            long totalBytesRead = 0;
            int bytesRead;

            try
            {
                while ((bytesRead = await modelStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                    totalBytesRead += bytesRead;

                    progress?.Report(new ModelDownloadProgress(totalBytesRead, estimatedSize));
                }
            }
            catch
            {

                fileStream.Close();

                try
                {
                    if (File.Exists(modelPath))
                        File.Delete(modelPath);
                }
                catch
                {
                }

                throw;
            }
        }

        private static GgmlType MapToGgmlType(string name)
        {
            return name.ToLower() switch
            {
                "tiny" => GgmlType.Tiny,
                "small" => GgmlType.Small,
                "medium" => GgmlType.Medium,
                "large" => GgmlType.LargeV1,
                _ => GgmlType.Base
            };
        }

        private static long GetEstimatedModelSize(GgmlType type)
        {
            return type switch
            {
                GgmlType.Tiny => 75_000_000,
                GgmlType.Base => 145_000_000,
                GgmlType.Small => 466_000_000,
                GgmlType.Medium => 1_500_000_000,
                GgmlType.LargeV1 => 2_900_000_000,
                _ => 145_000_000
            };
        }
    }
}