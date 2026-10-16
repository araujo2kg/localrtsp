namespace localrtsp.Services.Dependencies
{
    public interface IDependencyService
    {
        /// <summary>
        /// Locates and verifies the required streaming tools.
        /// </summary>
        Task<ToolPaths> ResolveAsync(StreamSettings settings, CancellationToken token);

        /// <summary>
        /// Checks file decoding or availability of the selected camera.
        /// </summary>
        Task ValidateSourceAsync(StreamSettings settings, string ffmpeg, CancellationToken token);

        /// <summary>
        /// Lists local DirectShow cameras without opening a video feed.
        /// </summary>
        Task<IReadOnlyList<CameraDevice>> GetCamerasAsync(string explicitFFmpeg, CancellationToken token);
    }
}