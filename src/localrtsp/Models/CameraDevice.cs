namespace localrtsp.Models
{
    public sealed record CameraDevice(string Name, string Id)
    {
        /// <summary>
        /// Returns the label displayed in the source selector.
        /// </summary>
        public override string ToString()
        {
            return Name;
        }
    }
}