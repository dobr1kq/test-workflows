using ECommerce.Domain;

namespace ECommerce.Application.Abstractions;

public interface ICartService
{
    Task<Cart> GetCartByUserIdAsync(string userId);
    // Додати товар: перевіряє StockQuantity в Domain
    Task AddItemAsync(string userId, Guid productId, int quantity);
    
    // Оновити кількість: перевіряє доступність на складі
    Task UpdateQuantityAsync(string userId, Guid productId, int quantity);
    Task RemoveItemAsync(string userId, Guid productId);
    
    // Оформлення замовлення: логіка фіксації ціни та очищення кошика
    Task<Order> CheckoutAsync(string userId);
}