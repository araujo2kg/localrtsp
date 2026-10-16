using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace localrtsp.Services.Desktop
{
    public sealed class DesktopInteractions : IDesktopInteractions
    {
        #region Fields

        private readonly Window _window;

        #endregion

        #region Constructors

        /// <summary>
        /// Uses the main window for file picking and clipboard access.
        /// </summary>
        public DesktopInteractions(Window window)
        {
            _window = window;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Opens a picker for a local image or video file.
        /// </summary>
        public async Task<string?> PickFileAsync(bool image)
        {
            IReadOnlyList<IStorageFile> files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = image ? "Select image" : "Select video", AllowMultiple = false, FileTypeFilter = image ? new[] { FilePickerFileTypes.ImageAll } : new[] { FilePickerFileTypes.All } });

            return files.FirstOrDefault()?.TryGetLocalPath();
        }

        /// <summary>
        /// Copies the displayed RTSP URL to the clipboard.
        /// </summary>
        public async Task CopyAsync(string text)
        {
            if (_window.Clipboard == null)
            {
                throw new InvalidOperationException("Clipboard is unavailable.");
            }

            await _window.Clipboard.SetTextAsync(text);
        }

        #endregion
    }
}