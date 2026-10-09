namespace Renewtron.Data;

/// <summary>
/// The PDF behind an <see cref="AsicKeyNotification"/>, kept because ASIC's download link
/// dies after 30 days and customers want their renewal letters later. Its own table so the
/// bytes are only read when someone downloads it.
/// </summary>
public class AsicKeyNotificationPdf
{
    public Guid NotificationId { get; set; }
    public byte[] Content { get; set; } = [];
    public DateTime SavedAt { get; set; }
}
