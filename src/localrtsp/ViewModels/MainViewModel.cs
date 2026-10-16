using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace localrtsp.ViewModels
{
    public sealed partial class MainViewModel : ObservableObject
    {
        #region Fields

        private static readonly TimeSpan _clipboardConfirmationDuration = TimeSpan.FromMilliseconds(1200);
        private readonly SessionCoordinator _sessions;
        private readonly IDependencyService _dependencies;
        private readonly SettingsStore _settings;
        private readonly SessionLog _log;
        private readonly IDesktopInteractions _desktop;
        private readonly CancellationTokenSource _lifetime = new();
        private int _logUpdatePending;
        private bool _closed;

        [ObservableProperty]
        private int _sourceIndex;

        [ObservableProperty]
        private Resolution _resolution = Defaults.Resolutions[0];

        [ObservableProperty]
        private int _fps = Defaults.FramesPerSecond;

        [ObservableProperty]
        private string _color = Defaults.FrameColor;

        [ObservableProperty]
        private string _filePath = "";

        [ObservableProperty]
        private CameraDevice? _selectedCamera;

        [ObservableProperty]
        private bool _loop = true;

        [ObservableProperty]
        private decimal? _stallSeconds = Defaults.StallSeconds;

        [ObservableProperty]
        private string _fFmpegPath = "";

        [ObservableProperty]
        private string _mediaMtxPath = "";

        /// <summary>
        /// Indicates that a source or dependency operation is in progress.
        /// </summary>
        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _status = "Stopped";

        [ObservableProperty]
        private string _message = "Checking local dependencies...";

        /// <summary>
        /// The effective URL of the current or most recent session.
        /// </summary>
        [ObservableProperty]
        private string _url = Defaults.Url(Defaults.FirstPort);

        /// <summary>
        /// Describes the applied settings, independently of pending edits.
        /// </summary>
        [ObservableProperty]
        private string _activeDescription = "No active session";

        [ObservableProperty]
        private string _logText = "";

        [ObservableProperty]
        private string _copyLabel = "Copy URL";

        #endregion

        #region Properties and Events

        public string[] Sources
        {
            get;
        } =
        {
            "Solid color",
            "Moving test pattern",
            "Static image",
            "Video file",
            "Local camera",
            "Unresponsive connection"
        };

        public Resolution[] Resolutions => Defaults.Resolutions;

        public int[] FrameRates => Defaults.FrameRates;

        public ObservableCollection<CameraDevice> Cameras { get; } = new();

        public bool IsColor => SourceIndex == (int)SourceKind.SolidColor;

        public bool IsFile => SourceIndex is (int)SourceKind.Image or (int)SourceKind.Video;

        public bool IsVideo => SourceIndex == (int)SourceKind.Video;

        public bool IsCamera => SourceIndex == (int)SourceKind.Camera;

        public bool IsFault => SourceIndex == (int)SourceKind.Unresponsive;

        public bool IsEditable => !IsBusy;

        public bool IsError => Status == "Error";

        public bool IsRunning => Status is "Streaming" or "Simulating Fault";

        public bool IsTransitioning => Status is "Starting" or "Stopping";

        public bool CanStart => !IsBusy && _sessions.Snapshot.ActiveSettings == null;

        public bool CanApply => !IsBusy && _sessions.Snapshot.ActiveSettings != null;

        /// <summary>
        /// Allows stopping an active session or canceling startup.
        /// </summary>
        public bool CanStop => IsBusy || _sessions.Snapshot.ActiveSettings != null;

        #endregion

        #region Constructors

        /// <summary>
        /// Connects session services and subscribes to state updates.
        /// </summary>
        public MainViewModel(
            SessionCoordinator sessions,
            IDependencyService dependencies,
            SettingsStore settings,
            SessionLog log,
            IDesktopInteractions desktop)
        {
            _sessions = sessions;
            _dependencies = dependencies;
            _settings = settings;
            _log = log;
            _desktop = desktop;
            _sessions.Changed += SessionChanged;
            _log.Changed += LogChanged;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Restores pending settings and starts a solid-color stream.
        /// </summary>
        public async Task InitializeAsync()
        {
            await RunAsync(async () =>
            {
                StreamSettings settings = await _settings.LoadAsync(_lifetime.Token);
                SourceIndex = settings.Source == SourceKind.Unresponsive ? 0 : (int)settings.Source;
                Resolution = Resolutions.FirstOrDefault(r => r.Width == settings.Width && r.Height == settings.Height) ?? Resolutions[0];
                Fps = FrameRates.Contains(settings.Fps) ? settings.Fps : Defaults.FramesPerSecond;
                Color = settings.Color;
                FilePath = settings.FilePath;
                Loop = settings.Loop;
                StallSeconds = settings.StallSeconds;
                FFmpegPath = settings.FFmpegPath;
                MediaMtxPath = settings.MediaMtxPath;

                if (!string.IsNullOrEmpty(settings.CameraId))
                {
                    SelectedCamera = new CameraDevice("Saved camera - refresh to confirm availability", settings.CameraId);
                    Cameras.Add(SelectedCamera);
                }

                // A startup never revives a camera or a fault listener without an explicit source selection.
                await _sessions.StartAsync(CreateSettings() with
                {
                    Source = SourceKind.SolidColor
                }, _lifetime.Token);
            });
        }

        #endregion

        #region Commands

        /// <summary>
        /// Starts a session using the selected settings.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStart))]
        private Task StartAsync()
        {
            return StartSelectedAsync();
        }

        /// <summary>
        /// Restarts the stream with the pending settings.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanApply))]
        private Task ApplyAsync()
        {
            return StartSelectedAsync();
        }

        /// <summary>
        /// Stops the active operation and releases its owned resources.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStop))]
        private Task StopAsync()
        {
            return RunAsync(() => _sessions.StopAsync(), busy: false);
        }

        /// <summary>
        /// Selects a local source file.
        /// </summary>
        [RelayCommand]
        private Task BrowseAsync()
        {
            return RunAsync(async () =>
            {
                string? file = await _desktop.PickFileAsync(SourceIndex == (int)SourceKind.Image);

                if (file != null)
                {
                    FilePath = file;
                }
            });
        }

        /// <summary>
        /// Reloads available cameras and preserves the selected identifier.
        /// </summary>
        [RelayCommand]
        private Task RefreshCamerasAsync()
        {
            return RunAsync(async () =>
            {
                string? selected = SelectedCamera?.Id;
                IReadOnlyList<CameraDevice> cameras = await _dependencies.GetCamerasAsync(FFmpegPath, _lifetime.Token);
                Cameras.Clear();

                foreach (CameraDevice camera in cameras)
                {
                    Cameras.Add(camera);
                }

                SelectedCamera = cameras.FirstOrDefault(c => c.Id == selected);

                if (Cameras.Count == 0)
                {
                    Message = "No camera devices were found. Check Windows camera permissions.";
                }
            });
        }

        /// <summary>
        /// Copies the displayed RTSP URL to the clipboard.
        /// </summary>
        [RelayCommand]
        private Task CopyAsync()
        {
            return RunAsync(async () =>
            {
                await _desktop.CopyAsync(Url);
                CopyLabel = "Copied!";
                await Task.Delay(_clipboardConfirmationDuration, _lifetime.Token);
                CopyLabel = "Copy URL";
            }, busy: false);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Refreshes controls for the selected source kind.
        /// </summary>
        partial void OnSourceIndexChanged(int value)
        {
            foreach (string property in new[]
            {
                nameof(IsColor),
                nameof(IsFile),
                nameof(IsVideo),
                nameof(IsCamera),
                nameof(IsFault)
            }

            )
            {
                OnPropertyChanged(property);
            }
        }

        /// <summary>
        /// Refreshes state indicators after a session status change.
        /// </summary>
        partial void OnStatusChanged(string value)
        {
            OnPropertyChanged(nameof(IsError));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsTransitioning));
        }

        /// <summary>
        /// Refreshes commands when an operation starts or finishes.
        /// </summary>
        partial void OnIsBusyChanged(bool value)
        {
            RefreshCommands();
        }

        /// <summary>
        /// Updates editing availability and session commands.
        /// </summary>
        private void RefreshCommands()
        {
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(CanStop));
            StartCommand.NotifyCanExecuteChanged();
            ApplyCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Starts the selected source and saves its settings.
        /// </summary>
        private Task StartSelectedAsync()
        {
            return RunAsync(async () =>
            {
                StreamSettings settings = CreateSettings();
                await _sessions.StartAsync(settings, _lifetime.Token);
                await _settings.SaveAsync(settings, _lifetime.Token);
            });
        }

        /// <summary>
        /// Collects the pending source and output settings.
        /// </summary>
        private StreamSettings CreateSettings()
        {
            return new()
            {
                Source = (SourceKind)SourceIndex,
                Width = Resolution.Width,
                Height = Resolution.Height,
                Fps = Fps,
                Color = Color.Trim(),
                FilePath = FilePath,
                CameraId = SelectedCamera?.Id ?? "",
                Loop = Loop,
                StallSeconds = (int)(StallSeconds ?? Defaults.StallSeconds),
                FFmpegPath = FFmpegPath.Trim(),
                MediaMtxPath = MediaMtxPath.Trim()
            };
        }

        /// <summary>
        /// Runs an operation with cancellation and bounded diagnostics.
        /// </summary>
        private async Task RunAsync(Func<Task> operation, bool busy = true)
        {
            if (_closed || busy && IsBusy)
            {
                return;
            }

            if (busy)
            {
                IsBusy = true;
            }

            try
            {
                await operation();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Message = ex.Message;
                _log.Write("UI", ex.Message);
            }
            finally
            {
                if (busy)
                {
                    IsBusy = false;
                }
            }
        }

        /// <summary>
        /// Posts session state changes to the UI thread.
        /// </summary>
        private void SessionChanged(SessionSnapshot snapshot)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_closed)
                {
                    return;
                }

                Status = snapshot.State switch
                {
                    SessionState.SimulatingFault => "Simulating Fault",
                    _ => snapshot.State.ToString()
                };
                Url = snapshot.Url;
                Message = snapshot.Message;
                ActiveDescription = snapshot.ActiveSettings is StreamSettings active ? $"Active: {Sources[(int)active.Source]} | {active.Width} x {active.Height} | {active.Fps} FPS" : "No active session - URL is inactive";
                RefreshCommands();
            });
        }

        /// <summary>
        /// Coalesces log updates on the UI thread.
        /// </summary>
        private void LogChanged()
        {
            if (Interlocked.Exchange(ref _logUpdatePending, 1) != 0)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _logUpdatePending, 0);

                if (!_closed)
                {
                    LogText = _log.Text;
                }
            });
        }

        #endregion

        #region Disposal

        /// <summary>
        /// Cancels pending work and releases the session before closing.
        /// </summary>
        public async Task CloseAsync()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            await _lifetime.CancelAsync();
            _sessions.Changed -= SessionChanged;
            _log.Changed -= LogChanged;

            try
            {
                await _sessions.DisposeAsync();
            }
            catch (Exception ex)
            {
                _log.Write("Shutdown", ex.Message);
            }
            finally
            {
                _lifetime.Dispose();
            }
        }

        #endregion
    }
}