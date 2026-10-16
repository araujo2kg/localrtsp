namespace localrtsp.Models
{
    public enum SessionState
    {
        Stopped,
        Starting,
        Streaming,
        SimulatingFault,
        Stopping,
        Error
    }
}