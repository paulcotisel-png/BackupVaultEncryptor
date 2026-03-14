using System.Reflection;
using System.Windows;

namespace BackupVaultEncryptor.App.UI
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
            {
                var versionString = version.ToString(3);
                VersionTextBlock.Text = $"Version {versionString}";
            }
            else
            {
                VersionTextBlock.Text = "Version unknown";
            }
        }
    }
}
