using DesktopPet.Application.Contracts;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.Application.Productivity;

public sealed class ProductivityEventHub : IProductivityEventPublisher
{
    public event EventHandler<ProductivityEvent>? Published;
    public void Publish(ProductivityEvent notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        Published?.Invoke(this, notification);
    }
}
