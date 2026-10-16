namespace localrtsp.Infrastructure.Processes
{
    public interface IProcessRunner
    {
        /// <summary>
        /// Starts a child process owned by this application.
        /// </summary>
        IOwnedProcess Start(string executable, IReadOnlyList<string> arguments, string component);

        /// <summary>
        /// Runs an operation with cancellation and bounded diagnostics.
        /// </summary>
        Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token);
    }
}