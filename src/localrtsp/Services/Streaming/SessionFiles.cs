namespace localrtsp.Services.Streaming
{
    internal static class SessionFiles
    {
        #region Properties and Events

        internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "localrtsp", "sessions");

        #endregion

        #region Public Methods

        /// <summary>
        /// Removes known session files without deleting unknown contents.
        /// </summary>
        internal static void Delete(string directory)
        {
            try
            {
                File.Delete(Path.Combine(directory, "mediamtx.yml"));

                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory); // Never recurse into unknown contents.
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Removes session files left by processes that have exited.
        /// </summary>
        internal static void CleanStale()
        {
            if (!Directory.Exists(Root))
            {
                return;
            }

            foreach (string directory in Directory.GetDirectories(Root, "session-*"))
            {
                string[] parts = Path.GetFileName(directory).Split('-');

                if (parts.Length != 3 || !int.TryParse(parts[1], out int processId) || !Guid.TryParseExact(parts[2], "N", out _))
                {
                    continue;
                }

                try
                {
                    using Process process = Process.GetProcessById(processId);

                    if (!process.HasExited)
                    {
                        continue;
                    }
                }
                catch (ArgumentException)
                {
                }

                Delete(directory);
            }
        }

        #endregion
    }
}