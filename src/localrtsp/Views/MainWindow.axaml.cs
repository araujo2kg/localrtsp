using Avalonia.Controls;

using localrtsp.ViewModels;

namespace localrtsp.Views
{
    public sealed partial class MainWindow : Window
    {
        #region Fields

        private bool _canClose;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates the window and registers its lifecycle handlers.
        /// </summary>
        public MainWindow() : this(true)
        {
        }

        /// <summary>
        /// Creates the window and registers its lifecycle handlers.
        /// </summary>
        public MainWindow(bool initializeSession)
        {
            InitializeComponent();

            if (initializeSession)
            {
                Opened += OnOpened;
            }

            Closing += OnClosing;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Initializes the stream after the window opens.
        /// </summary>
        private async void OnOpened(object? sender, EventArgs args)
        {
            if (DataContext is MainViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        }

        /// <summary>
        /// Delays window closure until the stream is released.
        /// </summary>
        private async void OnClosing(object? sender, WindowClosingEventArgs args)
        {
            if (_canClose)
            {
                return;
            }

            args.Cancel = true;
            IsEnabled = false;

            try
            {
                if (DataContext is MainViewModel viewModel)
                {
                    await viewModel.CloseAsync();
                }
            }
            finally
            {
                _canClose = true;
                Close();
            }
        }

        #endregion
    }
}