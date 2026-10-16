namespace localrtsp.Services.Desktop
{
    public interface IDesktopInteractions
    {
        /// <summary>
        /// Opens a picker for a local image or video file.
        /// </summary>
        Task<string?> PickFileAsync(bool image);

        /// <summary>
        /// Copies the displayed RTSP URL to the clipboard.
        /// </summary>
        Task CopyAsync(string text);
    }
}