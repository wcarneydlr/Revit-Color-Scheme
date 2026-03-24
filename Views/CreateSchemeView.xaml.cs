using ColorSchemeAddin.Models;
using System.Windows;
using System.Windows.Input;

namespace ColorSchemeAddin.Views
{
    public partial class CreateSchemeView : System.Windows.Controls.UserControl
    {
        public CreateSchemeView() => InitializeComponent();

        private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is ColorEntryModel entry)
            {
                using var dlg = new System.Windows.Forms.ColorDialog
                {
                    Color    = System.Drawing.Color.FromArgb(entry.R, entry.G, entry.B),
                    FullOpen = true,
                    AnyColor = true
                };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    entry.R = dlg.Color.R;
                    entry.G = dlg.Color.G;
                    entry.B = dlg.Color.B;
                }
            }
        }
    }
}
