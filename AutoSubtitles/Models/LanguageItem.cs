namespace AutoSubtitles
{
    public class LanguageItem
    {
        public string DisplayName { get; set; } = string.Empty; // То, что видит пользователь (например, "Русский")
        public string Code { get; set; } = string.Empty;       // То, что передаем в Whisper (например, "ru" или "auto")
    }
}