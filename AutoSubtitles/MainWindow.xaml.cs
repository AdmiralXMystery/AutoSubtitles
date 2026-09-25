using Microsoft.Win32;
using NAudio.Wave;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Linq;


namespace AutoSubtitles
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer timer;
        private bool isUserMovingSlider = false;

        private readonly PythonServerLauncher _pythonServerLauncher = new PythonServerLauncher();
        private readonly MainViewModel _vm;
        private List<ViewModel>? _pendingMultiSelection;

        private TimeSpan _savedVideoPosition = TimeSpan.Zero;

        private CancellationTokenSource _waveformCts;
        private Rectangle _playbackMarker;

        public MainWindow()
        {
            InitializeComponent();

            _vm = new MainViewModel();
            _vm.Initialize();
            this.DataContext = _vm;

            subtitlesGrid.ItemsSource = _vm.Subtitles;

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += Timer_Tick;

            _vm.SpeakerColumnVisibilityRequested += UpdateSpeakerColumnVisibility;
            _vm.OrientationChanged += ApplyOrientation;
            _vm.ActiveSubtitleChanged += subtitle => subtitlesGrid.ScrollIntoView(subtitle);

            if (_vm.SelectedModel == null && _vm.GGML_Models.Count > 0)
            {
                _vm.SelectedModel = _vm.GGML_Models[0];
            }

            if (_vm.FasterWhisper_SelectedModel == null && _vm.FasterWhisper_Models.Count > 0)
            {
                _vm.FasterWhisper_SelectedModel = _vm.FasterWhisper_Models[0];
            }

            _playbackMarker = PlaybackMarker;

            _pythonServerLauncher.LogReceived += AppendLogLine;

            string serverExePath = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "auto_subtitles_python_server",
                "auto_subtitles_python_server.exe");

            bool serverStarted = _pythonServerLauncher.Start(serverExePath);

            if (!serverStarted)
            {
                MessageBox.Show(
                    "The local AI server failed to start",
                    "AI launch error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

        }

        private async void Open_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Media files (*.mp4;*.wmv;*.avi;*.mp3;*.wav;*.aac;*.m4a;*.flac;*.ogg)|*.mp4;*.wmv;*.avi;*.mp3;*.wav;*.aac;*.m4a;*.flac;*.ogg|All files (*.*)|*.*";

            if (openFileDialog.ShowDialog() != true) return;

            timer.Stop();
            timelineSlider.Value = 0;
            timeLabel.Text = "00:00 / 00:00";
            _savedVideoPosition = TimeSpan.Zero;

            myMediaElement.Stop();
            myMediaElement.Position = TimeSpan.Zero;
            myMediaElement.Volume = 0;

            // ← Вся «чистая» логика теперь в VM
            _vm.OpenFile(openFileDialog.FileName);

            if (_vm.IsAudioMode)
            {
                // АУДИО РЕЖИМ
                myMediaElement.Source = new Uri(_vm.VideoPath);
                myMediaElement.Visibility = Visibility.Collapsed;
                WaveformGrid.Visibility = Visibility.Visible;
                myMediaElement.Volume = volumeSlider.Value;

                if (myMediaElement.NaturalDuration.HasTimeSpan)
                {
                    TimeSpan duration = myMediaElement.NaturalDuration.TimeSpan;
                    timelineSlider.Maximum = duration.TotalSeconds;
                    timeLabel.Text = $"00:00 / {TimeFormatter.FormatTime(duration)}";
                }

                await RenderWaveformAsync(_vm.VideoPath);
            }
            else
            {
                // ВИДЕО РЕЖИМ
                myMediaElement.Visibility = Visibility.Visible;
                WaveformGrid.Visibility = Visibility.Collapsed;
                myMediaElement.Source = new Uri(_vm.VideoPath);
                myMediaElement.Play();
            }
        }

        private async Task RenderWaveformAsync(string filePath)
        {
            _waveformCts?.Cancel();
            _waveformCts = new CancellationTokenSource();
            var token = _waveformCts.Token;

            double scale = 2;
            int width = (int)(this.ActualWidth * 0.7 * scale);
            int height = (int)(this.ActualHeight * 0.2 * scale);

            if (width <= 0) width = 1920;
            if (height <= 0) height = 400;

            // Ограничиваем для производительности
            width = Math.Min(width, 3840);
            height = Math.Min(height, 800);

            try
            {
                var (topPoints, bottomPoints) = await Task.Run(() =>
                {
                    int[] tops = new int[width];
                    int[] bottoms = new int[width];
                    int midY = height / 2;

                    // ============================================================
                    // РЕЖИМ 1: ОДНОПОТОЧНАЯ ОБРАБОТКА (по умолчанию)
                    // ============================================================
                    if (!_vm.MainWindow_AppSettings.MultiThreadedRendering)
                    {
                        using (var reader = new AudioFileReader(filePath))
                        {
                            double totalSeconds = reader.TotalTime.TotalSeconds;
                            long totalSamples = (long)(totalSeconds * reader.WaveFormat.SampleRate * reader.WaveFormat.Channels);
                            int samplesPerPixel = (int)(totalSamples / width);

                            if (samplesPerPixel <= 0) samplesPerPixel = 1;

                            int alignment = reader.WaveFormat.BlockAlign / (reader.WaveFormat.BitsPerSample / 8);
                            if (alignment <= 0) alignment = 1;
                            samplesPerPixel = ((samplesPerPixel + alignment - 1) / alignment) * alignment;

                            float[] readBuffer = new float[samplesPerPixel];

                            for (int x = 0; x < width; x++)
                            {
                                if (token.IsCancellationRequested) return (null, null);

                                int samplesRead = reader.Read(readBuffer, 0, samplesPerPixel);
                                if (samplesRead == 0) break;

                                float min = 0;
                                float max = 0;

                                for (int i = 0; i < samplesRead; i++)
                                {
                                    float val = readBuffer[i];
                                    if (val > max) max = val;
                                    if (val < min) min = val;
                                }

                                int topY = midY - (int)(max * midY);
                                int bottomY = midY - (int)(min * midY);

                                tops[x] = Math.Max(0, Math.Min(height - 1, topY));
                                bottoms[x] = Math.Max(0, Math.Min(height - 1, bottomY));
                            }
                        }
                    }
                    // ============================================================
                    // РЕЖИМ 2: МНОГОПОТОЧНАЯ ОБРАБОТКА (для длинных файлов)
                    // ============================================================
                    else
                    {
                        int threadCount = Environment.ProcessorCount;
                        int pixelsPerChunk = width / threadCount;

                        var parallelOptions = new ParallelOptions
                        {
                            CancellationToken = token,
                            MaxDegreeOfParallelism = threadCount
                        };

                        try
                        {
                            Parallel.For(0, threadCount, parallelOptions, i =>
                            {
                                int startX = i * pixelsPerChunk;
                                int endX = (i == threadCount - 1) ? width : startX + pixelsPerChunk;

                                using (var reader = new AudioFileReader(filePath))
                                {
                                    double totalSeconds = reader.TotalTime.TotalSeconds;
                                    long totalSamples = (long)(totalSeconds * reader.WaveFormat.SampleRate * reader.WaveFormat.Channels);
                                    int samplesPerPixel = (int)(totalSamples / width);

                                    if (samplesPerPixel <= 0) samplesPerPixel = 1;

                                    int alignment = reader.WaveFormat.BlockAlign / (reader.WaveFormat.BitsPerSample / 8);
                                    if (alignment <= 0) alignment = 1;
                                    samplesPerPixel = ((samplesPerPixel + alignment - 1) / alignment) * alignment;

                                    long startSample = (long)startX * samplesPerPixel;
                                    long startBytes = startSample * 4;
                                    startBytes = (startBytes / reader.WaveFormat.BlockAlign) * reader.WaveFormat.BlockAlign;

                                    if (startBytes < reader.Length)
                                        reader.Position = startBytes;

                                    float[] readBuffer = new float[samplesPerPixel];

                                    for (int x = startX; x < endX; x++)
                                    {
                                        if (token.IsCancellationRequested) return;

                                        int samplesRead = reader.Read(readBuffer, 0, samplesPerPixel);
                                        if (samplesRead == 0) break;

                                        float min = 0;
                                        float max = 0;

                                        for (int j = 0; j < samplesRead; j++)
                                        {
                                            float val = readBuffer[j];
                                            if (val > max) max = val;
                                            if (val < min) min = val;
                                        }

                                        int topY = midY - (int)(max * midY);
                                        int bottomY = midY - (int)(min * midY);

                                        tops[x] = Math.Max(0, Math.Min(height - 1, topY));
                                        bottoms[x] = Math.Max(0, Math.Min(height - 1, bottomY));
                                    }
                                }
                            });
                        }
                        catch (OperationCanceledException)
                        {
                            return (null, null);
                        }
                    }

                    return (tops, bottoms);
                }, token);

                if (token.IsCancellationRequested || topPoints == null) return;

                // Отрисовка bitmap (без изменений)
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var wbitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                    int stride = wbitmap.BackBufferStride;
                    int bytesPerPixel = 4;
                    uint waveColor = 0xFF3FA9F5;

                    wbitmap.Lock();
                    unsafe
                    {
                        IntPtr pBackBuffer = wbitmap.BackBuffer;
                        for (int x = 0; x < width; x++)
                        {
                            int topY = topPoints[x];
                            int bottomY = bottomPoints[x];

                            for (int y = topY; y <= bottomY; y++)
                            {
                                byte* pPixel = (byte*)pBackBuffer + (y * stride) + (x * bytesPerPixel);
                                *((uint*)pPixel) = waveColor;
                            }
                        }
                    }
                    wbitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                    wbitmap.Unlock();

                    WaveformImage.Source = wbitmap;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка рендеринга waveform: {ex.Message}");
            }
        }


        private async void StartTranscription_Click(object sender, RoutedEventArgs e)
        {
            if (myMediaElement.NaturalDuration.HasTimeSpan)
                _vm.VideoDuration = myMediaElement.NaturalDuration.TimeSpan;

            if (!_vm.ModelType)
            {
                _vm.TranscriptionStartFrom = myMediaElement.Position;
            }
            else
            {
                _vm.TranscriptionStartFrom = TimeSpan.Zero;
            }

            await _vm.StartTranscriptionAsync();
        }

        private async void CancelTranscription_Click(object sender, RoutedEventArgs e)
        {
            await _vm.CancelTranscriptionAsync();
        }


        private void myMediaElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (myMediaElement.NaturalDuration.HasTimeSpan)
            {
                TimeSpan duration = myMediaElement.NaturalDuration.TimeSpan;
                timelineSlider.Maximum = duration.TotalSeconds;
                timeLabel.Text = $"00:00 / {TimeFormatter.FormatTime(duration)}";
            }
          
            myMediaElement.Pause();
            myMediaElement.Position = TimeSpan.Zero;
            timelineSlider.Value = 0;
            myMediaElement.Volume = volumeSlider.Value;

        }


        private void Timer_Tick(object sender, EventArgs e)
        {
            if (myMediaElement.Source != null && myMediaElement.NaturalDuration.HasTimeSpan && !isUserMovingSlider)
            {
                TimeSpan currentPos = myMediaElement.Position;
                TimeSpan totalDur = myMediaElement.NaturalDuration.TimeSpan;

                timelineSlider.Value = currentPos.TotalSeconds;
                timeLabel.Text = $"{TimeFormatter.FormatTime(currentPos)} / {TimeFormatter.FormatTime(totalDur)}";

                if (_vm.IsAudioMode)
                {
                    UpdateWaveformMarker(currentPos.TotalSeconds, totalDur.TotalSeconds);
                }

                if (currentPos > TimeSpan.Zero && currentPos.TotalSeconds < totalDur.TotalSeconds - 1)
                {
                    _savedVideoPosition = currentPos;
                }

                if (currentPos != TimeSpan.Zero) _savedVideoPosition = currentPos;

                _vm.UpdateActiveSubtitleHighlight(currentPos);
            }
        }

        private void timelineSlider_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            isUserMovingSlider = true;
            timer.Stop();
        }


        private void timelineSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (myMediaElement.Source != null && myMediaElement.NaturalDuration.HasTimeSpan)
            {
                TimeSpan newPos = TimeSpan.FromSeconds(timelineSlider.Value);
                myMediaElement.Position = newPos;
                _savedVideoPosition = newPos;

                TimeSpan totalDur = myMediaElement.NaturalDuration.TimeSpan;
                timeLabel.Text = $"{TimeFormatter.FormatTime(newPos)} / {TimeFormatter.FormatTime(totalDur)}";

                if (_vm.IsAudioMode)
                {
                    UpdateWaveformMarker(newPos.TotalSeconds, totalDur.TotalSeconds);
                }


                _vm.UpdateActiveSubtitleHighlight(newPos);
            }
            isUserMovingSlider = false;
            timer.Start();
        }

        private void timelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isUserMovingSlider && myMediaElement.Source != null && myMediaElement.NaturalDuration.HasTimeSpan)
            {
                TimeSpan previewPos = TimeSpan.FromSeconds(timelineSlider.Value);
                TimeSpan totalDur = myMediaElement.NaturalDuration.TimeSpan;
                timeLabel.Text = $"{TimeFormatter.FormatTime(previewPos)} / {TimeFormatter.FormatTime(totalDur)}";

                if (_vm.IsAudioMode)
                {
                    UpdateWaveformMarker(previewPos.TotalSeconds, totalDur.TotalSeconds);
                }
            }
        }

        private void volumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (myMediaElement != null)
            {
                myMediaElement.Volume = volumeSlider.Value;
            }
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (myMediaElement.Source != null)
            {
                myMediaElement.Play();
                timer.Start();
            }
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            myMediaElement.Pause();
            timer.Stop();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            myMediaElement.Stop();
            timer.Stop();
            timelineSlider.Value = 0;
        }

        private void subtitlesGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject dep = (DependencyObject)e.OriginalSource;

            while (dep != null && !(dep is DataGridCell))
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridCell cell)
            {
                if (!cell.IsReadOnly)
                {
                    cell.Focus();

                    DataGridRow row = VisualTreeHelper.GetParent(dep) as DataGridRow;
                    while (row == null && dep != null)
                    {
                        dep = VisualTreeHelper.GetParent(dep);
                        row = dep as DataGridRow;
                    }
                    if (row != null) row.IsSelected = true;

                    subtitlesGrid.BeginEdit();
                }
            }
        }

        private void subtitlesGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            DependencyObject dep = (DependencyObject)e.OriginalSource;

            while (dep != null && !(dep is DataGridRow))
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridRow row && row.DataContext is ViewModel selectedSubtitle)
            {
                // Не трогаем выделение, если кликнули по строке, которая уже выделена —
                // иначе WPF в Extended-режиме сбросит мультивыделение.
                if (!subtitlesGrid.SelectedItems.Contains(selectedSubtitle))
                {
                    subtitlesGrid.SelectedItems.Clear();
                    subtitlesGrid.SelectedItem = selectedSubtitle;
                }

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    if (myMediaElement.Source != null)
                    {
                        timer.Stop();
                        myMediaElement.Position = selectedSubtitle.StartTime;
                        timelineSlider.Value = selectedSubtitle.StartTime.TotalSeconds;

                        if (myMediaElement.NaturalDuration.HasTimeSpan)
                        {
                            TimeSpan totalDur = myMediaElement.NaturalDuration.TimeSpan;
                            timeLabel.Text = $"{TimeFormatter.FormatTime(selectedSubtitle.StartTime)} / {TimeFormatter.FormatTime(totalDur)}";

                            if (_vm.IsAudioMode)
                            {
                                UpdateWaveformMarker(selectedSubtitle.StartTime.TotalSeconds, myMediaElement.NaturalDuration.TimeSpan.TotalSeconds);
                            }
                        }

                        _vm.UpdateActiveSubtitleHighlight(selectedSubtitle.StartTime);

                        timer.Start();
                    }

                    e.Handled = true;
                }
            }
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null && child is not T)
                child = VisualTreeHelper.GetParent(child);
            return child as T;
        }

        private void subtitlesGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pendingMultiSelection = null;

            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row == null || !row.IsSelected) return;

            // Если кликнули ПКМ по строке, которая уже в мультивыделении — сохраняем его.
            if (subtitlesGrid.SelectedItems.Count > 1)
            {
                _pendingMultiSelection = subtitlesGrid.SelectedItems
                    .OfType<ViewModel>()
                    .ToList();
            }
        }

        private void subtitlesGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (_pendingMultiSelection == null) return;

            // Меню открывается после того, как DataGrid уже мог сбросить выделение.
            // Проверяем и при необходимости восстанавливаем.
            if (subtitlesGrid.SelectedItems.Count <= 1)
            {
                var backup = _pendingMultiSelection;
                subtitlesGrid.SelectedItems.Clear();
                foreach (var item in backup)
                    subtitlesGrid.SelectedItems.Add(item);
            }

            _pendingMultiSelection = null;
        }

        private void MergeRows_Click(object sender, RoutedEventArgs e)
        {
            // Аккуратно завершаем возможное редактирование ячейки, чтобы DataGrid
            // не пытался одновременно редактировать и перестраивать строки.
            subtitlesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            subtitlesGrid.CommitEdit(DataGridEditingUnit.Row, true);

            // Берём выделенные строки и сортируем по времени начала,
            // чтобы «верхняя» всегда была первой — независимо от порядка выделения.
            var selected = subtitlesGrid.SelectedItems
                .OfType<ViewModel>()
                .OrderBy(s => s.StartTime)
                .ToList();

            if (selected.Count < 2)
            {
                MessageBox.Show("Select at least two rows to merge.",
                                "Merge rows", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var first = selected.First();
            var last = selected.Last();

            // Текст: склеиваем через пробел, пустые куски отбрасываем
            string mergedText = string.Join(" ",
                selected.Select(s => (s.Text ?? string.Empty).Trim())
                        .Where(t => !string.IsNullOrEmpty(t)));

            // Спикер: берём первый непустой из выделенных (обычно это спикер верхней строки)
            string mergedSpeaker = selected
                .Select(s => s.Speaker)
                .FirstOrDefault(sp => !string.IsNullOrWhiteSpace(sp)) ?? first.Speaker ?? string.Empty;

            // Обновляем «верхнюю» строку — INotifyPropertyChanged сам обновит UI
            first.Text = mergedText;
            first.Speaker = mergedSpeaker;
            first.EndTime = last.EndTime;
            first.TimeRange = $"[{first.StartTime:mm':'ss} -> {last.EndTime:mm':'ss}]";

            // Удаляем все остальные выделенные строки.
            // Итерируемся по копии — оригинал меняется прямо в процессе.
            foreach (var item in selected.Skip(1).ToList())
            {
                _vm.Subtitles.Remove(item);
            }

            subtitlesGrid.SelectedItem = first;
            subtitlesGrid.ScrollIntoView(first);
        }

        private void InsertEmptyRow_Click(object sender, RoutedEventArgs e)
        {
            TimeSpan start;
            int insertIndex;

            if (subtitlesGrid.SelectedItem is ViewModel selectedItem)
            {
                // Вставляем сразу после выделенной строки, начиная с её EndTime
                start = selectedItem.EndTime;
                insertIndex = _vm.Subtitles.IndexOf(selectedItem) + 1;
            }
            else if (_vm.Subtitles.Count > 0)
            {
                // Ничего не выбрано — добавляем в самый конец
                start = _vm.Subtitles[^1].EndTime;
                insertIndex = _vm.Subtitles.Count;
            }
            else
            {
                // Список пуст — начинаем с нуля
                start = TimeSpan.Zero;
                insertIndex = 0;
            }

            // Длительность по умолчанию — 2 секунды, но не выходим за пределы видео
            TimeSpan end = start + TimeSpan.FromSeconds(2);
            if (_vm.VideoDuration > TimeSpan.Zero && end > _vm.VideoDuration)
                end = _vm.VideoDuration;

            // Спикер: если активна диаризация (Python + DiarizationMode) — "unknown"
            bool diarizationOn = _vm.ModelType && _vm.DiarizationMode;

            var newSubtitle = new ViewModel
            {
                StartTime = start,
                EndTime = end,
                TimeRange = $"[{start:mm':'ss} -> {end:mm':'ss}]",
                Speaker = diarizationOn ? "unknown" : string.Empty,
                Text = string.Empty
            };

            _vm.Subtitles.Insert(insertIndex, newSubtitle);
            subtitlesGrid.SelectedItem = newSubtitle;
            subtitlesGrid.ScrollIntoView(newSubtitle);
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            var selected = subtitlesGrid.SelectedItems
                .OfType<ViewModel>()
                .ToList();

            if (selected.Count == 0) return;

            string message = selected.Count == 1
                ? "Are you sure you want to delete this subtitle?"
                : $"Are you sure you want to delete {selected.Count} selected subtitles?";

            var result = MessageBox.Show(message, "Deleting",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            foreach (var item in selected)
            {
                _vm.Subtitles.Remove(item);
            }

            subtitlesGrid.SelectedItems.Clear();
        }

        private void SplitRow_Click(object sender, RoutedEventArgs e)
        {
            if (subtitlesGrid.SelectedItem is not ViewModel selectedItem) return;

            // Аккуратно завершаем возможное редактирование ячейки, чтобы DataGrid
            // не пытался одновременно редактировать и перестраивать строки.
            subtitlesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            subtitlesGrid.CommitEdit(DataGridEditingUnit.Row, true);

            TimeSpan originalEndTime = selectedItem.EndTime;
            TimeSpan duration = originalEndTime - selectedItem.StartTime;
            TimeSpan middleTime = selectedItem.StartTime + TimeSpan.FromTicks(duration.Ticks / 2);

            string fullText = selectedItem.Text ?? "";
            string[] words = fullText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length <= 1)
            {
                MessageBox.Show("The text is too short (1 word) and cannot be split automatically.",
                                "String splitting", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int midIndex = words.Length / 2;
            string firstHalf = string.Join(" ", words, 0, midIndex);
            string secondHalf = string.Join(" ", words, midIndex, words.Length - midIndex);

            int currentIndex = _vm.Subtitles.IndexOf(selectedItem);

            // Изменяем верхнюю половину. INotifyPropertyChanged сам обновит UI.
            selectedItem.Text = firstHalf;
            selectedItem.EndTime = middleTime;
            selectedItem.TimeRange = $"[{selectedItem.StartTime:mm':'ss} -> {middleTime:mm':'ss}]";

            var newSubtitle = new ViewModel
            {
                StartTime = middleTime,
                EndTime = originalEndTime,
                TimeRange = $"[{middleTime:mm':'ss} -> {originalEndTime:mm':'ss}]",
                Speaker = selectedItem.Speaker,
                Text = secondHalf
            };

            _vm.Subtitles.Insert(currentIndex + 1, newSubtitle);

        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _vm.CancelCurrentOperation();
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _vm.DownloadModelAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TogglePlayPause()
        {
            if (myMediaElement.Source == null) return;

            if (timer.IsEnabled)
            {
                myMediaElement.Pause();
                timer.Stop();

                playPauseButton.Content = "Play";
            }
            else
            {
                myMediaElement.Play();
                timer.Start();

                playPauseButton.Content = "Pause";
            }
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            TogglePlayPause();
        }


        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Open_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.J && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ImportJson_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Export_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Space)
            {
                if (e.OriginalSource is TextBox textBox && subtitlesGrid.IsKeyboardFocusWithin)
                {
                    return;
                }

                TogglePlayPause();
                e.Handled = true;
            }
        }

        private void ImportJson_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Select the JSON subtitle file."
            };

            if (openFileDialog.ShowDialog() != true) return;

            try
            {
                int count = _vm.ImportJson(openFileDialog.FileName);
                MessageBox.Show($"Subtitles have been successfully imported: {count}",
                                "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (JsonException)
            {
                MessageBox.Show("The file could not be read. Make sure the selected JSON file has the correct subtitle format.",
                                "Format error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred while reading the file: {ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SubtitlesForExport.Count == 0)
            {
                MessageBox.Show("No data for export. First, complete the video transcription.",
                                "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ExportWindow exportWindow = new ExportWindow(_vm.SubtitlesForExport, _vm.VideoPath);
            exportWindow.Owner = Window.GetWindow(this);
            exportWindow.ShowDialog();
        }



        private void ToggleOrientation_Click(object sender, RoutedEventArgs e)
        {
            _vm.ToggleOrientation();   // ApplyOrientation вызовется через событие OrientationChanged
        }

        private void ApplyOrientation()
        {
            if (_vm.IsVerticalOrientation)
            {
                ColVideo.Width = new GridLength(1.2, GridUnitType.Star);
                ColSplitter.Width = GridLength.Auto;
                ColSubs.Width = new GridLength(0.8, GridUnitType.Star);

                RowVideo.Height = new GridLength(1, GridUnitType.Star);
                RowSplitter.Height = new GridLength(0);
                RowSubs.Height = new GridLength(0);

                Grid.SetRow(PlayerGrid, 0);
                Grid.SetColumn(PlayerGrid, 0);
                Grid.SetColumnSpan(PlayerGrid, 1);

                Grid.SetRow(MainSplitter, 0);
                Grid.SetColumn(MainSplitter, 1);
                Grid.SetColumnSpan(MainSplitter, 1);

                MainSplitter.Width = 5;
                MainSplitter.Height = double.NaN;
                MainSplitter.ResizeDirection = GridResizeDirection.Columns;

                Grid.SetRow(hiddenTabControl, 0);
                Grid.SetColumn(hiddenTabControl, 2);
                Grid.SetColumnSpan(hiddenTabControl, 1);
            }
            else
            {
                ColVideo.Width = new GridLength(1, GridUnitType.Star);
                ColSplitter.Width = new GridLength(0);
                ColSubs.Width = new GridLength(1, GridUnitType.Star);

                RowVideo.Height = new GridLength(1.2, GridUnitType.Star);
                RowSplitter.Height = GridLength.Auto;
                RowSubs.Height = new GridLength(0.8, GridUnitType.Star);

                Grid.SetRow(PlayerGrid, 0);
                Grid.SetColumn(PlayerGrid, 0);
                Grid.SetColumnSpan(PlayerGrid, 3);

                Grid.SetRow(MainSplitter, 1);
                Grid.SetColumn(MainSplitter, 0);
                Grid.SetColumnSpan(MainSplitter, 3);

                MainSplitter.Height = 5;
                MainSplitter.Width = double.NaN;
                MainSplitter.ResizeDirection = GridResizeDirection.Rows;

                Grid.SetRow(hiddenTabControl, 2);
                Grid.SetColumn(hiddenTabControl, 0);
                Grid.SetColumnSpan(hiddenTabControl, 3);
            }
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow windowSettins = new SettingsWindow(_vm.MainWindow_AppSettings);
            windowSettins.Owner = this;

            if (windowSettins.ShowDialog() == true)
            {
                _vm.MainWindow_AppSettings = windowSettins.ThisSettings;
                SettingsService.Save(_vm.MainWindow_AppSettings);

                ModelScanner.ScanGgmlModels(_vm.ModelDirectory, _vm.GGML_Models);
                ModelScanner.ScanFasterWhisperModels(_vm.MainWindow_AppSettings.huggingface_hub_path, _vm.FasterWhisper_Models);
            }
        }

        private void AppendLogLine(string? line)
        {
            if (line == null) return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                ServerLogsTextBox.AppendText($"[{timestamp}] {line}\n");

                ServerLogsTextBox.ScrollToEnd();
            }));
        }

        private void UpdateSpeakerColumnVisibility()
        {
            if (_vm.ModelType == true && _vm.DiarizationMode == true)
            {
                SpeakerColumn.Visibility = Visibility.Visible;
            }
            else
            {
                SpeakerColumn.Visibility = Visibility.Collapsed;
            }
        }

        private void ResetToDefault_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to reset all advanced AI parameters to their default values?",
                "Reset Settings", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _vm.ResetToDefault();
            }
        }

        private void UpdateWaveformMarker(double currentSeconds, double totalSeconds)
        {
            if (_playbackMarker == null || totalSeconds <= 0 || currentSeconds < 0)
                return;

            // Получаем ширину контейнера
            double containerWidth = WaveformImage.ActualWidth;
            if (containerWidth <= 0) return;

            // Вычисляем позицию
            double progress = Math.Min(1.0, currentSeconds / totalSeconds);
            double leftPosition = progress * containerWidth;

            // Обновляем маркер
            _playbackMarker.Margin = new Thickness(leftPosition - 1, 0, 0, 0);
            _playbackMarker.Visibility = Visibility.Visible;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SettingsService.Save(_vm.MainWindow_AppSettings); // Сохраняем настройки в JSON перед выходом
            _pythonServerLauncher.Dispose();              // Глушим фоновые процессы Python
            base.OnClosing(e);
        }

    }
}