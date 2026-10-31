using Microsoft.Extensions.Logging;

namespace NyssaAI.Logging.UseCaseTests.domain;

/// <summary>A consumer that logs. It names the framework's logger, never this package.</summary>
public sealed class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger) => _logger = logger;

    public void Place(int orderId) => _logger.LogInformation("Order {OrderId} placed", orderId);
}
