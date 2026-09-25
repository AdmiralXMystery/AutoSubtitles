using System;

namespace AutoSubtitles
{
    public static class TimeFormatter
    {
        public static string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
            {
                return time.ToString(@"hh\:mm\:ss");
            }
            return time.ToString(@"mm\:ss");
        }
    }
}