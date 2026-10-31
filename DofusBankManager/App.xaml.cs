using System.Windows;

namespace DofusBankManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.LoadSaved();
    }
}
