using System;
using System.Collections.Generic;
using System.Windows;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using System.Windows.Input;
using System.Windows.Media;
using IWshRuntimeLibrary;
using Serilog;
using Serilog.Core;
using File = System.IO.File;
using Titanium;
using static Titanium.TypesFuncs;
using Serilog.Events;
using CheckBox = System.Windows.Controls.CheckBox;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Change_Shortcuts_paths
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{

		public MainWindow()
		{
			InitializeComponent();
		}
		public MainWindow(string[]? Args = null)
		{
			bool? turnOnLogging = null;
			bool isAutomatic = false;
			bool isNoUi = false;

			if (Args != null && Args.Length != 0)
			{
				foreach (var setting in Args)
				{
					var split = setting.Split("=");
					var settingName = split[0];
					var settingValue = split[1];
					switch (settingName) {
						case "source":
							tbSourceFolderPath.Text = settingValue;
							break;

						case "subfolders":
							cbSubfolders.IsChecked = settingValue is "true" or "1";
							break;
						
						case "skipValid":
							cbSkipValid.IsChecked = settingValue is "true" or "1";
							break;

						case "regular":
							cbRegular.IsChecked = settingValue is "true" or "1";
							break;

						case "find":
							tbFind.Text = settingValue;
							break;

						case "replace":
							tbReplace.Text = settingValue;
							break;

						case "log":
							turnOnLogging = settingValue is "true" or "1";
							break;

						case "Auto":
							Visibility = Visibility.Hidden;
							isAutomatic = isNoUi = settingValue is "true" or "1";
							break;

						case "AutoStart":
							isAutomatic = settingValue is "true" or "1";
							break;

						default:
							Log.Warning($"Unknown setting \"{settingName}\"");
							break;
					}
				}
			}

			cbLog.IsChecked ??= Directory.GetCurrentDirectory().Split("\\").Last() == "Debug";

			var ls = new LoggingLevelSwitch();
			//: if current folder is not Debug
			if (cbLog.IsChecked is false)
			{
				//: Turn off debugging
				ls.MinimumLevel = ((LogEventLevel)1 + (int)LogEventLevel.Fatal);
			}
			using var log = new LoggerConfiguration()
				.MinimumLevel.ControlledBy(ls)
				.WriteTo.File("log.txt")
				.CreateLogger();

			Log.Information($"\n\n\n\n\n-------------Logger Started [{DateTime.Now}]--------------");

			InitializeComponent();
			if (isAutomatic)
				btnReplace_Click(null, null);

			if (isNoUi)
				Close();
		}

		private void BtnSourceFolderPath_OnClick(object Sender, RoutedEventArgs E)
		{
			// Open Folder Browser Dialog
			var FolderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
			System.Windows.Forms.DialogResult Result = FolderBrowserDialog.ShowDialog();
			if (Result == System.Windows.Forms.DialogResult.OK)
			{
				// Set the selected folder path to the TextBox
				tbSourceFolderPath.Text = FolderBrowserDialog.SelectedPath;
			}
		}


		//! Find and replace targets in all shortcuts (.lnk) in the source folder and it's subfolders
		private void btnReplace_Click(object Sender, RoutedEventArgs E)
		{
			List<string> EmptyTextboxNames = new();
            //: Checking if all textboxes are filled
			if (string.IsNullOrEmpty(tbSourceFolderPath.Text))
				EmptyTextboxNames.Add("Source Folder Path");
			if (string.IsNullOrEmpty(tbFind.Text))
				EmptyTextboxNames.Add("Find");
			if (string.IsNullOrEmpty(tbReplace.Text))
				EmptyTextboxNames.Add("Replace");

			//: If all textboxes are filled
			if (EmptyTextboxNames.Count == 0)
			{
				if (tbSourceFolderPath.Text[0] is '"')
				{
					//. Make a list from all paths in quotes. Not tested
					var paths = tbSourceFolderPath.Text.Split('"').Where((_, i) => i % 2 == 1);
					Log.Information($"Started in {paths.Enumerate()} with Find({tbFind.Text}) and Replace({tbReplace.Text})");
					foreach (var path in paths)
					{
						ReplaceShortcutTarget(path);
					}
				}
				else
				{
					Log.Information($"Started in {tbSourceFolderPath.Text} with Find({tbFind.Text}) and Replace({tbReplace.Text})");
					ReplaceShortcutTarget(tbSourceFolderPath.Text);
				}

				MessageBox.Show("Done!", "Message", MessageBoxButton.OK, MessageBoxImage.Information);
			}
			else
				MessageBox.Show($"Please input {(EmptyTextboxNames.Count == 1? "a" : "") } {EmptyTextboxNames.Enumerate()}!", "Message", MessageBoxButton.OK, MessageBoxImage.Information);

		}
		private void CbWorkingDirectory_CheckChanged(object Sender, RoutedEventArgs E)
		{
			var cb = (CheckBox)Sender;
			cbWorkingDirectory_OnlyWithShortcut.IsEnabled = cb.IsChecked == true;
		}
		private void CbWorkingDirectory_OnlyWithShortcut_OnIsEnabledChanged(object Sender, DependencyPropertyChangedEventArgs E)
		{
			var cb = (CheckBox)Sender;
			cb.Foreground = cb.IsEnabled ? Brushes.Black : Brushes.Gray;
			if (lbWorkingDirectory_OnlyWithShortcutDescription != null) 
				lbWorkingDirectory_OnlyWithShortcutDescription.Foreground = cb.IsEnabled ? Brushes.Gray : Brushes.Gainsboro; //TODO: По хорошему, нужно все элементы настроек и их описания объединить в свой StackPanel, а Foreground изменять ссылаясь на родителя (StackPanel), но тут всего один элемент, поэтому и так сойдёт.
		}

		// Мои преподаватели говорили мне, что надо отделять логику в отдельный файл, но и так сойдёт, правда же?
		//# Logic
		#region Logic

		//: Get the target path of a shortcut (.lnk) file
		public IWshShortcut GetShortcutInfo(string ShortcutPath)
		{
			if (!File.Exists(ShortcutPath)) return null;

			WshShell shell = new(); //: Create a new WshShell Interface
			IWshShortcut link = (IWshShortcut)shell.CreateShortcut(ShortcutPath); //: Link the interface to our shortcut

			return link;
		}

		//: Set the target path of a shortcut (.lnk) file
		private bool SetShortcutTargetFile(string ShortcutPath, string TargetFilePath, string? WorkingDirectory = null)
		{
			if (!File.Exists(ShortcutPath)) return false;

			WshShell shell = new(); //Create a new WshShell Interface 
			IWshShortcut link = (IWshShortcut)shell.CreateShortcut(ShortcutPath); //Link the interface to our shortcut

			link.TargetPath = TargetFilePath; //Assign the shortcut's target
			if (WorkingDirectory != null)
				link.WorkingDirectory = WorkingDirectory;

			try
			{
				link.Save(); //Save the shortcut
			}
			catch (UnauthorizedAccessException e)
			{
				var result = MessageBox.Show($"Access denied to \"{ShortcutPath}\". Restart program as admin?", "Error", MessageBoxButton.YesNo, MessageBoxImage.Error);
				if (result == MessageBoxResult.Yes)
				{
					ProcessStartInfo startInfo = new()
					{
						UseShellExecute = true,
						WorkingDirectory = Environment.CurrentDirectory,
						//FileName = Process.GetCurrentProcess().MainModule.FileName, //. Get the path of self process
						//Verb = "runas", //. Run as admin
						Arguments = $"runas /user:Administrator \"{Process.GetCurrentProcess().MainModule.FileName} AutoStart=true source=\"{tbSourceFolderPath.Text}\" subfolders={cbSubfolders.IsChecked} skipValid={cbSkipValid.IsChecked} regular={cbRegular.IsChecked} log={cbLog.IsChecked} find=\"{tbFind.Text}\" replace=\"{tbReplace.Text}\"\"" //. Pass arguments; run as admin 
					};
					try
					{
						Process.Start(startInfo);
					}
					catch (Exception exception)
					{
						MessageBox.Show(exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
					}

					Close();
				}

			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
			

			return true;
		}

		private void ReplaceShortcutTarget(string FolderPath)
		{
			Log.Debug($"Digged in \"{FolderPath}\")");
			//: Get all shortcuts in the source folder and it's subfolders
			string[] shortcuts = Directory.GetFiles(FolderPath, "*.lnk", SearchOption.AllDirectories);
			var folders = Directory.GetDirectories(FolderPath);

			//: Loop through all folders recursively
			foreach (var folder in folders)
			{
				ReplaceShortcutTarget(folder);
			}


			//: Loop through all shortcuts
			foreach (string shortcut in shortcuts)
			{
				
				string targetPath = GetShortcutInfo(shortcut).TargetPath; //. Get the target path of the shortcut
				string WorkingDirectory = GetShortcutInfo(shortcut).WorkingDirectory; //. Get the target WorkingDirectory of the shortcut
				if (cbSkipValid.IsChecked == true && File.Exists(targetPath)) continue; //. If the target path is valid - continue [cbSkipValid]

				Log.Debug($"Checking \"{shortcut}\" with target \"{targetPath}\"");  
				if (string.IsNullOrEmpty(targetPath)) continue; //. If the target path is empty - continue

				//: Replace the old path with the new path
				string newTargetPath = cbRegular.IsChecked == true? 
					Regex.Replace(targetPath, tbFind.Text, tbReplace.Text) : //. Regular expressions [cbRegular]
					targetPath.Replace(tbFind.Text, tbReplace.Text); //. Ordinary replace
				if (cbCancelInvalid.IsChecked == true && !File.Exists(newTargetPath)) continue; //. If the new path is invalid - continue [cbCancelInvalid]

				if (newTargetPath == targetPath && cbWorkingDirectory_OnlyWithShortcut.IsChecked == true || cbWorkingDirectory.IsChecked == false) continue; //. If nothing changed - continue [cbWorkingDirectory_OnlyWithShortcut]

				string? newWorkingDirectory = null;
				if(cbWorkingDirectory.IsChecked == true) //: Changing Working Directory [cbWorkingDirectory]
				{
					if (string.IsNullOrEmpty(WorkingDirectory)) continue; //. If the target WorkingDirectory is empty - continue

					newWorkingDirectory = cbRegular.IsChecked == true?
						Regex.Replace(WorkingDirectory, tbFind.Text, tbReplace.Text) : //. Regular expressions [cbRegular]
						WorkingDirectory.Replace(tbFind.Text, tbReplace.Text); //. Ordinary replace

					
					Log.Information($"--Working directory changed to \"{newWorkingDirectory}\"");
				}
				if (newTargetPath == targetPath && newWorkingDirectory is null || WorkingDirectory == newWorkingDirectory) continue; //. If nothing changed - continue

				Log.Information($"--Valid. Replacing to \"{newTargetPath}\"");
				SetShortcutTargetFile(shortcut, newTargetPath, newWorkingDirectory); //. Set the new target path to the shortcut
			}
			
			#endregion
		}

		//# Drag and Drop

		private void SourceFolder_MouseMove(object Sender, MouseEventArgs E)
		{
			Grid SourceFolder = (Grid)Sender;
			if (E.LeftButton == MouseButtonState.Pressed)
			{
				DragDrop.DoDragDrop(SourceFolder, SourceFolder, DragDropEffects.Copy);
			}
		}

		private void SourceFolder_DragEnter(object Sender, DragEventArgs E)
		{
			Grid SourceFolder = (Grid)Sender;
			SourceFolder.Opacity = 0.5;
			}

		private void SourceFolder_DragLeave(object Sender, DragEventArgs E)
		{
			Grid SourceFolder = (Grid)Sender;
			SourceFolder.Opacity = 1;
		}

		private void SourceFolder_DragOver(object Sender, DragEventArgs E)
		{
			E.Effects = E.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.All : DragDropEffects.None;
			E.Handled = false;
		}

		private void SourceFolder_Drop(object Sender, DragEventArgs E)
		{
			string[]? filenames = E.Data.GetData(DataFormats.FileDrop) as string[];
			foreach (var name in filenames)
			{
				tbSourceFolderPath.AppendText(File.ReadAllText(name).Add("\"", false).Add("\", ", true));
			}

			tbSourceFolderPath.Text = tbSourceFolderPath.Text.TrimEnd(',', ' ');
		}
	}
}
