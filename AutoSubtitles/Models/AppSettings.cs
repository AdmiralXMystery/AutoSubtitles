using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoSubtitles
{
    public class AppSettings
    {
        public bool MultiThreadedRendering { get; set; } = true;
        public string GGMLmodelDirectory { get; set; } = string.Empty;
        public string huggingface_hub_path { get; set; } = string.Empty;
        
        private int _beam_size = 3;
        public int beam_size
        {
            get => _beam_size;
            set { _beam_size = value; OnPropertyChanged(); }
        }
        public bool OfflineMode { get; set; }
        public string HF_TOKEN { get; set; } = string.Empty;

        private double _max_pause = 1.0;
        public double max_pause { get => _max_pause; set { _max_pause = value; OnPropertyChanged(); } }

        private bool _vad_filter = false;
        public bool vad_filter { get => _vad_filter; set { _vad_filter = value; OnPropertyChanged(); } }

        private double _vad_threshold = 0.5;
        public double vad_threshold { get => _vad_threshold; set { _vad_threshold = value; OnPropertyChanged(); } }

        private int _min_silence_duration_ms = 2000;
        public int min_silence_duration_ms { get => _min_silence_duration_ms; set { _min_silence_duration_ms = value; OnPropertyChanged(); } }

        private int _min_speech_duration_ms = 250;
        public int min_speech_duration_ms { get => _min_speech_duration_ms; set { _min_speech_duration_ms = value; OnPropertyChanged(); } }

        private int _speech_pad_ms = 400;
        public int speech_pad_ms { get => _speech_pad_ms; set { _speech_pad_ms = value; OnPropertyChanged(); } }

        private double _max_speech_duration_s = double.PositiveInfinity;
        public double max_speech_duration_s { get => _max_speech_duration_s; set { _max_speech_duration_s = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null!)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}