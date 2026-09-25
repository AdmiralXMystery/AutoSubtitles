using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace AutoSubtitles
{
    using System.ComponentModel;
    using System.Runtime.CompilerServices;

    public class WhisperNetModel : INotifyPropertyChanged
    {
        private string _name;
        private bool _isDownloaded;
        private bool _isDownloading;
        private double _downloadProgress;
        private string _downloadStatusText = "Waiting...";

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string FileName => $"ggml-{Name}.bin";

        public bool IsDownloaded
        {
            get => _isDownloaded;
            set { _isDownloaded = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanBeDownloaded)); }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set { _isDownloading = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanBeDownloaded)); }
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; OnPropertyChanged(); }
        }

        public string DownloadStatusText
        {
            get => _downloadStatusText;
            set { _downloadStatusText = value; OnPropertyChanged(); }
        }

        public bool CanBeDownloaded => !IsDownloaded && !IsDownloading;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
