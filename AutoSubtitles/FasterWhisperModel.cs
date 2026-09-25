using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace AutoSubtitles
{
    public class FasterWhisperModel : INotifyPropertyChanged
    {
        private string _name;
        private bool _isDownloaded;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public bool IsDownloaded
        {
            get => _isDownloaded;
            set { _isDownloaded = value; OnPropertyChanged(); }
        }

        // Это свойство вернет то имя папки, которое мы ищем в кэше HF
        public string HuggingFaceFolderName => $"models--Systran--faster-whisper-{Name.ToLower()}";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
