using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AutoSubtitles
{
    public partial class ExportWindow : Window
    {
        private readonly List<ViewModel> _subtitles;
        private readonly string _videoPath;
        private bool _isInitialized = false;

        public ExportWindow(List<ViewModel> subtitles, string videoPath)
        {
            InitializeComponent();
            _subtitles = subtitles ?? new List<ViewModel>();
            _videoPath = videoPath;
            _isInitialized = true;

            PopulateSpeakerRenames();   // ← до UpdatePreview

            _isInitialized = true;

            UpdatePreview();
        }

        private void PopulateSpeakerRenames()
        {
            var speakers = _subtitles
                .Select(s => s.Speaker)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (speakers.Count == 0)
            {
                // Диаризации не было — прячем всю секцию
                RenameSpeakersPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var items = speakers.Select(s => new SpeakerRenameItem(s)).ToList();
            SpeakerRenameList.ItemsSource = items;
        }

        private void OnSettingsChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();
        private void OnSettingsChanged(object sender, RoutedEventArgs e) => UpdatePreview();
        private void OnSpeakerRenameChanged(object sender, TextChangedEventArgs e) => UpdatePreview();


        private void UpdatePreview()
        {
            if (!_isInitialized) return;

            var previewItems = _subtitles.Take(7).ToList();
            PreviewTextBox.Text = SubtitleExporter.Generate(previewItems, BuildOptions(), isPreview: true);
        }

        private ExportOptions BuildOptions()
        {
            // Собираем переименования
            var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (SpeakerRenameList.ItemsSource is IEnumerable<SpeakerRenameItem> items)
            {
                foreach (var it in items)
                {
                    var newName = it.NewName?.Trim();
                    if (!string.IsNullOrEmpty(newName) &&
                        !string.Equals(it.Original, newName, StringComparison.Ordinal))
                    {
                        renames[it.Original] = newName;
                    }
                }
            }

            return new ExportOptions
            {
                Format = FormatComboBox.SelectedIndex == 1 ? SubtitleExportFormat.Json : SubtitleExportFormat.Txt,
                IncludeTimestamps = IncludeTimestampsCheckBox.IsChecked == true,
                IncludeSpeaker = IncludeSpeakerCheckBox.IsChecked == true,
                IncludeText = IncludeTextCheckBox.IsChecked == true,
                Separator = SeparatorComboBox.SelectedIndex switch
                {
                    1 => TxtSeparator.Tab,
                    2 => TxtSeparator.Space,
                    _ => TxtSeparator.NewLine
                },
                SpeakerRenames = renames
            };
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var options = BuildOptions();
            bool isJson = options.Format == SubtitleExportFormat.Json;

            SaveFileDialog saveFileDialog = new SaveFileDialog();

            if (isJson)
            {
                saveFileDialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                saveFileDialog.DefaultExt = ".json";
            }
            else
            {
                saveFileDialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                saveFileDialog.DefaultExt = ".txt";
            }

            string baseName = string.IsNullOrEmpty(_videoPath)
                ? "subtitles"
                : Path.GetFileNameWithoutExtension(_videoPath) + "_subtitles";

            saveFileDialog.FileName = baseName + (isJson ? ".json" : ".txt");

            if (saveFileDialog.ShowDialog() != true) return;

            try
            {
                string finalOutput = SubtitleExporter.Generate(_subtitles, options, isPreview: false);
                File.WriteAllText(saveFileDialog.FileName, finalOutput, Encoding.UTF8);

                MessageBox.Show("The data has been successfully exported!", "Success",
                                MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"The file could not be saved: {ex.Message}", "Export Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }

    public class SpeakerRenameItem
    {
        public string Original { get; }
        public string NewName { get; set; } = string.Empty;

        public SpeakerRenameItem(string original) => Original = original;
    }
}
