namespace localrtsp.Models
{
    public sealed record SessionSnapshot(SessionState State, string Url, string Message, StreamSettings? ActiveSettings = null);
}