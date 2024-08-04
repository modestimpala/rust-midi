using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using Microsoft.WindowsAPICodePack.Dialogs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace rust_midi
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private static Playback _playback;
        private const string DeviceName = "rust";
        private OutputDevice _device;
        private string _currentlyPlaying;

        private const string GlobalHotkey1 = "F6";
        private const string GlobalHotkey2 = "F3";
        
        private bool _isPlaying;
        private string _midiDir;

        private bool _getRandomFile;
        private bool _includeSubfolders;
        private static SearchOption _searchOption = SearchOption.TopDirectoryOnly;
            
        private List<FileInfo> _currentFiles;

        public MainWindow()
        {
            InitializeComponent();

            _currentFiles = new List<FileInfo>();
            //set up context menus
            var favoritesContextMenu = new ContextMenu();
            var item = new MenuItem();
            item.Click += DeleteFavoriteClick;
            item.Header = "Delete";
            favoritesContextMenu.Items.Add(item);
            favoritesList.ContextMenu = favoritesContextMenu;

            var listContextMenu = new ContextMenu();
            var item2 = new MenuItem();
            item2.Click += AddFavoriteClick;
            item2.Header = "Favorite";
            listContextMenu.Items.Add(item2);
            listBox.ContextMenu = listContextMenu;
            
            listBox.MouseDoubleClick += ListBox_MouseDoubleClick;
            favoritesList.MouseDoubleClick += FavoritesList_MouseDoubleClick;

            //todo: better error handling
            try
            {
                //register global hotkeys
                GlobalHotKey.RegisterHotKey(GlobalHotkey1, () => PlayRandomMidi());
                GlobalHotKey.RegisterHotKey(GlobalHotkey2, () => StopPlayback());

                //settings time!
                var dirSetting = ReadSetting("dir");
                if (dirSetting != "Not found") //init dir
                {
                    _midiDir = dirSetting;
                    directory.Text = _midiDir;
                }
                else
                {
                    _midiDir = @"C:\";
                    directory.Text = _midiDir;
                }

                if (IsValidPath(_midiDir)) PopulateFiles(_midiDir);
                PopulateFavorites();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                throw;
            }
        }

        private void DeleteFavoriteClick(object sender, RoutedEventArgs e)
        {
            if (favoritesList.SelectedItem != null) DeleteFavorite(favoritesList.SelectedItem.ToString());
        }

        private void AddFavoriteClick(object sender, RoutedEventArgs e)
        {
            if (listBox.SelectedItem != null)
            {
                WriteFavorites(listBox.SelectedItem.ToString());
                MessageBox.Show("added " + listBox.SelectedItem);
            }
        }

        public void PopulateFavorites()
        {
            favoritesList.Items.Clear();
            var favoriteString = ReadFavorites();
            if (string.IsNullOrEmpty(favoriteString)) return;
            if (favoriteString.Contains(@"|"))
            {
                var favorites = favoriteString.Split(@"|");
                foreach (var file in favorites) favoritesList.Items.Add(GetCleanFilename(file));
            }
            else
            {
                favoritesList.Items.Add(GetCleanFilename(favoriteString));
            }
        }

        public void WriteFavorites(string path)
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;
            if (settings["favorites"] == null)
                settings.Add("favorites", path);
            else
                settings["favorites"].Value += @"|" + path;
            configFile.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            PopulateFavorites();
        }

        public void DeleteFavorite(string fav)
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;
            if (settings["favorites"] == null)
                return;
            if (settings["favorites"].Value.Contains(@"|"))
                settings["favorites"].Value = settings["favorites"].Value.Replace(@"|" + fav, "");
            else
                settings["favorites"].Value = "";
            configFile.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            PopulateFavorites();
        }

        public void ClearFavorites()
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;
            if (settings["favorites"] == null)
                return;
            settings["favorites"].Value = "";
            configFile.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            PopulateFavorites();
        }

        private static string ReadFavorites()
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;
            if (settings["favorites"] == null)
                return "";
            return settings["favorites"].Value;
        }
        
        private static string GetFullPathFromFavorites(string cleanFileName)
        {
            var favoriteString = ReadFavorites();
            if (favoriteString.Contains(@"|"))
            {
                var favorites = favoriteString.Split(@"|");
                var result = Array.Find(favorites, element => element.Contains(cleanFileName));
                if (result != null)
                    return result;
                return null;
            }

            if (favoriteString.Contains(cleanFileName))
                return favoriteString;
            return null;
        }

        private static void WriteSettings(string key, string value)
        {
            try
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.AppSettings.Settings;
                if (settings[key] == null)
                    settings.Add(key, value);
                else
                    settings[key].Value = value;
                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            }
            catch (ConfigurationErrorsException)
            {
                MessageBox.Show("Error writting app settings");
            }
        }
        
        private static string ReadSetting(string key)
        {
            try
            {
                var appSettings = ConfigurationManager.AppSettings;
                var result = appSettings[key] ?? "Not found";
                return result;
            }
            catch (ConfigurationErrorsException)
            {
                MessageBox.Show("Error reading settings");
                return null;
            }
        }

        private void PopulateFiles(string dir)
        {
            try
            {
                listBox.Items.Clear();
                _searchOption = _includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(dir, "*.mid", _searchOption);
                _currentFiles = files.Select(f => new FileInfo(f)).ToList();
                SortFiles();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }

        private void SortFiles()
        {
            switch (sortComboBox.SelectedIndex)
            {
                case 0: // Name
                    _currentFiles = _currentFiles.OrderBy(f => f.Name).ToList();
                    break;
                case 1: // Date Modified
                    _currentFiles = _currentFiles.OrderByDescending(f => f.LastWriteTime).ToList();
                    break;
                case 2: // Date Created
                    _currentFiles = _currentFiles.OrderByDescending(f => f.CreationTime).ToList();
                    break;
                case 3: // File Size
                    _currentFiles = _currentFiles.OrderByDescending(f => f.Length).ToList();
                    break;
            }

            listBox.Items.Clear();
            foreach (var file in _currentFiles)
            {
                listBox.Items.Add(GetCleanFilename(file.FullName));
            }
        }

        private void sortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SortFiles();
        }

        private void randomCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            if (randomCheckbox.IsChecked != null)
            {
                _getRandomFile = (bool)randomCheckbox.IsChecked;
            }
        }

        private void PlayRandomMidi()
        {
            if (playButton.IsEnabled)
            {
                var file1 = GetRandomMidi(_midiDir);
                PlayMidi(file1);
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
                    if (_playback != null)
                    {
                        if (_playback.IsRunning) _playback.Stop();

                        //_playback.Dispose();

                        if (_device != null) _device.Dispose();
                    }
                }
                catch (AccessViolationException e)
                {
                    MessageBox.Show(e.Message);
                    throw;
                }
            }

            stopButton.IsEnabled = false;
        }
        
        private void ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (listBox.SelectedItem != null)
            {
                var selectedFileName = listBox.SelectedItem.ToString();
                var selectedFile = _currentFiles.FirstOrDefault(f => f.Name == selectedFileName || f.FullName.EndsWith(selectedFileName));
        
                if (selectedFile != null)
                {
                    StopPlayback();
                    PlayMidi(selectedFile.FullName);
                }
                else
                {
                    MessageBox.Show("Selected file not found.");
                }
            }
        }

        private void FavoritesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (favoritesList.SelectedItem != null)
            {
                var file = favoritesList.SelectedItem.ToString();
                var fullPath = GetFullPathFromFavorites(file);
                if (fullPath != null)
                {
                    StopPlayback();
                    PlayMidi(fullPath);
                }
            }
        }

        private void PlayMidi(string file)
        {
            try
            {
                _device = OutputDevice.GetByName(DeviceName);
                if (_device != null)
                {
                    stopButton.IsEnabled = true;
                    playButton.IsEnabled = false;

                    //var midi = MidiFile.Read(file);
                    var midi = MidiFile.Read(file, new ReadingSettings
                    {
                        NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore
                    });
                    _playback = midi.GetPlayback(_device);

                    _playback.Stopped += FinishedHandler;
                    _playback.Finished += FinishedHandler;

                    currentlyPlayingTextBlock.Text = GetCleanFilename(file);
                    _currentlyPlaying = file;
                    //var tempoMap = midi.GetTempoMap();
                    var tempoMap = midi.GetTempoMap();
                    //TODO make timer more acurate (i think the time being sent is wrong)
                    TimeSpan midiFileDuration = midi.GetTimedEvents().LastOrDefault(e => e.Event is NoteOffEvent)?.TimeAs<MetricTimeSpan>(tempoMap) ?? new MetricTimeSpan();
                    //var midiFileDuration = midi.GetDuration<MetricTimeSpan>();
                    ProgressBarRun(midiFileDuration);
                    //
                    //MessageBox.Show(midiFileDuration.ToString());


                    _playback.Start();
                    _isPlaying = true;
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }

        private void FinishedHandler(object sender, EventArgs e)
        {
            // Use the Dispatcher to update UI elements
            Dispatcher.Invoke(() =>
            {
                playButton.IsEnabled = true;
                stopButton.IsEnabled = false;
                _isPlaying = false;
                
                // If you have any other UI updates, include them here
                songProgressBar.Value = 0;
                songProgressBar.IsEnabled = false;
            });
        }

        private void ProgressBarRun(TimeSpan time)
        {
            var t = new DispatcherTimer();
            var tempTime = new TimeSpan(0);
            songProgressBar.Maximum = time.TotalSeconds;
            songProgressBar.Value = 0;
            songProgressBar.IsEnabled = true;
            t.Interval = new TimeSpan(0, 0, 1);
            t.Tick += delegate(object snd, EventArgs ea)
            {
                if (tempTime.TotalSeconds >= time.TotalSeconds || !_isPlaying)
                {
                    // Get rid of the timer.
                    songProgressBar.Value = 0;
                    songProgressBar.IsEnabled = false;
                    ((DispatcherTimer)snd).Stop();
                }

                songProgressBar.Value++;
            };
            t.Start();
        }
        
        private void browseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.InitialDirectory = "C:\\Users";
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                directory.Text = dialog.FileName;
                _midiDir = dialog.FileName;
                WriteSettings("dir", dialog.FileName);
                PopulateFiles(dialog.FileName);
            }
        }

        private void playButton_Click(object sender, RoutedEventArgs e)
        {
            if (_getRandomFile)
            {
                // Get random file
                var file1 = GetRandomMidi(_midiDir);
                // Play 
                PlayMidi(file1);
            }
            else if (fileTabs.SelectedIndex == 0 && listBox.SelectedItem != null)
            {
                // Play list file
                var selectedFileName = listBox.SelectedItem.ToString();
                var selectedFile = _currentFiles.FirstOrDefault(f => f.Name == selectedFileName || f.FullName.EndsWith(selectedFileName));
        
                if (selectedFile != null)
                {
                    PlayMidi(selectedFile.FullName);
                }
                else
                {
                    MessageBox.Show("Selected file not found.");
                }
            }
            else if (fileTabs.SelectedIndex == 1 && favoritesList.SelectedItem != null)
            {
                // Play favorites file
                var file2 = favoritesList.SelectedItem.ToString();
                var fullPath = GetFullPathFromFavorites(file2);
                PlayMidi(fullPath);
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
                    if (_playback.IsRunning) _playback.Stop();
                    //_playback.Dispose();

                    if (_device != null) _device.Dispose();
                }
                catch (AccessViolationException exception)
                {
                    MessageBox.Show(exception.Message);
                    throw;
                }
            }

            //var _device = OutputDevice.GetByName(deviceName);
            //_device.Dispose();
            songProgressBar.Value = 0;
            stopButton.IsEnabled = false;
        }

        private void saveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentlyPlaying != null)
            {
                WriteFavorites(_currentlyPlaying);
                PopulateFavorites();
                addedLabel.Visibility = Visibility.Visible;
                var t = new DispatcherTimer();
                t.Interval = new TimeSpan(0, 0, 2);
                t.Tick += delegate(object snd, EventArgs ea)
                {
                    addedLabel.Visibility = Visibility.Hidden;
                    // Get rid of the timer.
                    ((DispatcherTimer)snd).Stop();
                };
                t.Start();
                //MessageBox.Show("Added " + currentlyPlaying);
            }
        }

        private void deleteFavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = (DialogResult)MessageBox.Show("Are you sure?", 
                "Confirm", MessageBoxButton.YesNo);
            if (dialog == System.Windows.Forms.DialogResult.Yes)
            {
                ClearFavorites();
                MessageBox.Show("Cleared");
            }
        }

        private static string GetRandomMidi(string dir)
        {
            var rand = new Random();
            var files = Directory.GetFiles(dir, "*.mid", _searchOption);
            return files[rand.Next(files.Length)];
        }

        private bool IsValidPath(string path, bool allowRelativePaths = false)
        {
            bool isValid;
            try
            {
                if (allowRelativePaths)
                {
                    isValid = Path.IsPathRooted(path);
                }
                else
                {
                    var root = Path.GetPathRoot(path);
                    isValid = string.IsNullOrEmpty(root?.Trim('\\', '/')) == false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                isValid = false;
            }

            return isValid;
        }

        private string GetCleanFilename(string path)
        {
            if (!string.IsNullOrEmpty(path))
                try
                {
                    var justPath = Directory.GetParent(path).FullName;
                    return path.Replace(justPath + "\\", "");
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message);
                    throw;
                }

            return "";
        }

        private void directory_TextChanged(object sender, TextChangedEventArgs e)
        {
        }

        private void directory_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (IsValidPath(directory.Text))
                {
                    _midiDir = directory.Text;
                    WriteSettings("dir", directory.Text);
                }
                else
                {
                    MessageBox.Show("invalid  path");
                }
            }
        }

        private void subfolderCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (subfolderCheckbox.IsChecked != null)
            {
                _includeSubfolders = (bool)subfolderCheckbox.IsChecked;

                _searchOption = _includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                WriteSettings("subfolders", _includeSubfolders.ToString());
                PopulateFiles(_midiDir);
            }
        }

        private void filterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(filterTextBox.Text))
            {
                PopulateFiles(_midiDir);
            }
            else
            {
                _currentFiles = Directory.GetFiles(_midiDir, filterTextBox.Text + "*.mid", _searchOption)
                    .Select(f => new FileInfo(f))
                    .ToList();
                SortFiles();
            }
        }
    }
}