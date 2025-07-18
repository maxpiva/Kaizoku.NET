using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KaizokuTray.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace KaizokuTray.Views
{
    public partial class StorageFolderDialog : Window
    {
        public StorageFolderDialogViewModel ViewModel { get; }
        
        public string? SelectedFolderPath { get; private set; }
        public bool DialogResult { get; private set; }

        public StorageFolderDialog()
        {
            InitializeComponent();
            ViewModel = new StorageFolderDialogViewModel();
            DataContext = ViewModel;
            
            // Subscribe to property changes
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            
            // Subscribe to window closed event to dispose the view model
            Closed += OnWindowClosed;
        }

        public StorageFolderDialog(string? initialPath) : this()
        {
            if (!string.IsNullOrEmpty(initialPath))
            {
                ViewModel.FolderPath = initialPath;
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            // Dispose the view model when the window is closed
            ViewModel?.Dispose();
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StorageFolderDialogViewModel.FolderPath))
            {
                // Validate folder when path changes
                _ = ValidateFolderAsync();
            }
        }

        private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var topLevel = GetTopLevel(this);
                if (topLevel == null) return;

                var storageProvider = topLevel.StorageProvider;
                if (storageProvider == null) return;

                var options = new FolderPickerOpenOptions
                {
                    Title = "Select Storage Folder",
                    AllowMultiple = false
                };

                // Set initial directory if we have a valid path
                if (!string.IsNullOrEmpty(ViewModel.FolderPath) && 
                    System.IO.Directory.Exists(ViewModel.FolderPath))
                {
                    options.SuggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(ViewModel.FolderPath);
                }

                var result = await storageProvider.OpenFolderPickerAsync(options);
                
                if (result != null && result.Count > 0)
                {
                    var selectedFolder = result[0];
                    var path = selectedFolder.TryGetLocalPath();
                    
                    if (!string.IsNullOrEmpty(path))
                    {
                        ViewModel.FolderPath = path;
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle any errors during folder selection
                ViewModel.SetValidationError($"Error opening folder browser: {ex.Message}");
            }
        }

        private async Task ValidateFolderAsync()
        {
            if (string.IsNullOrWhiteSpace(ViewModel.FolderPath))
            {
                ViewModel.ClearValidation();
                return;
            }

            try
            {
                await ViewModel.ValidateFolderAsync(ViewModel.FolderPath);
            }
            catch (Exception ex)
            {
                ViewModel.SetValidationError($"Validation error: {ex.Message}");
            }
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.IsValidFolder && !string.IsNullOrEmpty(ViewModel.FolderPath))
            {
                SelectedFolderPath = ViewModel.FolderPath;
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Shows the dialog and returns the selected folder path if OK was clicked
        /// </summary>
        /// <param name="parent">Parent window</param>
        /// <param name="initialPath">Initial folder path to display</param>
        /// <returns>Selected folder path or null if cancelled</returns>
        public static async Task<string?> ShowDialogAsync(Window? parent = null, string? initialPath = null)
        {
            var dialog = new StorageFolderDialog(initialPath);
            
            try
            {
                if (parent != null)
                {
                    // Use modal dialog with parent
                    await dialog.ShowDialog(parent);
                }
                else
                {
                    // No parent - use a different approach to avoid blocking the UI thread
                    var tcs = new TaskCompletionSource<string?>();
                    
                    // Subscribe to dialog close event to get the result
                    dialog.Closed += (sender, e) =>
                    {
                        var result = dialog.DialogResult ? dialog.SelectedFolderPath : null;
                        tcs.SetResult(result);
                    };
                    
                    // Show the dialog non-modally
                    dialog.Show();
                    
                    // Wait for the result without blocking the UI thread
                    return await tcs.Task;
                }
            }
            catch (PlatformNotSupportedException)
            {
                // Fallback for platforms that don't support modal dialogs
                var tcs = new TaskCompletionSource<string?>();
                
                dialog.Closed += (sender, e) =>
                {
                    var result = dialog.DialogResult ? dialog.SelectedFolderPath : null;
                    tcs.SetResult(result);
                };
                
                dialog.Show();
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                // If dialog fails completely, return null
                try
                {
                    dialog.Close();
                }
                catch { }
                
                // Log the error if possible
                System.Diagnostics.Debug.WriteLine($"StorageFolderDialog failed: {ex.Message}");
                return null;
            }
            
            return dialog.DialogResult ? dialog.SelectedFolderPath : null;
        }
    }
}