using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FmlDiff.Services;
using FmlDiff.ViewModels;
using FmlDiff.Views;

namespace FmlDiff;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dataLoader = new FmlDataLoader();
            var diffEngine = new HexDiffEngine();
            var viewModel = new MainWindowViewModel(dataLoader, diffEngine);
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
