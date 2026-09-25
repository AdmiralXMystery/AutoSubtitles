//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Runtime.CompilerServices;
//using System.Threading;
//using System.Threading.Tasks;
//using NAudio.Wave;
//using NAudio.Wave.SampleProviders;
//using Whisper.net;

//namespace AutoSubtitles
//{
//    // Локальная транскрипция через Whisper.net (GGML-модели).
//    // Не знает про WPF — общается через IAsyncEnumerable и IProgress.
//    public class WhisperLocalTranscriber
//    {
//        // Транскрибирует аудио из videoPath с помощью модели modelPath.
//        // Возвращает поток сегментов; прогресс (в секундах) уходит в progress.
//        // Отмена — через token.
//        public async IAsyncEnumerable<ViewModel> TranscribeAsync(
//            string videoPath,
//            string modelPath,
//            string languageCode,
//            IProgress<double>? progress,
//            [EnumeratorCancellation] CancellationToken token)
//        {
//            // ============================================================
//            // 1. Читаем аудио из медиафайла, ресемплим в 16 kHz mono
//            // ============================================================
//            float[] finalSamplesArray;
//            double totalSeconds;

//            using (var reader = new MediaFoundationReader(videoPath))
//            {
//                totalSeconds = reader.TotalTime.TotalSeconds;

//                var resampler = new WdlResamplingSampleProvider(reader.ToSampleProvider(), 16000);
//                var monoProvider = resampler.ToMono();

//                var audioSamples = new List<float>();
//                float[] readBuffer = new float[4000];

//                while (true)
//                {
//                    token.ThrowIfCancellationRequested();

//                    int samplesRead = monoProvider.Read(readBuffer, 0, readBuffer.Length);
//                    if (samplesRead == 0) break;

//                    for (int i = 0; i < samplesRead; i++)
//                        audioSamples.Add(readBuffer[i]);
//                }

//                finalSamplesArray = audioSamples.ToArray();
//            }

//            // ============================================================
//            // 2. Создаём процессор Whisper.net
//            // ============================================================
//            using var whisperFactory = WhisperFactory.FromPath(modelPath);

//            var builder = whisperFactory.CreateBuilder().WithThreads(4);

//            if (languageCode == "auto")
//                builder.WithLanguageDetection();
//            else
//                builder.WithLanguage(languageCode);

//            var processor = builder.Build();

//            try
//            {
//                // ============================================================
//                // 3. Итерируемся по сегментам и отдаём наружу
//                // ============================================================
//                await foreach (var segment in processor.ProcessAsync(finalSamplesArray, token))
//                {
//                    if (string.IsNullOrWhiteSpace(segment.Text))
//                        continue;

//                    string timeStart = segment.Start.ToString(@"mm\:ss");
//                    string timeEnd = segment.End.ToString(@"mm\:ss");

//                    if (totalSeconds > 0)
//                    {
//                        double currentProcessedSecond = segment.End.TotalSeconds;
//                        progress?.Report(Math.Min(totalSeconds, currentProcessedSecond));
//                    }

//                    yield return new ViewModel
//                    {
//                        TimeRange = $"[{timeStart} -> {timeEnd}]",
//                        Text = segment.Text.Trim(),
//                        StartTime = segment.Start,
//                        EndTime = segment.End
//                    };
//                }

//                // Финальный прогресс — 100%
//                if (!token.IsCancellationRequested && totalSeconds > 0)
//                {
//                    progress?.Report(totalSeconds);
//                }
//            }
//            finally
//            {
//                // Безопасное асинхронное освобождение ресурсов, как требует Whisper.net
//                if (processor != null)
//                {
//                    await processor.DisposeAsync();
//                }
//            }
//        }
//    }
//}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;

namespace AutoSubtitles
{
    public class WhisperLocalTranscriber
    {
        private const int SampleRate = 16000;

        public async IAsyncEnumerable<ViewModel> TranscribeAsync(
            string videoPath,
            string modelPath,
            string languageCode,
            TimeSpan startFrom,
            IProgress<double>? progress,
            [EnumeratorCancellation] CancellationToken token)
        {
            var channel = Channel.CreateUnbounded<ViewModel>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

            // Вся работа — в фоне. UI-поток не блокируется.
            var backgroundTask = Task.Run(async () =>
            {
                try
                {
                    await RunTranscriptionAsync(
                        videoPath, modelPath, languageCode, startFrom,
                        progress, token, channel.Writer);
                    channel.Writer.TryComplete();
                }
                catch (OperationCanceledException)
                {
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            });

            try
            {
                await foreach (var item in channel.Reader.ReadAllAsync(CancellationToken.None))
                {
                    token.ThrowIfCancellationRequested();
                    yield return item;
                }
            }
            finally
            {
                // Всегда дожидаемся фоновой задачи, чтобы не оставить висящих процессов
                try
                {
                    await backgroundTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка фоновой транскрипции: {ex}");
                }
            }
        }

        private async Task RunTranscriptionAsync(
            string videoPath,
            string modelPath,
            string languageCode,
            TimeSpan startFrom,
            IProgress<double>? progress,
            CancellationToken token,
            ChannelWriter<ViewModel> writer)
        {
            token.ThrowIfCancellationRequested();

            // ============================================================
            // 1. Чтение аудио в память
            // ============================================================
            float[] samples;
            double totalSeconds;

            using (var reader = new MediaFoundationReader(videoPath))
            {
                totalSeconds = reader.TotalTime.TotalSeconds;

                if (startFrom > TimeSpan.Zero)
                    reader.CurrentTime = startFrom;

                var resampler = new WdlResamplingSampleProvider(reader.ToSampleProvider(), SampleRate);
                var monoProvider = resampler.ToMono();

                var buffer = new List<float>();
                float[] readBuf = new float[SampleRate * 4];

                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    int read = monoProvider.Read(readBuf, 0, readBuf.Length);
                    if (read == 0) break;

                    for (int i = 0; i < read; i++)
                        buffer.Add(readBuf[i]);
                }

                samples = buffer.ToArray();
            }

            token.ThrowIfCancellationRequested();

            // ============================================================
            // 2. Прогон через Whisper
            // ============================================================
            using var whisperFactory = WhisperFactory.FromPath(modelPath);

            var builder = whisperFactory.CreateBuilder().WithThreads(4);
            if (languageCode == "auto")
                builder.WithLanguageDetection();
            else
                builder.WithLanguage(languageCode);

            var processor = builder.Build();

            try
            {
                await foreach (var segment in processor.ProcessAsync(samples, token).ConfigureAwait(false))
                {
                    if (string.IsNullOrWhiteSpace(segment.Text))
                        continue;

                    TimeSpan segStart = segment.Start + startFrom;
                    TimeSpan segEnd = segment.End + startFrom;

                    if (totalSeconds > 0)
                        progress?.Report(Math.Min(totalSeconds, segEnd.TotalSeconds));

                    writer.TryWrite(new ViewModel
                    {
                        TimeRange = $"[{segStart:mm\\:ss} -> {segEnd:mm\\:ss}]",
                        Text = segment.Text.Trim(),
                        StartTime = segStart,
                        EndTime = segEnd
                    });
                }

                if (!token.IsCancellationRequested && totalSeconds > 0)
                    progress?.Report(totalSeconds);
            }
            finally
            {
                // Ждём освобождения — мы в фоновой задаче, UI не блокируется
                try
                {
                    await processor.DisposeAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { }
                catch (OperationCanceledException) { }
            }
        }
    }
}