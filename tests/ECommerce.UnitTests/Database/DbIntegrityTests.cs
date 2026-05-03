using AutoFixture;
using ECommerce.Domain;
using ECommerce.Infrastructure.Persistence;
using ECommerce.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

public class DbIntegrityTests : IAsyncLifetime
{
    private PostgreSqlContainer? _dbContainer;
    private bool _dockerAvailable;
    private readonly Fixture _fixture = new();

    public async Task InitializeAsync()
    {
        try
        {
            _dbContainer = new PostgreSqlBuilder()
                .WithImage("postgres:15-alpine")
                .WithDatabase("testdb")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _dbContainer.StartAsync();
            _dockerAvailable = true;
        }
        catch
        {
            _dockerAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_dockerAvailable && _dbContainer != null)
            await _dbContainer.DisposeAsync();
    }

    private DbContextOptions<AppDbContext> GetOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dbContainer!.GetConnectionString())
            .Options;

    [Fact]
    public async Task Sku_ShouldBeUnique()
    {
        if (!_dockerAvailable) return;

        var options = GetOptions();
        await using var context = new AppDbContext(options);
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        var p1 = new Product { Id = Guid.NewGuid(), SKU = "DUPLICATE", Name = "P1", Price = 10m, StockQuantity = 1 };
        var p2 = new Product { Id = Guid.NewGuid(), SKU = "DUPLICATE", Name = "P2", Price = 10m, StockQuantity = 1 };

        context.Products.Add(p1);
        await context.SaveChangesAsync();

        context.Products.Add(p2);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Checkout_ShouldReduceStockAtomically()
    {
        if (!_dockerAvailable) return;

        var options = GetOptions();
        var productId = Guid.NewGuid();

        await using (var setupContext = new AppDbContext(options))
        {
            await setupContext.Database.EnsureDeletedAsync();
            await setupContext.Database.EnsureCreatedAsync();

            var atomicProduct = new Product
            {
                Id = productId,
                Name = "Atomic Product",
                Description = "Atomicity test",
                Price = 20m,
                StockQuantity = 1,
                Category = "Test",
                SKU = Guid.NewGuid().ToString()
            };
            setupContext.Products.Add(atomicProduct);

            setupContext.Carts.AddRange(
                new Cart
                {
                    Id = Guid.NewGuid(), UserId = "user1",
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                    Items = [new CartItem { Id = Guid.NewGuid(), ProductId = productId, Quantity = 1, UnitPrice = 20m }]
                },
                new Cart
                {
                    Id = Guid.NewGuid(), UserId = "user2",
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                    Items = [new CartItem { Id = Guid.NewGuid(), ProductId = productId, Quantity = 1, UnitPrice = 20m }]
                });

            await setupContext.SaveChangesAsync();
        }

        var gate = new TaskCompletionSource<bool>();

        var t1 = Task.Run(async () =>
        {
            await gate.Task;
            await using var ctx = new AppDbContext(options);
            try { await new CartService(ctx).CheckoutAsync("user1"); return true; }
            catch { return false; }
        });

        var t2 = Task.Run(async () =>
        {
            await gate.Task;
            await using var ctx = new AppDbContext(options);
            try { await new CartService(ctx).CheckoutAsync("user2"); return true; }
            catch { return false; }
        });

        gate.SetResult(true);
        var results = await Task.WhenAll(t1, t2);

        Assert.Contains(true, results);
        Assert.Contains(false, results);

        await using var verifyContext = new AppDbContext(options);
        var product = await verifyContext.Products.FindAsync(productId);
        Assert.NotNull(product);
        Assert.Equal(0, product!.StockQuantity);
    }

    [Fact]
    public async Task OrderSnapshot_ShouldPreservePriceAndDetails()
    {
        if (!_dockerAvailable) return;

        var options = GetOptions();
        var productId = Guid.NewGuid();

        await using (var setupContext = new AppDbContext(options))
        {
            await setupContext.Database.EnsureDeletedAsync();
            await setupContext.Database.EnsureCreatedAsync();

            setupContext.Products.Add(new Product
            {
                Id = productId, Name = "Snapshot Product", Description = "Snapshot test",
                Price = 30m, StockQuantity = 5, Category = "Test", SKU = Guid.NewGuid().ToString()
            });
            setupContext.Carts.Add(new Cart
            {
                Id = Guid.NewGuid(), UserId = "snapshot-user",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                Items = [new CartItem { Id = Guid.NewGuid(), ProductId = productId, Quantity = 1, UnitPrice = 30m }]
            });
            await setupContext.SaveChangesAsync();
        }

        await using (var checkoutCtx = new AppDbContext(options))
        {
            var order = await new CartService(checkoutCtx).CheckoutAsync("snapshot-user");
            Assert.Equal(30m, order.Items.First().UnitPrice);
            Assert.Equal(30m, order.TotalAmount);
            Assert.Equal("Snapshot Product", order.Items.First().ProductName);
        }

        await using (var verifyCtx = new AppDbContext(options))
        {
            var order = await verifyCtx.Orders.Include(o => o.Items).FirstOrDefaultAsync();
            Assert.NotNull(order);
            Assert.Equal(30m, order!.Items.First().UnitPrice);
            Assert.Equal(30m, order.TotalAmount);
            Assert.Equal("Snapshot Product", order.Items.First().ProductName);
        }
    }

    [Fact]
    public async Task DeleteProduct_ShouldNotDeleteOrders_ButShouldHandleReference()
    {
        if (!_dockerAvailable) return;

        var options = GetOptions();
        await using var context = new AppDbContext(options);
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        var product = _fixture.Build<Product>()
            .With(p => p.Price, 10m)
            .With(p => p.StockQuantity, 5)
            .With(p => p.SKU, Guid.NewGuid().ToString())
            .Create();
        context.Products.Add(product);
        context.Orders.Add(new Order
        {
            UserId = "test",
            Items =
            [
                new OrderItemSnapshot
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    UnitPrice = 10m,
                    Quantity = 1
                }
            ]
        });
        await context.SaveChangesAsync();

        context.Products.Remove(product);
        await context.SaveChangesAsync();

        var savedOrder = await context.Orders.Include(o => o.Items).FirstOrDefaultAsync();
        Assert.NotNull(savedOrder);
        Assert.Equal(product.Name, savedOrder!.Items.First().ProductName);
    }
}
