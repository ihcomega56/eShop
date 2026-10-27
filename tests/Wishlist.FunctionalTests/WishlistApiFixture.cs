using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using eShop.Wishlist.API.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace eShop.Wishlist.FunctionalTests;

public sealed class WishlistApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly IHost _app;
    private readonly IResourceBuilder<PostgresServerResource> _database;
    private string _databaseConnectionString = null!;

    public WishlistApiFixture()
    {
        var options = new DistributedApplicationOptions
        {
            AssemblyName = typeof(WishlistApiFixture).Assembly.FullName,
            DisableDashboard = true
        };
        var appBuilder = DistributedApplication.CreateBuilder(options);
        _database = appBuilder.AddPostgres("wishlistdb");
        _app = appBuilder.Build();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{_database.Resource.Name}"] = _databaseConnectionString,
                ["Identity:Url"] = "http://identity.test",
                ["Identity:Audience"] = "wishlist"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICatalogProductClient>();
            services.AddSingleton<TestCatalogProductClient>();
            services.AddSingleton<ICatalogProductClient>(provider => provider.GetRequiredService<TestCatalogProductClient>());
            services.AddSingleton<IStartupFilter>(new TestAuthenticationStartupFilter());
        });
        return base.CreateHost(builder);
    }

    public async ValueTask InitializeAsync()
    {
        await _app.StartAsync();
        using var startupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _app.Services
            .GetRequiredService<ResourceNotificationService>()
            .WaitForResourceHealthyAsync(_database.Resource.Name, startupTimeout.Token);
        _databaseConnectionString = (await _database.Resource.GetConnectionStringAsync())!;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _app.StopAsync();
        if (_app is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            _app.Dispose();
        }
    }

    public HttpClient CreateClient(string userId)
    {
        var client = CreateDefaultClient();
        client.DefaultRequestHeaders.Add("X-Test-User", userId);
        return client;
    }

    public HttpClient CreateAnonymousClient()
    {
        var client = CreateDefaultClient();
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        return client;
    }

    public TestCatalogProductClient CatalogClient => Services.GetRequiredService<TestCatalogProductClient>();

    private sealed class TestAuthenticationStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.UseMiddleware<TestAuthenticationMiddleware>();
                next(app);
            };
    }
}

public sealed class TestCatalogProductClient : ICatalogProductClient
{
    private readonly Dictionary<int, CatalogProduct> _products = new()
    {
        [42] = new(42, AvailableStock: 0),
        [43] = new(43, AvailableStock: 10)
    };

    public Task<CatalogProduct?> GetProductAsync(int productId, CancellationToken cancellationToken)
    {
        _products.TryGetValue(productId, out var product);
        return Task.FromResult(product);
    }
}
