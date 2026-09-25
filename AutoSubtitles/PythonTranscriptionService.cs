using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;


namespace AutoSubtitles
{

    public class PythonTranscriptionService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "http://127.0.0.1:8000";

        public PythonTranscriptionService()
        {
            _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        }

        public async Task<List<ServerSubtitleResult>> TranscribeVideoAsync(
            string taskId,
            string videoPath,
            string modelName,
            string languageCode,
            bool diarizationOn,
            string hfToken,
            AppSettings settings,
            CancellationToken token)
        {
            string transcribeUrl = $"{_baseUrl}/api/transcribe-local";

            var requestPayload = new
            {
                task_id = taskId,
                file_path = videoPath,
                output_dir = Path.GetDirectoryName(videoPath) ?? AppContext.BaseDirectory,
                model = modelName,
                language = languageCode,
                beam_size = settings.beam_size,
                enable_diarization = diarizationOn,
                offline_mode = false,
                hf_token = string.IsNullOrWhiteSpace(hfToken) ? null : hfToken,

                max_pause = settings.max_pause,
                vad_filter = settings.vad_filter,
                vad_threshold = settings.vad_threshold,
                min_silence_duration_ms = settings.min_silence_duration_ms,
                min_speech_duration_ms = settings.min_speech_duration_ms,
                speech_pad_ms = settings.speech_pad_ms,
                max_speech_duration_s = double.IsPositiveInfinity(settings.max_speech_duration_s) ? 999999 : settings.max_speech_duration_s
            };

            string jsonPayload = JsonSerializer.Serialize(requestPayload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(transcribeUrl, content, token);

            if (response.IsSuccessStatusCode)
            {
                string responseText = await response.Content.ReadAsStringAsync(token);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var serverResponse = JsonSerializer.Deserialize<ServerResponseRoot>(responseText, options);

                return serverResponse?.Segments ?? new List<ServerSubtitleResult>();
            }
            else
            {
                string errorText = await response.Content.ReadAsStringAsync(token);
                throw new Exception($"The server returned an error ({response.StatusCode}): {errorText}");
            }
        }

        public async Task CancelTranscriptionAsync(string taskId)
        {
            string cancelUrl = $"{_baseUrl}/api/cancel";
            var cancelPayload = new { task_id = taskId };
            string jsonPayload = JsonSerializer.Serialize(cancelPayload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                await _httpClient.PostAsync(cancelUrl, content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"The cancellation request could not be sent: {ex.Message}");
            }
        }

        public async Task TranscribeVideoStreamingAsync(
                                string taskId,
                                string videoPath,
                                string modelName,
                                string languageCode,
                                string hfToken,
                                AppSettings settings,
                                Action<ServerSubtitleResult> onChunkReceived,
                                CancellationToken token)
        {
            string transcribeUrl = $"{_baseUrl}/api/transcribe-local";

            var requestPayload = new
            {
                task_id = taskId,
                file_path = videoPath,
                output_dir = Path.GetDirectoryName(videoPath) ?? AppContext.BaseDirectory,
                model = modelName,
                language = languageCode,
                beam_size = settings.beam_size,
                enable_diarization = false,
                offline_mode = false,
                hf_token = string.IsNullOrWhiteSpace(hfToken) ? null : hfToken,

                max_pause = settings.max_pause,
                vad_filter = settings.vad_filter,
                vad_threshold = settings.vad_threshold,
                min_silence_duration_ms = settings.min_silence_duration_ms,
                min_speech_duration_ms = settings.min_speech_duration_ms,
                speech_pad_ms = settings.speech_pad_ms,
                max_speech_duration_s = double.IsPositiveInfinity(settings.max_speech_duration_s) ? 999999 : settings.max_speech_duration_s
            };

            string jsonPayload = JsonSerializer.Serialize(requestPayload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, transcribeUrl) { Content = content };
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

            if (!response.IsSuccessStatusCode)
            {
                string errorText = await response.Content.ReadAsStringAsync(token);
                throw new Exception($"The server returned an error ({response.StatusCode}): {errorText}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                token.ThrowIfCancellationRequested();
                string? line = await reader.ReadLineAsync(token);

                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("data: "))
                {
                    string jsonChunk = line.Substring(6).Trim();

                    try
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var chunk = JsonSerializer.Deserialize<ServerSubtitleResult>(jsonChunk, options);

                        if (chunk != null)
                        {
                            onChunkReceived(chunk);
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }
            }
        }

    }

    public class ServerResponseRoot
    {
        public string Status { get; set; } = string.Empty;
        public string TaskId { get; set; } = string.Empty;
        public List<ServerSubtitleResult> Segments { get; set; } = new List<ServerSubtitleResult>();
    }

    public class ServerSubtitleResult
    {
        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("speaker")]
        public string? Speaker { get; set; }
    }


}
