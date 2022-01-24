using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Text.RegularExpressions;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using System.Threading;
using System.Configuration;
using System.ComponentModel;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace rust_midi
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window 
    {
        bool getRandomFile = false;
        bool isPlaying = false;
        string midiDir = @"";
        private static Playback _playback;
        private static string deviceName = "rust";
        string globalHotkey1 = "F6";
        string globalHotkey2 = "F3";
        public string currentlyPlaying;

        private ContextMenu favoritesContextMenu;
        private ContextMenu listContextMenu;
        public MainWindow()
        {
            InitializeComponent();

            //set up context menus
            favoritesContextMenu = new ContextMenu();
            var item = new System.Windows.Controls.MenuItem();
            item.Click += deleteFavoriteClick;
            item.Header = "Delete";
            favoritesContextMenu.Items.Add(item);
            favoritesList.ContextMenu = favoritesContextMenu;

            listContextMenu = new ContextMenu();
            var item2 = new System.Windows.Controls.MenuItem();
            item2.Click += addFavoriteClick;
            item2.Header = "Add";
            listContextMenu.Items.Add(item2);
            listBox.ContextMenu = listContextMenu;

            //todo: better error handling
            try
            {
                //register global hotkeys
                GlobalHotKey.RegisterHotKey(globalHotkey1, () => playRandomMidi());
                GlobalHotKey.RegisterHotKey(globalHotkey2, () => StopPlayback());

                //settings time!
                var dirSetting = ReadSetting("dir");
                if (dirSetting != "Not found") //init dir
                {
                    midiDir = dirSetting;
                    directory.Text = midiDir;
                } else
                {
                    midiDir = @"C:\";
                    directory.Text = midiDir;
                }
                if (IsValidPath(midiDir))
                {
                    populateFiles(midiDir);
                }
                PopulateFavorites();
            } catch (Exception e)
            {
                MessageBox.Show(e.Message);
                throw e;
            }
        }

        private void deleteFavoriteClick(object sender, RoutedEventArgs e)
        {
            if(favoritesList.SelectedItem != null)
            {
                DeleteFavorite(favoritesList.SelectedItem.ToString());
            }
        }

        private void addFavoriteClick(object sender, RoutedEventArgs e)
        {
            if (favoritesList.SelectedItem != null)
            {
                WriteFavorites(favoritesList.SelectedItem.ToString());
            }
        }
        static string GetFullPathFromFavorites(string cleanFileName)
        {
            var favoriteString = ReadFavorites();
            if (favoriteString.Contains(@"|"))
            {
                string[] favorites = favoriteString.Split(@"|");
                var result = Array.Find(favorites, element => element.Contains(cleanFileName));
                if (result != null)
                {
                    return result;
                } else
                {
                    return null;
                }
                
            }
            else
            {
                if(favoriteString.Contains(cleanFileName))
                {
                    return favoriteString;
                } else
                {
                    return null;
                }
            }
        }
        public void PopulateFavorites()
        {
            favoritesList.Items.Clear();
            var favoriteString = ReadFavorites();
            if (favoriteString.Contains(@"|"))
            {
                string[] favorites = favoriteString.Split(@"|");
                foreach (string file in favorites)
                {
                    favoritesList.Items.Add(GetCleanFilename(file));
                }
            } else
            {
                favoritesList.Items.Add(GetCleanFilename(favoriteString));
            }
        }

        static string ReadSetting(string key)
        {
            try
            {
                var appSettings = ConfigurationManager.AppSettings;
                string result = appSettings[key] ?? "Not found";
                return result;
            } catch (ConfigurationErrorsException)
            {
                MessageBox.Show("Erorr reading settings");
                return null;
            }
        }
        public void WriteFavorites(string path)
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;
            if (settings["favorites"] == null)
            {
                settings.Add("favorites", path);
            }
            else
            {
                settings["favorites"].Value += @"|" + path; 
            }
            configFile.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            PopulateFavorites();
        }

        public void DeleteFavorite(string fav)
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;
            if (settings["favorites"] == null)
            {
                return;
            }
            else if(settings["favorites"].Value.Contains(@"|"))
            {
                settings["favorites"].Value = settings["favorites"].Value.Replace(@"|" + fav, "");
            } else
            {
                settings["favorites"].Value = "";
            }
            configFile.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            PopulateFavorites();
        }

        public void ClearFavorites()
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;
            if (settings["favorites"] == null)
            {
                return;
            } else
            {
                settings["favorites"].Value = "";
            }
            configFile.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            PopulateFavorites();
        }

        static string ReadFavorites()
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;
            if (settings["favorites"] == null)
            {
                return "";
            } else
            {
                return settings["favorites"].Value;
            }
        }

        static void WriteSettings(string key, string value)
        {
            try
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.AppSettings.Settings;
                if(settings[key] == null)
                {
                    settings.Add(key, value);
                } else
                {
                    settings[key].Value = value;
                }
                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            } catch (ConfigurationErrorsException)
            {
                MessageBox.Show("Error writting app settings");
            }
        }

        private void browseButton_Click(object sender, RoutedEventArgs e)
        {
            var fileContent = string.Empty;
            var filePath = string.Empty;
            CommonOpenFileDialog dialog = new CommonOpenFileDialog();
            dialog.InitialDirectory = "C:\\Users";
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                directory.Text = dialog.FileName;
                midiDir = dialog.FileName;
                WriteSettings("dir", dialog.FileName);
                populateFiles(dialog.FileName);
            }

        }
        string pathEdit;
        string cleanFile;
        private void populateFiles(string dir)
        {
            try
            {
                listBox.Items.Clear();
                string[] files = Directory.GetFiles(dir);
                foreach (string fileName in files)
                {
                    //pathEdit = Directory.GetParent(fileName).FullName;
                    //cleanFile = fileName.Replace(pathEdit, " ");
                    listBox.Items.Add(GetCleanFilename(fileName));
                    
                }
            } catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }


        private void randomCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            if((bool)randomCheckbox.IsChecked)
            {
                getRandomFile = true;
            } else
            {
                getRandomFile = false;
            }
        }

        private void playRandomMidi()
        {
            if (playButton.IsEnabled)
            {
                string file1 = getRandomMidi(midiDir);
                playMidi(file1);
            } 

        }

        private void StopPlayback()
        {
            if (_playback != null)
            {
                _playback.InterruptNotesOnStop = true;

                //_playback.Stop();

                try
                {
                    _playback.Dispose();
                }
                catch (AccessViolationException _e)
                {
                    throw;
                }

            }
            //var _device = OutputDevice.GetByName(deviceName);
            //_device.Dispose();
            stopButton.IsEnabled = false;
        }

        private async void playMidi(string file)
        {
            var _device = OutputDevice.GetByName(deviceName);
            stopButton.IsEnabled = true;
            //var midi = MidiFile.Read(file);
            var midi = MidiFile.Read(file, new ReadingSettings
            {
                NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
            });
            _playback = midi.GetPlayback(_device);
            playButton.IsEnabled = false;
            currentlyPlayingTextBlock.Text = GetCleanFilename(file);
            currentlyPlaying = file;
            //var tempoMap = midi.GetTempoMap();
            TempoMap tempoMap = midi.GetTempoMap();

            TimeSpan midiFileDuration = midi.GetTimedEvents().LastOrDefault(e => e.Event is NoteOffEvent)?.TimeAs<MetricTimeSpan>(tempoMap) ?? new MetricTimeSpan();
            ProgressBarRun(midiFileDuration);
            //
            //MessageBox.Show(midiFileDuration.ToString());

            await Task.Run(() =>
            {
                _playback.Start();
                SpinWait.SpinUntil(() => !_playback.IsRunning);
                _device.Dispose();
                _playback.Dispose();
            });
            playButton.IsEnabled = true;
        }

        System.Windows.Forms.Timer myTimer = new System.Windows.Forms.Timer();
        private void ProgressBarRun(TimeSpan time)
        {
            
        }

        public void IncreaseBar()
        {
            songProgressBar.Value++;
        }
        private static void TimerEventProcessor(Object myObject, EventArgs myEventArgs)
        {
            
        }

        private void playButton_Click(object sender, RoutedEventArgs e)
        {

            var _device = OutputDevice.GetByName(deviceName);
            if ((bool)randomCheckbox.IsChecked)
            {
                //get random file
                string file1 = getRandomMidi(midiDir);
                // play 
                playMidi(file1);

            }
            else if (fileTabs.SelectedIndex == 0 && listBox.SelectedItem != null)
            {
                //play list file

                string file2 = listBox.SelectedItem.ToString();
                file2 = midiDir + "\\" + file2; //add our path to the cleanfilename from list
                playMidi(file2);


            }
            else if (fileTabs.SelectedIndex == 1 && favoritesList.SelectedItem != null)
            {
                //play favorites file
                var file2 = favoritesList.SelectedItem.ToString();
                var fullPath = GetFullPathFromFavorites(file2);
                playMidi(fullPath);
            }
            else
            {
                MessageBox.Show("Please select a file or check Play Random");

            }
        }
        private void stopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_playback != null)
            {
                _playback.InterruptNotesOnStop = true;

                //_playback.Stop();
                try
                {
                    _playback.Dispose();
                }
                catch (AccessViolationException _e)
                {
                    throw;
                }

            }
            var _device = OutputDevice.GetByName(deviceName);
            _device.Dispose();
            stopButton.IsEnabled = false;
        }
        private void pauseButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void saveButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentlyPlaying != null)
            {
                WriteFavorites(currentlyPlaying);
                PopulateFavorites();
                MessageBox.Show("Added " + currentlyPlaying);
            }
        }

        private void deleteFavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.DialogResult dialog = (System.Windows.Forms.DialogResult)MessageBox.Show("Are you sure?", "Confirm", MessageBoxButton.YesNo);
            if (dialog == System.Windows.Forms.DialogResult.Yes)
            {
                ClearFavorites();
                MessageBox.Show("Cleared");
            }
        }

        static string getRandomMidi(string dir)
        {
            var rand = new Random();
            var files = Directory.GetFiles(dir, "*.mid");
            return files[rand.Next(files.Length)];
        }

       

        private bool IsValidPath(string path, bool allowRelativePaths = false)
        {
            bool isValid = true;

            try
            {
                string fullPath = System.IO.Path.GetFullPath(path);

                if (allowRelativePaths)
                {
                    isValid = System.IO.Path.IsPathRooted(path);
                }
                else
                {
                    string root = System.IO.Path.GetPathRoot(path);
                    isValid = string.IsNullOrEmpty(root.Trim(new char[] { '\\', '/' })) == false;
                }
            }
            catch (Exception ex)
            {
                isValid = false;
            }

            return isValid;
        }

        private string GetCleanFilename(string path)
        {
            if (path != " " || path != "" || path != null)
            {
                string justPath = Directory.GetParent(path).FullName;
                return path.Replace(justPath + "\\", "");
            } else
            {
                return "";
            }
        }

        private void directory_TextChanged(object sender, TextChangedEventArgs e)
        {
            
        }

        private void directory_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Enter)
            {
                if (IsValidPath(directory.Text))
                {
                    midiDir = directory.Text;
                    WriteSettings("dir", directory.Text);
                } else
                {
                    MessageBox.Show("invalid  path");
                }
            }
        }

    }
}
