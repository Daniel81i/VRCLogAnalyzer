using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace VRCLogAnalyzer
{
    public partial class CreditWindow : Window
    {
        public CreditWindow()
        {
            InitializeComponent();
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            AppTitle.Text = version == null ? "VRCLogAnalyzer" : $"VRCLogAnalyzer v{version.Major}.{version.Minor}.{version.Build}";
        }

        private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            MainWindow.OpenBrowser(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
    }
}
