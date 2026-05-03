using ECommerce.Application.Abstractions;
using ECommerce.Domain;
using ECommerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Infrastructure.Services;

public class CartService : ICartService
{
    private readonly AppDbContext _context;

    public CartService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Cart> GetCartByUserIdAsync(string userId)
    {
        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart == null)
        {
            cart = new Cart 
            { 
                Id = Guid.NewGuid(), 
                UserId = userId, 
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Carts.Add(cart);
            await _context.SaveChangesAsync();
        }

        return cart;
    }

    public async Task AddItemAsync(string userId, Guid productId, int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Кількість має бути більше нуля", nameof(quantity));
        }

        var product = await _context.Products.FindAsync(productId);
        if (product == null)
        {
            throw new InvalidOperationException("Товар не знайдено");
        }

        if (product.StockQuantity < quantity)
        {
            throw new InvalidOperationException("Недостатньо товару на складі");
        }

        var cart = await GetCartByUserIdAsync(userId);
        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == productId);

        if (existingItem != null)
        {
            var newTotalQuantity = existingItem.Quantity + quantity;
            if (product.StockQuantity < newTotalQuantity)
            {
                throw new InvalidOperationException("Сумарна кількість перевищує залишок на складі");
            }
            existingItem.Quantity = newTotalQuantity;
        }
        else
        {
            var newItem = new CartItem 
            { 
                Id = Guid.NewGuid(),
                CartId = cart.Id,
                ProductId = productId, 
                Quantity = quantity, 
                UnitPrice = product.Price 
            };
            _context.CartItems.Add(newItem);
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task UpdateQuantityAsync(string userId, Guid productId, int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Кількість має бути більше нуля", nameof(quantity));
        }

        var cart = await GetCartByUserIdAsync(userId);
        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);

        if (item == null)
        {
            throw new InvalidOperationException("Товар у кошику не знайдено");
        }

        var product = await _context.Products.FindAsync(productId);
        if (product == null)
        {
            throw new InvalidOperationException("Товар не існує");
        }

        if (product.StockQuantity < quantity)
        {
            throw new InvalidOperationException("Недостатньо товару на складі");
        }

        item.Quantity = quantity;
        cart.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task RemoveItemAsync(string userId, Guid productId)
    {
        var cart = await GetCartByUserIdAsync(userId);
        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);

        if (item != null)
        {
            _context.CartItems.Remove(item);
            cart.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<Order> CheckoutAsync(string userId)
    {
        var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync()
            : null;

        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart == null || !cart.Items.Any())
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync();
            }
            throw new InvalidOperationException("Кошик порожній");
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            Status = OrderStatus.Pending,
            TotalAmount = 0,
            Items = new List<OrderItemSnapshot>()
        };

        foreach (var item in cart.Items)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == item.ProductId);
            
            if (product == null)
            {
                throw new InvalidOperationException($"Товар з ID {item.ProductId} більше не існує");
            }

            if (product.StockQuantity < item.Quantity)
            {
                throw new InvalidOperationException($"Недостатньо товару {product.Name} для оформлення замовлення");
            }

            product.StockQuantity -= item.Quantity;

            var orderItem = new OrderItemSnapshot
            {
                Id = Guid.NewGuid(),
                ProductId = item.ProductId,
                ProductName = product.Name,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            };

            order.Items.Add(orderItem);
            order.TotalAmount += orderItem.UnitPrice * orderItem.Quantity;
        }

        _context.Orders.Add(order);
        _context.CartItems.RemoveRange(cart.Items);
        
        await _context.SaveChangesAsync();

        if (transaction != null)
        {
            await transaction.CommitAsync();
        }

        return order;
    }
}