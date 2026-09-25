//using System.Collections.ObjectModel;
//using System.ComponentModel;
//using System.Runtime.CompilerServices;
//using System.Windows;

//namespace AutoSubtitles
//{
//    // Пока — только «мост» к MainWindow.
//    // Постепенно все свойства будут перенесены сюда, и мост исчезнет.
//    public class MainViewModel : INotifyPropertyChanged
//    {

//        public MainViewModel()
//        {
//            ModelDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "models");

//            TranscriptionLanguages.Add(new LanguageItem { DisplayName = "Auto Detect", Code = "auto" });
//            TranscriptionLanguages.Add(new LanguageItem { DisplayName = "English", Code = "en" });
//            TranscriptionLanguages.Add(new LanguageItem { DisplayName = "Русский", Code = "ru" });

//            SelectedLanguage = TranscriptionLanguages[0];
//        }

//        public void Initialize()
//        {
//            MainWindow_AppSettings = SettingsService.Load();
//            ModelScanner.ScanGgmlModels(ModelDirectory, GGML_Models);
//            ModelScanner.ScanFasterWhisperModels(MainWindow_AppSettings.huggingface_hub_path, FasterWhisper_Models);

//            // Выбор модели по умолчанию
//            var defaultGgml = GGML_Models.FirstOrDefault(m => m.Name.ToLower() == "small") ?? GGML_Models.FirstOrDefault();
//            if (defaultGgml != null) SelectedModel = defaultGgml;

//            var defaultFaster = FasterWhisper_Models.FirstOrDefault(m => m.Name.ToLower() == "small") ?? FasterWhisper_Models.FirstOrDefault();
//            if (defaultFaster != null) FasterWhisper_SelectedModel = defaultFaster;
//        }


//        public ObservableCollection<ViewModel> Subtitles { get; } = new ObservableCollection<ViewModel>();
//        public ObservableCollection<WhisperNetModel> GGML_Models { get; } = new ObservableCollection<WhisperNetModel>();
//        public ObservableCollection<FasterWhisperModel> FasterWhisper_Models { get; } = new ObservableCollection<FasterWhisperModel>();
//        public ObservableCollection<LanguageItem> TranscriptionLanguages { get; } = new ObservableCollection<LanguageItem>();

//        // Сервисы
//        private readonly PythonTranscriptionService _pythonService = new PythonTranscriptionService();
//        private readonly WhisperLocalTranscriber _localTranscriber = new WhisperLocalTranscriber();
//        private readonly ModelDownloader _modelDownloader = new ModelDownloader();

//        public TimeSpan TranscriptionStartFrom { get; set; } = TimeSpan.Zero;

//        // Состояние транскрипции
//        private CancellationTokenSource? _cts;
//        private string _currentTaskId = string.Empty;

//        // Путь к видео/аудио — используется командами
//        public string VideoPath { get; set; } = string.Empty;

//        // Длительность текущего медиа (устанавливает MainWindow перед стартом)
//        public TimeSpan VideoDuration { get; set; } = TimeSpan.Zero;

//        // Для Export_Click: MainWindow откроет ExportWindow с этими данными
//        public List<ViewModel> SubtitlesForExport => Subtitles.ToList();

//        public string ModelDirectory { get; set; } = string.Empty;

//        private AppSettings _settings = new AppSettings();
//        public AppSettings MainWindow_AppSettings
//        {
//            get => _settings;
//            set { _settings = value; OnPropertyChanged(); }
//        }

//        private WhisperNetModel? _selectedModel;
//        public WhisperNetModel? SelectedModel
//        {
//            get => _selectedModel;
//            set
//            {
//                _selectedModel = value;
//                OnPropertyChanged();
//                OnPropertyChanged(nameof(CanStartTranscription));
//            }
//        }

//        private FasterWhisperModel? _fasterWhisperSelectedModel;
//        public FasterWhisperModel? FasterWhisper_SelectedModel
//        {
//            get => _fasterWhisperSelectedModel;
//            set
//            {
//                _fasterWhisperSelectedModel = value;
//                OnPropertyChanged();
//                OnPropertyChanged(nameof(CanStartTranscription));
//            }
//        }

//        private LanguageItem? _selectedLanguage;
//        public LanguageItem? SelectedLanguage
//        {
//            get => _selectedLanguage;
//            set { _selectedLanguage = value; OnPropertyChanged(); }
//        }

//        private bool _isTranscribing;
//        public bool IsTranscribing
//        {
//            get => _isTranscribing;
//            set
//            {
//                _isTranscribing = value;
//                OnPropertyChanged();
//                OnPropertyChanged(nameof(CanStartTranscription));
//            }
//        }

//        private double _transcriptionProgress;
//        public double TranscriptionProgress
//        {
//            get => _transcriptionProgress;
//            set { _transcriptionProgress = value; OnPropertyChanged(); }
//        }

//        private bool _modelType;
//        public bool ModelType
//        {
//            get => _modelType;
//            set
//            {
//                _modelType = value;
//                OnPropertyChanged();
//                OnPropertyChanged(nameof(CanStartTranscription));
//                UpdateSpeakerColumnVisibility();
//            }
//        }

//        private bool _diarizationMode = true;
//        public bool DiarizationMode
//        {
//            get => _diarizationMode;
//            set
//            {
//                _diarizationMode = value;
//                OnPropertyChanged();
//                UpdateSpeakerColumnVisibility();
//            }
//        }

//        private bool _isAudioMode;
//        public bool IsAudioMode
//        {
//            get => _isAudioMode;
//            set { _isAudioMode = value; OnPropertyChanged(); }
//        }

//        private bool _isVerticalOrientation = true;
//        public bool IsVerticalOrientation
//        {
//            get => _isVerticalOrientation;
//            set { _isVerticalOrientation = value; OnPropertyChanged(); }
//        }

//        private string _windowTitle = "AutoSubtitles";
//        public string WindowTitle
//        {
//            get => _windowTitle;
//            set { _windowTitle = value; OnPropertyChanged(); }
//        }


//        public bool CanStartTranscription
//        {
//            get
//            {
//                if (!ModelType)
//                {
//                    return SelectedModel != null && SelectedModel.IsDownloaded && !IsTranscribing;
//                }
//                else
//                {
//                    return FasterWhisper_SelectedModel != null && !IsTranscribing;
//                }
//            }
//        }

//        public void AddSubtitleToGrid(ServerSubtitleResult seg)
//        {
//            TimeSpan startTs = TimeSpan.FromSeconds(seg.Start);
//            TimeSpan endTs = TimeSpan.FromSeconds(seg.End);

//            string timeStartStr = startTs.ToString(@"mm\:ss");
//            string timeEndStr = endTs.ToString(@"mm\:ss");
//            string rangeStr = $"[{timeStartStr} -> {timeEndStr}]";

//            Subtitles.Add(new ViewModel
//            {
//                TimeRange = rangeStr,
//                StartTime = startTs,
//                EndTime = endTs,
//                Speaker = !string.IsNullOrEmpty(seg.Speaker) ? seg.Speaker : "",
//                Text = seg.Text.Trim()
//            });
//        }

//        public void OpenFile(string path)
//        {
//            Subtitles.Clear();
//            TranscriptionProgress = 0;

//            VideoPath = path;

//            string fileName = System.IO.Path.GetFileName(path);
//            WindowTitle = $"{fileName} — AutoSubtitles";

//            string extension = System.IO.Path.GetExtension(path).ToLower();
//            string[] audioExtensions = { ".mp3", ".wav", ".aac", ".m4a", ".flac", ".ogg" };
//            IsAudioMode = audioExtensions.Contains(extension);
//        }

//        public async Task StartTranscriptionAsync()
//        {
//            if (!CanStartTranscription) return;
//            if (string.IsNullOrEmpty(VideoPath)) return;

//            Subtitles.Clear();
//            IsTranscribing = true;
//            TranscriptionProgress = 0;

//            _cts = new CancellationTokenSource();
//            var token = _cts.Token;

//            _currentTaskId = Guid.NewGuid().ToString();

//            try
//            {
//                if (!ModelType)
//                {
//                    string currentModelPath = System.IO.Path.Combine(ModelDirectory, SelectedModel!.FileName);
//                    string languageCode = SelectedLanguage?.Code ?? "auto";

//                    var progress = new Progress<double>(absSeconds =>
//                    {
//                        TranscriptionProgress = absSeconds;
//                    });


//                    await foreach (var subtitle in _localTranscriber.TranscribeAsync(
//                                    VideoPath, currentModelPath, languageCode,
//                                    TranscriptionStartFrom, progress, token))
//                    {
//                        Subtitles.Add(subtitle);
//                    }
//                }
//                else
//                {
//                    // ============================================================
//                    // РЕЖИМ 2: PYTHON BACKEND
//                    // ============================================================
//                    string modelName = FasterWhisper_SelectedModel?.Name ?? "small";
//                    string languageCode = SelectedLanguage?.Code ?? "auto";
//                    string hfToken = MainWindow_AppSettings.HF_TOKEN ?? "";
//                    bool useDiarization = DiarizationMode;

//                    if (useDiarization)
//                    {
//                        // Синхронный режим (с диаризацией)
//                        List<ServerSubtitleResult> serverSegments = await Task.Run(() =>
//                            _pythonService.TranscribeVideoAsync(
//                                _currentTaskId, VideoPath, modelName, languageCode, true,
//                                hfToken, MainWindow_AppSettings, token), token);

//                        foreach (var seg in serverSegments)
//                        {
//                            AddSubtitleToGrid(seg);
//                        }
//                    }
//                    else
//                    {
//                        // Потоковый режим (без диаризации)
//                        await Task.Run(() =>
//                            _pythonService.TranscribeVideoStreamingAsync(
//                                _currentTaskId,
//                                VideoPath,
//                                modelName,
//                                languageCode,
//                                hfToken,
//                                MainWindow_AppSettings,
//                                (chunk) =>
//                                {
//                                    // Колбэк вызывается из фонового потока.
//                                    // Маршалим в UI-поток:
//                                    Application.Current.Dispatcher.Invoke(() =>
//                                    {
//                                        AddSubtitleToGrid(chunk);
//                                        TranscriptionProgress = chunk.End;
//                                    });
//                                },
//                                token
//                            ), token);
//                    }

//                    // Финальное заполнение прогресса
//                    if (VideoDuration > TimeSpan.Zero && !token.IsCancellationRequested)
//                    {
//                        TranscriptionProgress = VideoDuration.TotalSeconds;
//                    }
//                }
//            }
//            catch (OperationCanceledException)
//            {
//                System.Diagnostics.Debug.WriteLine("Транскрипция была отменена пользователем.");
//            }
//            catch (Exception ex) when (ex is TaskCanceledException || ex.InnerException is OperationCanceledException)
//            {
//                System.Diagnostics.Debug.WriteLine($"Транскрипция отменена: {ex.Message}");
//            }
//            catch (Exception ex)
//            {
//                MessageBox.Show($"Ошибка при обработке: {ex.Message}", "Ошибка ИИ",
//                    MessageBoxButton.OK, MessageBoxImage.Error);
//            }
//            finally
//            {
//                IsTranscribing = false;
//                _cts?.Dispose();
//                _cts = null;
//                _currentTaskId = string.Empty;
//            }
//        }

//        public async Task CancelTranscriptionAsync()
//        {
//            if (!IsTranscribing) return;

//            _cts?.Cancel();
//            if (!string.IsNullOrEmpty(_currentTaskId) && ModelType)
//            {
//                string taskIdToCancel = _currentTaskId;
//                await Task.Run(() => _pythonService.CancelTranscriptionAsync(taskIdToCancel));
//            }
//        }

//        public int ImportJson(string path)
//        {
//            var imported = SubtitleImporter.ImportFromJson(path);

//            Subtitles.Clear();
//            foreach (var item in imported)
//            {
//                Subtitles.Add(item);
//            }

//            TranscriptionProgress = 0;
//            return Subtitles.Count;
//        }

//        public void ResetToDefault()
//        {
//            MainWindow_AppSettings.max_pause = 1.0;
//            MainWindow_AppSettings.vad_filter = false;
//            MainWindow_AppSettings.vad_threshold = 0.5;
//            MainWindow_AppSettings.min_silence_duration_ms = 2000;
//            MainWindow_AppSettings.min_speech_duration_ms = 250;
//            MainWindow_AppSettings.speech_pad_ms = 400;
//            MainWindow_AppSettings.max_speech_duration_s = double.PositiveInfinity;
//            MainWindow_AppSettings.beam_size = 3;

//            SettingsService.Save(MainWindow_AppSettings);
//            OnPropertyChanged(nameof(MainWindow_AppSettings));
//        }

//        public async Task DownloadModelAsync()
//        {
//            if (SelectedModel == null || !SelectedModel.CanBeDownloaded) return;

//            var model = SelectedModel;
//            model.IsDownloading = true;
//            model.DownloadProgress = 0;
//            model.DownloadStatusText = "Connecting...";

//            _cts = new CancellationTokenSource();
//            var token = _cts.Token;

//            var progress = new Progress<ModelDownloadProgress>(p =>
//            {
//                double totalMb = (double)p.TotalBytes / (1024 * 1024);
//                double currentMb = (double)p.BytesRead / (1024 * 1024);

//                if (p.TotalBytes > 0)
//                {
//                    model.DownloadProgress = Math.Min(100, Math.Floor((double)p.BytesRead / p.TotalBytes * 100));
//                    model.DownloadStatusText = $"Скачано: {currentMb:F1} МБ / {totalMb:F1} МБ ({model.DownloadProgress}%)";
//                }
//                else
//                {
//                    model.DownloadStatusText = $"Скачано: {currentMb:F1} МБ";
//                }
//            });

//            try
//            {
//                await _modelDownloader.DownloadAsync(model, ModelDirectory, progress, token);
//                model.IsDownloaded = true;
//            }
//            catch (OperationCanceledException)
//            {
//                model.DownloadStatusText = "Download cancelled";
//            }
//            catch (Exception ex)
//            {
//                // Прокидываем наверх — MainWindow покажет MessageBox
//                throw new Exception($"Download error: {ex.Message}", ex);
//            }
//            finally
//            {
//                model.IsDownloading = false;
//                _cts?.Dispose();
//                _cts = null;
//            }
//        }

//        public void CancelCurrentOperation()
//        {
//            _cts?.Cancel();
//        }

//        public void ToggleOrientation()
//        {
//            IsVerticalOrientation = !IsVerticalOrientation;
//            OrientationChanged?.Invoke();
//        }

//        public event Action? OrientationChanged;


//        public event Action? SpeakerColumnVisibilityRequested;

//        private void UpdateSpeakerColumnVisibility()
//        {
//            SpeakerColumnVisibilityRequested?.Invoke();
//        }

//        public void UpdateActiveSubtitleHighlight(TimeSpan currentPos)
//        {
//            ViewModel? currentSubtitle = null;

//            foreach (var item in Subtitles)
//            {
//                if (currentPos >= item.StartTime && currentPos < item.EndTime)
//                {
//                    item.IsCurrent = true;
//                    currentSubtitle = item;
//                }
//                else
//                {
//                    item.IsCurrent = false;
//                }
//            }

//            // Fallback: если позиция ровно на границе — ищем совпадение по <=
//            if (currentSubtitle == null)
//            {
//                foreach (var item in Subtitles)
//                {
//                    if (currentPos >= item.StartTime && currentPos <= item.EndTime)
//                    {
//                        item.IsCurrent = true;
//                        currentSubtitle = item;
//                        break;
//                    }
//                }
//            }

//            // Сообщаем наружу, что нужно проскроллить к активному субтитру
//            if (currentSubtitle != null)
//            {
//                ActiveSubtitleChanged?.Invoke(currentSubtitle);
//            }
//        }

//        public event Action<ViewModel>? ActiveSubtitleChanged;

//        public event PropertyChangedEventHandler? PropertyChanged;
//        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
//        {
//            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
//        }

//    }
//}


using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace AutoSubtitles
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public MainViewModel()
        {
            ModelDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "models");

            TranscriptionLanguages.Add(new LanguageItem { DisplayName = "Auto Detect", Code = "auto" });
            TranscriptionLanguages.Add(new LanguageItem { DisplayName = "English", Code = "en" });
            TranscriptionLanguages.Add(new LanguageItem { DisplayName = "Русский", Code = "ru" });

            SelectedLanguage = TranscriptionLanguages[0];
        }

        public void Initialize()
        {
            MainWindow_AppSettings = SettingsService.Load();
            ModelScanner.ScanGgmlModels(ModelDirectory, GGML_Models);
            ModelScanner.ScanFasterWhisperModels(MainWindow_AppSettings.huggingface_hub_path, FasterWhisper_Models);

            var defaultGgml = GGML_Models.FirstOrDefault(m => m.Name.ToLower() == "small") ?? GGML_Models.FirstOrDefault();
            if (defaultGgml != null) SelectedModel = defaultGgml;

            var defaultFaster = FasterWhisper_Models.FirstOrDefault(m => m.Name.ToLower() == "small") ?? FasterWhisper_Models.FirstOrDefault();
            if (defaultFaster != null) FasterWhisper_SelectedModel = defaultFaster;
        }

        // ============================================================
        // Коллекции
        // ============================================================
        public ObservableCollection<ViewModel> Subtitles { get; } = new();
        public ObservableCollection<WhisperNetModel> GGML_Models { get; } = new();
        public ObservableCollection<FasterWhisperModel> FasterWhisper_Models { get; } = new();
        public ObservableCollection<LanguageItem> TranscriptionLanguages { get; } = new();

        // ============================================================
        // Сервисы
        // ============================================================
        private readonly PythonTranscriptionService _pythonService = new();
        private readonly WhisperLocalTranscriber _localTranscriber = new();
        private readonly ModelDownloader _modelDownloader = new();

        // ============================================================
        // Состояние
        // ============================================================

        private CancellationTokenSource? _cts;
        private string _currentTaskId = string.Empty;

        public string VideoPath { get; set; } = string.Empty;
        public List<ViewModel> SubtitlesForExport => Subtitles.ToList();
        public string ModelDirectory { get; set; } = string.Empty;

        // ============================================================
        // Свойства привязки
        // ============================================================
        private AppSettings _settings = new();
        public AppSettings MainWindow_AppSettings
        {
            get => _settings;
            set { _settings = value; OnPropertyChanged(); }
        }

        private WhisperNetModel? _selectedModel;
        public WhisperNetModel? SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (_selectedModel != null)
                    _selectedModel.PropertyChanged -= OnSelectedModelPropertyChanged;

                _selectedModel = value;

                if (_selectedModel != null)
                    _selectedModel.PropertyChanged += OnSelectedModelPropertyChanged;

                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartTranscription));
            }
        }

        private void OnSelectedModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WhisperNetModel.IsDownloaded)
                || e.PropertyName == nameof(WhisperNetModel.IsDownloading))
            {
                OnPropertyChanged(nameof(CanStartTranscription));
            }
        }

        private FasterWhisperModel? _fasterWhisperSelectedModel;
        public FasterWhisperModel? FasterWhisper_SelectedModel
        {
            get => _fasterWhisperSelectedModel;
            set
            {
                if (_fasterWhisperSelectedModel != null)
                    _fasterWhisperSelectedModel.PropertyChanged -= OnFasterWhisperSelectedModelPropertyChanged;

                _fasterWhisperSelectedModel = value;

                if (_fasterWhisperSelectedModel != null)
                    _fasterWhisperSelectedModel.PropertyChanged += OnFasterWhisperSelectedModelPropertyChanged;

                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartTranscription));
            }
        }

        private void OnFasterWhisperSelectedModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FasterWhisperModel.IsDownloaded))
                OnPropertyChanged(nameof(CanStartTranscription));
        }

        private LanguageItem? _selectedLanguage;
        public LanguageItem? SelectedLanguage
        {
            get => _selectedLanguage;
            set { _selectedLanguage = value; OnPropertyChanged(); }
        }

        private bool _isTranscribing;
        public bool IsTranscribing
        {
            get => _isTranscribing;
            set
            {
                _isTranscribing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartTranscription));
                OnPropertyChanged(nameof(CanCancelTranscription));
            }
        }

        private bool _isCancelling;
        public bool IsCancelling
        {
            get => _isCancelling;
            set
            {
                _isCancelling = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanCancelTranscription));
            }
        }

        public bool CanCancelTranscription => IsTranscribing && !IsCancelling;


        private bool _modelType;
        public bool ModelType
        {
            get => _modelType;
            set
            {
                _modelType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartTranscription));
                UpdateSpeakerColumnVisibility();

                TranscriptionProgress = 0;
                TranscriptionStartFrom = TimeSpan.Zero;
            }
        }

        private bool _diarizationMode = true;
        public bool DiarizationMode
        {
            get => _diarizationMode;
            set
            {
                _diarizationMode = value;
                OnPropertyChanged();
                UpdateSpeakerColumnVisibility();
            }
        }

        private bool _isAudioMode;
        public bool IsAudioMode
        {
            get => _isAudioMode;
            set { _isAudioMode = value; OnPropertyChanged(); }
        }

        private bool _isVerticalOrientation = true;
        public bool IsVerticalOrientation
        {
            get => _isVerticalOrientation;
            set { _isVerticalOrientation = value; OnPropertyChanged(); }
        }


        private string _windowTitle = "AutoSubtitles";
        public string WindowTitle
        {
            get => _windowTitle;
            set { _windowTitle = value; OnPropertyChanged(); }
        }

        public bool CanStartTranscription
        {
            get
            {
                if (!ModelType)
                    return SelectedModel != null && SelectedModel.IsDownloaded && !IsTranscribing;
                else
                    return FasterWhisper_SelectedModel != null && !IsTranscribing;
            }
        }



        private TimeSpan _videoDuration = TimeSpan.Zero;
        public TimeSpan VideoDuration
        {
            get => _videoDuration;
            set
            {
                _videoDuration = value;
                OnPropertyChanged();
                UpdateProgressRatios();
            }
        }

        private TimeSpan _transcriptionStartFrom = TimeSpan.Zero;
        public TimeSpan TranscriptionStartFrom
        {
            get => _transcriptionStartFrom;
            set
            {
                _transcriptionStartFrom = value;
                OnPropertyChanged();
                UpdateProgressRatios();
            }
        }

        private double _transcriptionProgress;
        public double TranscriptionProgress
        {
            get => _transcriptionProgress;
            set
            {
                _transcriptionProgress = value;
                OnPropertyChanged();
                UpdateProgressRatios();
            }
        }

        // Доли для Grid-колонок (0..1)
        private double _progressStartRatio;
        public double ProgressStartRatio
        {
            get => _progressStartRatio;
            private set { _progressStartRatio = value; OnPropertyChanged(); }
        }

        private double _progressFillRatio;
        public double ProgressFillRatio
        {
            get => _progressFillRatio;
            private set { _progressFillRatio = value; OnPropertyChanged(); }
        }

        private double _progressEndRatio;
        public double ProgressEndRatio
        {
            get => _progressEndRatio;
            private set { _progressEndRatio = value; OnPropertyChanged(); }
        }

        private void UpdateProgressRatios()
        {
            double total = VideoDuration.TotalSeconds;
            if (total <= 0)
            {
                ProgressStartRatio = 0;
                ProgressFillRatio = 0;
                ProgressEndRatio = 0;
                return;
            }

            double startSec = TranscriptionStartFrom.TotalSeconds;
            double progressSec = TranscriptionProgress;

            // Зажимаем в [0, 1]
            double startRatio = Math.Max(0, Math.Min(1, startSec / total));
            double progressRatio = Math.Max(startRatio, Math.Min(1, progressSec / total));

            ProgressStartRatio = startRatio;
            ProgressFillRatio = progressRatio - startRatio;
            ProgressEndRatio = 1 - progressRatio;
        }

        // ============================================================
        // Открытие файла
        // ============================================================
        public void OpenFile(string path)
        {
            Subtitles.Clear();
            TranscriptionProgress = 0;

            VideoPath = path;

            string fileName = System.IO.Path.GetFileName(path);
            WindowTitle = $"{fileName} — AutoSubtitles";

            string extension = System.IO.Path.GetExtension(path).ToLower();
            string[] audioExtensions = { ".mp3", ".wav", ".aac", ".m4a", ".flac", ".ogg" };
            IsAudioMode = audioExtensions.Contains(extension);
        }

        // ============================================================
        // Транскрипция
        // ============================================================
        public async Task StartTranscriptionAsync()
        {
            if (!CanStartTranscription) return;
            if (string.IsNullOrEmpty(VideoPath)) return;

            if (ModelType)
                TranscriptionStartFrom = TimeSpan.Zero;

            Subtitles.Clear();
            IsCancelling = false;
            IsTranscribing = true;
            TranscriptionProgress = 0;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _currentTaskId = Guid.NewGuid().ToString();

            try
            {
                if (!ModelType)
                {
                    string currentModelPath = System.IO.Path.Combine(ModelDirectory, SelectedModel!.FileName);
                    string languageCode = SelectedLanguage?.Code ?? "auto";

                    var progress = new Progress<double>(absSeconds =>
                    {
                        TranscriptionProgress = absSeconds;
                    });

                    await foreach (var subtitle in _localTranscriber.TranscribeAsync(
                                    VideoPath, currentModelPath, languageCode,
                                    TranscriptionStartFrom, progress, token))
                    {
                        Subtitles.Add(subtitle);
                    }
                }
                else
                {
                    // Python-бэкенд — оставлен как есть
                    string modelName = FasterWhisper_SelectedModel?.Name ?? "small";
                    string languageCode = SelectedLanguage?.Code ?? "auto";
                    string hfToken = MainWindow_AppSettings.HF_TOKEN ?? "";
                    bool useDiarization = DiarizationMode;

                    if (useDiarization)
                    {
                        List<ServerSubtitleResult> serverSegments = await Task.Run(() =>
                            _pythonService.TranscribeVideoAsync(
                                _currentTaskId, VideoPath, modelName, languageCode, true,
                                hfToken, MainWindow_AppSettings, token), token);

                        foreach (var seg in serverSegments)
                            AddSubtitleToGrid(seg);
                    }
                    else
                    {
                        await Task.Run(() =>
                            _pythonService.TranscribeVideoStreamingAsync(
                                _currentTaskId, VideoPath, modelName, languageCode,
                                hfToken, MainWindow_AppSettings,
                                (chunk) =>
                                {
                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        AddSubtitleToGrid(chunk);
                                        TranscriptionProgress = chunk.End;
                                    });
                                },
                                token
                            ), token);
                    }

                    if (VideoDuration > TimeSpan.Zero && !token.IsCancellationRequested)
                        TranscriptionProgress = VideoDuration.TotalSeconds;
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("Транскрипция была отменена пользователем.");
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex.InnerException is OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"Транскрипция отменена: {ex.Message}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при обработке: {ex.Message}", "Ошибка ИИ",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsCancelling = false;   // снимаем «отмену» — задача завершилась
                IsTranscribing = false;
                _cts?.Dispose();
                _cts = null;
                _currentTaskId = string.Empty;
            }
        }

        public async Task CancelTranscriptionAsync()
        {
            if (!IsTranscribing) return;
            if (IsCancelling) return;   // уже отменяем — второй раз не нужно

            IsCancelling = true;

            try
            {
                _cts?.Cancel();

                if (!string.IsNullOrEmpty(_currentTaskId) && ModelType)
                {
                    string taskIdToCancel = _currentTaskId;
                    await Task.Run(() => _pythonService.CancelTranscriptionAsync(taskIdToCancel));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при отмене: {ex.Message}");
            }
            // IsCancelling НЕ сбрасываем здесь — сбросим в finally StartTranscriptionAsync,
            // когда фоновая задача реально завершится.
        }

        public void CancelCurrentOperation()
        {
            _cts?.Cancel();
        }

        // ============================================================
        // Python-режим (диаризация)
        // ============================================================
        public void AddSubtitleToGrid(ServerSubtitleResult seg)
        {
            TimeSpan startTs = TimeSpan.FromSeconds(seg.Start);
            TimeSpan endTs = TimeSpan.FromSeconds(seg.End);

            string timeStartStr = startTs.ToString(@"mm\:ss");
            string timeEndStr = endTs.ToString(@"mm\:ss");
            string rangeStr = $"[{timeStartStr} -> {timeEndStr}]";

            Subtitles.Add(new ViewModel
            {
                TimeRange = rangeStr,
                StartTime = startTs,
                EndTime = endTs,
                Speaker = !string.IsNullOrEmpty(seg.Speaker) ? seg.Speaker : "",
                Text = seg.Text.Trim()
            });
        }

        // ============================================================
        // Импорт / экспорт
        // ============================================================
        public int ImportJson(string path)
        {
            var imported = SubtitleImporter.ImportFromJson(path);

            Subtitles.Clear();
            foreach (var item in imported)
                Subtitles.Add(item);

            TranscriptionProgress = 0;
            return Subtitles.Count;
        }

        public void ResetToDefault()
        {
            MainWindow_AppSettings.max_pause = 1.0;
            MainWindow_AppSettings.vad_filter = false;
            MainWindow_AppSettings.vad_threshold = 0.5;
            MainWindow_AppSettings.min_silence_duration_ms = 2000;
            MainWindow_AppSettings.min_speech_duration_ms = 250;
            MainWindow_AppSettings.speech_pad_ms = 400;
            MainWindow_AppSettings.max_speech_duration_s = double.PositiveInfinity;
            MainWindow_AppSettings.beam_size = 3;

            SettingsService.Save(MainWindow_AppSettings);
            OnPropertyChanged(nameof(MainWindow_AppSettings));
        }

        // ============================================================
        // Скачивание модели
        // ============================================================
        public async Task DownloadModelAsync()
        {
            if (SelectedModel == null || !SelectedModel.CanBeDownloaded) return;

            var model = SelectedModel;
            model.IsDownloading = true;
            model.DownloadProgress = 0;
            model.DownloadStatusText = "Connecting...";

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                double totalMb = (double)p.TotalBytes / (1024 * 1024);
                double currentMb = (double)p.BytesRead / (1024 * 1024);

                if (p.TotalBytes > 0)
                {
                    model.DownloadProgress = Math.Min(100, Math.Floor((double)p.BytesRead / p.TotalBytes * 100));
                    model.DownloadStatusText = $"Скачано: {currentMb:F1} МБ / {totalMb:F1} МБ ({model.DownloadProgress}%)";
                }
                else
                {
                    model.DownloadStatusText = $"Скачано: {currentMb:F1} МБ";
                }
            });

            try
            {
                await _modelDownloader.DownloadAsync(model, ModelDirectory, progress, token);
                model.IsDownloaded = true;
            }
            catch (OperationCanceledException)
            {
                model.DownloadStatusText = "Download cancelled";
            }
            catch (Exception ex)
            {
                throw new Exception($"Download error: {ex.Message}", ex);
            }
            finally
            {
                model.IsDownloading = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ============================================================
        // Ориентация и колонки
        // ============================================================
        public void ToggleOrientation()
        {
            IsVerticalOrientation = !IsVerticalOrientation;
            OrientationChanged?.Invoke();
        }

        public event Action? OrientationChanged;
        public event Action? SpeakerColumnVisibilityRequested;

        private void UpdateSpeakerColumnVisibility()
        {
            SpeakerColumnVisibilityRequested?.Invoke();
        }

        // ============================================================
        // Подсветка активного субтитра
        // ============================================================
        public void UpdateActiveSubtitleHighlight(TimeSpan currentPos)
        {
            ViewModel? currentSubtitle = null;

            foreach (var item in Subtitles)
            {
                bool isCurrent = currentPos >= item.StartTime && currentPos < item.EndTime;
                item.IsCurrent = isCurrent;
                if (isCurrent) currentSubtitle = item;
            }

            if (currentSubtitle == null)
            {
                foreach (var item in Subtitles)
                {
                    if (currentPos >= item.StartTime && currentPos <= item.EndTime)
                    {
                        item.IsCurrent = true;
                        currentSubtitle = item;
                        break;
                    }
                }
            }

            if (currentSubtitle != null)
                ActiveSubtitleChanged?.Invoke(currentSubtitle);
        }

        public event Action<ViewModel>? ActiveSubtitleChanged;


        // ============================================================
        // INotifyPropertyChanged
        // ============================================================
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}