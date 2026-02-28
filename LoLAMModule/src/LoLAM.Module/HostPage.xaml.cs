using System.Windows.Controls;

namespace LoLAM.Module;

/// <summary>
/// Legacy host page placeholder (kept to avoid build errors if referenced anywhere).
/// The module now uses LoginPage/MainPage pages directly.
/// </summary>
public partial class HostPage : Page
{
    public HostPage()
    {
        InitializeComponent();
        Content = new TextBlock
        {
            Text = "HostPage is deprecated. Use LoginPage/MainPage.",
            FontSize = 16,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
    }
}
