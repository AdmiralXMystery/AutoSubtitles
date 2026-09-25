using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


namespace AutoSubtitles
{

    public partial class SettingsWindow : Window
    {

        public AppSettings ThisSettings { get; set; } = new AppSettings();

        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();

            this.DataContext = this;

            ThisSettings = currentSettings;
            
        }

        public void ShowInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton && clickedButton.Tag != null)
            {
                string action = clickedButton.Tag.ToString();

                switch (action)
                {
                    case "GGMLdirectory":
                        if (string.IsNullOrEmpty(ThisSettings.GGMLmodelDirectory) || !Directory.Exists(ThisSettings.GGMLmodelDirectory))
                        {
                            return;
                        }

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{ThisSettings.GGMLmodelDirectory}\"",
                            UseShellExecute = true
                        });
                        break;
                    case "huggingface_hub":
                        if (string.IsNullOrEmpty(ThisSettings.huggingface_hub_path) || !Directory.Exists(ThisSettings.huggingface_hub_path))
                        {
                            return;
                        }

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{ThisSettings.huggingface_hub_path}\"",
                            UseShellExecute = true
                        });
                        break;
                }
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), this);
                e.Handled = true;
            }
        }

        private void SaveAndExit_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

    }
}
