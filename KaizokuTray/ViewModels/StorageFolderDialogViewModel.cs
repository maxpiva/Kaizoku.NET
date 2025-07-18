using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace KaizokuTray.ViewModels
{
    public class StorageFolderDialogViewModel : INotifyPropertyChanged, IDisposable
    {
        private string _folderPath = string.Empty;
        private bool _isValidFolder = false;
        private bool _showValidationStatus = false;
        private string _validationMessage = string.Empty;
        private string _validationIcon = string.Empty;
        private IBrush _validationStatusBackground = Brushes.Transparent;
        private IBrush _validationStatusBorder = Brushes.Transparent;
        private ValidationState _currentValidationState = ValidationState.None;
        private bool _disposed = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        public StorageFolderDialogViewModel()
        {
            // Subscribe to theme changes to update colors automatically
            if (Application.Current != null)
            {
                Application.Current.ActualThemeVariantChanged += OnThemeChanged;
            }
        }

        public string FolderPath
        {
            get => _folderPath;
            set
            {
                if (SetProperty(ref _folderPath, value))
                {
                    // Trigger validation when path changes
                    _ = ValidateFolderAsync(value);
                }
            }
        }

        public bool IsValidFolder
        {
            get => _isValidFolder;
            private set => SetProperty(ref _isValidFolder, value);
        }

        public bool ShowValidationStatus
        {
            get => _showValidationStatus;
            private set => SetProperty(ref _showValidationStatus, value);
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            private set => SetProperty(ref _validationMessage, value);
        }

        public string ValidationIcon
        {
            get => _validationIcon;
            private set => SetProperty(ref _validationIcon, value);
        }

        public IBrush ValidationStatusBackground
        {
            get => _validationStatusBackground;
            private set => SetProperty(ref _validationStatusBackground, value);
        }

        public IBrush ValidationStatusBorder
        {
            get => _validationStatusBorder;
            private set => SetProperty(ref _validationStatusBorder, value);
        }

        /// <summary>
        /// Handles theme changes and updates validation colors accordingly
        /// </summary>
        private void OnThemeChanged(object? sender, EventArgs e)
        {
            if (_currentValidationState != ValidationState.None)
            {
                // Refresh colors when theme changes
                UpdateValidationColors(_currentValidationState);
            }
        }

        /// <summary>
        /// Gets theme-aware colors based on the current theme variant
        /// </summary>
        private (Color background, Color border) GetThemeAwareColors(ValidationState state)
        {
            // Get the current theme variant
            var app = Application.Current;
            var actualTheme = app?.ActualThemeVariant ?? ThemeVariant.Default;
            bool isDarkTheme = actualTheme == ThemeVariant.Dark;

            return state switch
            {
                ValidationState.Success => isDarkTheme 
                    ? (Color.FromRgb(20, 83, 45), Color.FromRgb(34, 197, 94))    // Dark theme: darker green background, bright green border
                    : (Color.FromRgb(220, 252, 220), Color.FromRgb(34, 197, 94)), // Light theme: light green background, green border
                
                ValidationState.Warning => isDarkTheme 
                    ? (Color.FromRgb(92, 77, 19), Color.FromRgb(245, 158, 11))    // Dark theme: darker yellow background, bright orange border
                    : (Color.FromRgb(255, 247, 205), Color.FromRgb(245, 158, 11)), // Light theme: light yellow background, orange border
                
                ValidationState.Error => isDarkTheme 
                    ? (Color.FromRgb(87, 29, 29), Color.FromRgb(239, 68, 68))     // Dark theme: darker red background, bright red border
                    : (Color.FromRgb(254, 226, 226), Color.FromRgb(239, 68, 68)), // Light theme: light red background, red border
                
                _ => (Colors.Transparent, Colors.Transparent)
            };
        }

        /// <summary>
        /// Updates validation colors based on current theme
        /// </summary>
        private void UpdateValidationColors(ValidationState state)
        {
            var (background, border) = GetThemeAwareColors(state);
            ValidationStatusBackground = new SolidColorBrush(background);
            ValidationStatusBorder = new SolidColorBrush(border);
        }

        public async Task ValidateFolderAsync(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                ClearValidation();
                return;
            }

            try
            {
                // Check if directory exists
                if (!Directory.Exists(folderPath))
                {
                    SetValidationError("The specified folder does not exist.");
                    return;
                }

                // Check if we have access to the directory
                try
                {
                    Directory.GetDirectories(folderPath);
                }
                catch (UnauthorizedAccessException)
                {
                    SetValidationError("Access denied to the specified folder.");
                    return;
                }
                catch (Exception ex)
                {
                    SetValidationError($"Cannot access folder: {ex.Message}");
                    return;
                }

                // Perform basic archive detection on background thread but update UI on UI thread
                bool containsArchives = await Task.Run(() =>
                {
                    try
                    {
                        return ContainsArchiveFilesBasic(folderPath);
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                });

                // Update UI back on the UI thread
                Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                {
                    try
                    {
                        if (containsArchives)
                        {
                            SetValidationSuccess("The selected folder contains archive files.");
                        }
                        else
                        {
                            SetValidationWarning("The selected folder appears to be empty or contains no archive files.\r\nThis is ok, if you're starting from scratch.");
                        }
                    }
                    catch (Exception ex)
                    {
                        SetValidationError($"Error scanning folder: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                SetValidationError($"Validation error: {ex.Message}");
            }
        }

        /// <summary>
        /// Basic archive detection without external dependencies
        /// </summary>
        private bool ContainsArchiveFilesBasic(string folderPath)
        {
            try
            {
                var archiveExtensions = new[] { ".zip", ".rar", ".7z", ".cbz", ".cbr", ".cb7", ".tar", ".gz" };
                
                // Check for archive files recursively
                var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                
                foreach (var file in files)
                {
                    var extension = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.Exists(archiveExtensions, ext => ext == extension))
                    {
                        return true;
                    }
                }
                
                return false;
            }
            catch
            {
                // If we can't scan, assume it's valid but unknown
                return false;
            }
        }

        public void SetValidationSuccess(string message)
        {
            _currentValidationState = ValidationState.Success;
            ValidationMessage = message;
            ValidationIcon = "✓";
            UpdateValidationColors(_currentValidationState);
            ShowValidationStatus = true;
            IsValidFolder = true;
        }

        public void SetValidationWarning(string message)
        {
            _currentValidationState = ValidationState.Warning;
            ValidationMessage = message;
            ValidationIcon = "⚠";
            UpdateValidationColors(_currentValidationState);
            ShowValidationStatus = true;
            IsValidFolder = true; // Still valid, just a warning
        }

        public void SetValidationError(string message)
        {
            _currentValidationState = ValidationState.Error;
            ValidationMessage = message;
            ValidationIcon = "✗";
            UpdateValidationColors(_currentValidationState);
            ShowValidationStatus = true;
            IsValidFolder = false;
        }

        public void ClearValidation()
        {
            _currentValidationState = ValidationState.None;
            ValidationMessage = string.Empty;
            ValidationIcon = string.Empty;
            ValidationStatusBackground = Brushes.Transparent;
            ValidationStatusBorder = Brushes.Transparent;
            ShowValidationStatus = false;
            IsValidFolder = false;
        }

        protected virtual bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
        {
            if (Equals(backingStore, value))
                return false;

            backingStore = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Enumeration for validation states
        /// </summary>
        private enum ValidationState
        {
            None,
            Success,
            Warning,
            Error
        }

        /// <summary>
        /// Disposes the view model and unsubscribes from theme change events
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                if (Application.Current != null)
                {
                    Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
                }
                _disposed = true;
            }
        }
    }
}