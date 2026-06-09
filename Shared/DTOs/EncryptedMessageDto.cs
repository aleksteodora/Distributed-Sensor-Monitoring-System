namespace Shared.DTOs;

public class EncryptedMessageDto
{
    public string EncryptedPayload { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public long MessageId { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public string SensorId { get; set; } = string.Empty;
}
