using Bogus;
using ECommerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        if (await context.Products.AnyAsync()) return;

        var faker = new Faker();

        // ── Products (10 000) ──────────────────────────────────────────────────
        var productFaker = new Faker<Product>()
            .RuleFor(p => p.Id,            f => Guid.NewGuid())
            .RuleFor(p => p.Name,          f => f.Commerce.ProductName())
            .RuleFor(p => p.Description,   f => f.Commerce.ProductDescription())
            .RuleFor(p => p.Price,         f => Math.Round(decimal.Parse(f.Commerce.Price(10, 1000)), 2))
            .RuleFor(p => p.StockQuantity, f => f.Random.Int(10, 200))
            .RuleFor(p => p.Category,      f => f.Commerce.Categories(1)[0])
            .RuleFor(p => p.SKU,           f => Guid.NewGuid().ToString("N"));

        var products = productFaker.Generate(10_000);
        await context.Products.AddRangeAsync(products);
        await context.SaveChangesAsync();

        // ── Carts + CartItems (500 carts × avg 3 items ≈ 1 500 items) ─────────
        var carts = new List<Cart>(500);
        for (int i = 0; i < 500; i++)
        {
            var cart = new Cart
            {
                Id        = Guid.NewGuid(),
                UserId    = $"seed-user-{i + 1}",
                CreatedAt = faker.Date.Past(1).ToUniversalTime(),
                UpdatedAt = faker.Date.Recent(30).ToUniversalTime()
            };

            int itemCount = faker.Random.Int(2, 4);
            foreach (var product in faker.PickRandom(products, itemCount))
            {
                cart.Items.Add(new CartItem
                {
                    Id        = Guid.NewGuid(),
                    CartId    = cart.Id,
                    ProductId = product.Id,
                    Quantity  = faker.Random.Int(1, 3),
                    UnitPrice = product.Price
                });
            }

            carts.Add(cart);
        }

        await context.Carts.AddRangeAsync(carts);
        await context.SaveChangesAsync();

        // ── Orders + OrderItemSnapshots (1 000 orders × avg 2 items ≈ 2 000) ──
        var orders = new List<Order>(1000);
        for (int i = 0; i < 1000; i++)
        {
            var order = new Order
            {
                Id          = Guid.NewGuid(),
                UserId      = $"seed-user-{faker.Random.Int(1, 500)}",
                CreatedAt   = faker.Date.Past(2).ToUniversalTime(),
                Status      = faker.PickRandom<OrderStatus>(),
                TotalAmount = 0
            };

            int itemCount = faker.Random.Int(1, 3);
            foreach (var product in faker.PickRandom(products, itemCount))
            {
                int qty = faker.Random.Int(1, 2);
                var snapshot = new OrderItemSnapshot
                {
                    Id          = Guid.NewGuid(),
                    ProductId   = product.Id,
                    ProductName = product.Name,
                    Quantity    = qty,
                    UnitPrice   = product.Price
                };
                order.Items.Add(snapshot);
                order.TotalAmount += snapshot.UnitPrice * snapshot.Quantity;
            }

            orders.Add(order);
        }

        await context.Orders.AddRangeAsync(orders);
        await context.SaveChangesAsync();
    }
}
