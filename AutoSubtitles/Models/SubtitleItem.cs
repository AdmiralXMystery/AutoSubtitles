//using System;
//using System.ComponentModel;
//using System.Runtime.CompilerServices;

//namespace AutoSubtitles
//{
//    public class ViewModel : INotifyPropertyChanged
//    {
//        private bool _isCurrent;
//        public string TimeRange
//        {
//            get => $"{StartTime:hh\\:mm\\:ss} - {EndTime:hh\\:mm\\:ss}";
//            set { /* пустой сеттер, чтобы сериализатор не ругался при импорте старых файлов */ }
//        }
//        public string Text { get; set; } = string.Empty;

//        public string Speaker { get; set; } = string.Empty;

//        public bool IsCurrent
//        {
//            get => _isCurrent;
//            set
//            {
//                if (_isCurrent != value)
//                {
//                    _isCurrent = value;
//                    OnPropertyChanged();
//                }
//            }
//        }

//        public TimeSpan StartTime { get; set; }
//        public TimeSpan EndTime { get; set; }

//        public event PropertyChangedEventHandler? PropertyChanged;
//        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
//        {
//            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
//        }
//    }
//}


using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoSubtitles
{
    public class ViewModel : INotifyPropertyChanged
    {
        private bool _isCurrent;
        private string _text = string.Empty;
        private string _speaker = string.Empty;
        private TimeSpan _startTime;
        private TimeSpan _endTime;

        public bool IsFromOverlap { get; set; }

        public string TimeRange
        {
            get => $"{StartTime:hh\\:mm\\:ss} - {EndTime:hh\\:mm\\:ss}";
            set { /* заглушка для сериализатора */ }
        }

        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(); }
        }

        public string Speaker
        {
            get => _speaker;
            set { _speaker = value; OnPropertyChanged(); }
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent != value)
                {
                    _isCurrent = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan StartTime
        {
            get => _startTime;
            set
            {
                _startTime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimeRange));
            }
        }

        public TimeSpan EndTime
        {
            get => _endTime;
            set
            {
                _endTime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimeRange));
            }
        }

        /// <summary>
        /// Принудительно уведомить UI об изменении текста.
        /// Используется при правке сегментов «на месте» из пост-обработки.
        /// </summary>
        public void RaiseTextChanged() => OnPropertyChanged(nameof(Text));

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}