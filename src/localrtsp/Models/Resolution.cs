namespace localrtsp.Models
{
    public sealed record Resolution(int Width, int Height)
    {
        /// <summary>
        /// Returns the label displayed in the source selector.
        /// </summary>
        public override string ToString()
        {
            return $"{Width} x {Height}";
        }
    }
}