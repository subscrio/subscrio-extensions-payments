namespace Subscrio.Payments.DTOs;

public sealed class PaymentPage
{
    public required IReadOnlyList<PaymentDto> Data { get; init; }
    public required long Total { get; init; }
}
