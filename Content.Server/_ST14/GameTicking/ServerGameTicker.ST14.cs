namespace Content.Server.GameTicking;

public sealed partial class ServerGameTicker
{
    /// <summary>
    /// ST14: the front has no station to announce late joiners to, so the "X has arrived" station announcements are off
    /// </summary>
    public bool ST14ArrivalAnnouncements { get; set; }
}
