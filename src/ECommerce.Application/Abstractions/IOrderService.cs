using ECommerce.Domain;

namespace ECommerce.Application.Abstractions;

public interface IOrderService
{
    Task<IEnumerable<Order>> GetUserOrdersAsync(string userId);
}