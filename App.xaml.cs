using System.Configuration;
using System.Data;
using System.Windows;
using Paperhome.Data;

namespace Paperhome;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Инициализируем базу данных хранения
        using (var db = new PaperworkDbContext())
        {
            db.Database.EnsureCreated();
        }
    }
}