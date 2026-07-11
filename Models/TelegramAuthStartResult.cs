namespace CosmoNet.App.Models;

public sealed class TelegramAuthStartResult
{
    public string LoginCode { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.Now.AddMinutes(10);
    public string Message { get; set; } = "";
}
