using Change_Shortcuts_paths;
using System.Configuration;
using System.Data;
using System.Windows;

namespace ChangeShortcutsPaths
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		private void Application_Start(object sender, StartupEventArgs e)
		{
			var mainWindow = new MainWindow(e.Args);
			mainWindow.Show();
		}
	}

}
