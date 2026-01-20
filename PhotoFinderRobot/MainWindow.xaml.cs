using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PhotoFinderRobot
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private const int MaxLogEntries = 5000;
        
        private FoundPhoto _foundPhoto;
        private int _processedCount;
        private int _errorCount;
        private bool _isRunning;

        public CancellationTokenSource CancellationTokenSource { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            
            // Set window icon from embedded resource
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/app.ico");
                Icon = BitmapFrame.Create(uri);
            }
            catch
            {
                // If icon fails to load, continue without it
            }
        }

        public FoundPhoto FoundPhoto
        {
            get => _foundPhoto;
            set
            {
                _foundPhoto = value;
                OnPropertyChanged();
            }
        }

        private void OnBrowseSourceClicked(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "Select Source Folder"
            };

            if (!string.IsNullOrEmpty(RootFolderTextBox.Text) && Directory.Exists(RootFolderTextBox.Text))
            {
                dialog.InitialDirectory = RootFolderTextBox.Text;
            }

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                RootFolderTextBox.Text = dialog.FileName;
            }
        }

        private void OnBrowseDestinationClicked(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "Select Destination Folder"
            };

            if (!string.IsNullOrEmpty(DestinationFolderTextBox.Text) && Directory.Exists(DestinationFolderTextBox.Text))
            {
                dialog.InitialDirectory = DestinationFolderTextBox.Text;
            }

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                DestinationFolderTextBox.Text = dialog.FileName;
            }
        }

        private void OnStartFindClicked(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(RootFolderTextBox.Text))
            {
                MessageBox.Show("Please select a source folder.", "Missing Source", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(RootFolderTextBox.Text))
            {
                MessageBox.Show("The source folder does not exist.", "Invalid Source", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(DestinationFolderTextBox.Text))
            {
                MessageBox.Show("Please select a destination folder.", "Missing Destination", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Reset counters
            _processedCount = 0;
            _errorCount = 0;
            LogListBox.Items.Clear();
            
            // Update UI state
            SetRunningState(true);
            AddLogEntry("🚀 Starting organization process...", isHighlight: true);

            CancellationTokenSource = new CancellationTokenSource();
            CancellationToken ct = CancellationTokenSource.Token;

            string rootFolder = RootFolderTextBox.Text;
            string destinationFolder = DestinationFolderTextBox.Text;

            Task.Factory.StartNew(() =>
            {
                try
                {
                    ApplyAllFiles(rootFolder, destinationFolder, ct);
                }
                finally
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
                    {
                        SetRunningState(false);
                        if (ct.IsCancellationRequested)
                        {
                            AddLogEntry("⚠️ Operation cancelled by user.", isHighlight: true);
                        }
                        else
                        {
                            AddLogEntry($"✅ Complete! Processed {_processedCount} files with {_errorCount} errors.", isHighlight: true);
                        }
                        UpdateProgress();
                    });
                }
            }, ct);
        }

        private void ApplyAllFiles(string folder, string destinationFolder, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;

            try
            {
                foreach (string file in Directory.GetFiles(folder, "*.*"))
                {
                    if (ct.IsCancellationRequested) return;

                    FoundItem foundItem = FoundItemFactory.GetItem(file);
                    if (foundItem != null)
                    {
                        ProcessFile(foundItem, destinationFolder, ct);
                    }
                }

                foreach (string directory in Directory.GetDirectories(folder))
                {
                    if (ct.IsCancellationRequested) return;

                    try
                    {
                        ApplyAllFiles(directory, destinationFolder, ct);
                    }
                    catch
                    {
                        // Skip directories we can't access
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
                {
                    AddLogEntry($"⚠️ Access denied: {folder}");
                });
            }
        }

        private void ProcessFile(FoundItem foundItem, string destinationFolder, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;

            try
            {
                string destPath = Path.Combine(destinationFolder, foundItem.DestinationSubPath);
                string destFileName = Path.Combine(destPath, Path.GetFileName(foundItem.CurrentFileName));

                // Handle duplicate filenames
                if (File.Exists(destFileName))
                {
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(foundItem.CurrentFileName);
                    string ext = Path.GetExtension(foundItem.CurrentFileName);
                    int counter = 1;
                    
                    while (File.Exists(destFileName))
                    {
                        destFileName = Path.Combine(destPath, $"{nameWithoutExt}_{counter}{ext}");
                        counter++;
                    }
                }

                Directory.CreateDirectory(destPath);
                File.Move(foundItem.CurrentFileName, destFileName);

                _processedCount++;

                Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
                {
                    if (ct.IsCancellationRequested) return;
                    
                    string fileName = Path.GetFileName(foundItem.CurrentFileName);
                    string datePath = foundItem.DestinationSubPath;
                    AddLogEntry($"📄 {fileName} → {datePath}");
                    UpdateProgress();
                });
            }
            catch (Exception ex)
            {
                _errorCount++;
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
                {
                    AddLogEntry($"❌ Error: {Path.GetFileName(foundItem.CurrentFileName)} - {ex.Message}");
                    UpdateProgress();
                });
            }
        }

        private void AddLogEntry(string message, bool isHighlight = false)
        {
            // Remove oldest entries if we've hit the limit
            while (LogListBox.Items.Count >= MaxLogEntries)
            {
                LogListBox.Items.RemoveAt(0);
            }
            
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogListBox.Items.Add($"[{timestamp}] {message}");
            LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
        }

        private void UpdateProgress()
        {
            ProgressText.Text = _isRunning 
                ? $"Processing... {_processedCount} files moved, {_errorCount} errors"
                : $"Done: {_processedCount} files moved, {_errorCount} errors";
            
            StatusText.Text = _isRunning ? "Running..." : "Ready";
        }

        private void SetRunningState(bool isRunning)
        {
            _isRunning = isRunning;
            StartButton.IsEnabled = !isRunning;
            CancelButton.IsEnabled = isRunning;
            RootFolderTextBox.IsEnabled = !isRunning;
            DestinationFolderTextBox.IsEnabled = !isRunning;
            StatusText.Text = isRunning ? "Running..." : "Ready";
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            CancellationTokenSource?.Cancel();
            AddLogEntry("🛑 Cancelling...", isHighlight: true);
        }

        private void OnClosedClicked(object sender, RoutedEventArgs e) => Close();

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
