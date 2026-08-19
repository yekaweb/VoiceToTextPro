using System.Windows;
using System.Windows.Controls;

namespace VoiceToTextPro.Controls
{
    public partial class MacWindowControls : UserControl
    {
        public MacWindowControls()
        {
            InitializeComponent();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = Window.GetWindow(this);
            win?.Close();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = Window.GetWindow(this);
            if (win != null)
            {
                win.WindowState = WindowState.Minimized;
            }
        }

        private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = Window.GetWindow(this);
            if (win != null)
            {
                win.WindowState = (win.WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
            }
        }
    }
}
