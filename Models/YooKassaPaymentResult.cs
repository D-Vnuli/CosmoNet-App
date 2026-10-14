namespace CosmoNet.App.Models;

public sealed class YooKassaPaymentResult
{
    public int OrderId { get; set; }
    public string PaymentUrl { get; set; } = "";
}
