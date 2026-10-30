using System.Configuration;
using System.Data;
using System.Windows;
using Pavlo.FMCWRadarHeatmap.VM;

namespace Pavlo.FMCWRadarHeatmap
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            var window = new MainWindow();
            var viewModel = new Viewmodel();

            window.DataContext = viewModel;
            
            window.Show();

            base.OnStartup(e);
        }
    }
}
