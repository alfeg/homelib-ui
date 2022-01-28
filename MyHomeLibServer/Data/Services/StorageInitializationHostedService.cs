using Microsoft.EntityFrameworkCore;

namespace MyHomeLibServer.Data.Services;

public class StorageInitializationHostedService : IHostedService
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<StorageInitializationHostedService> logger;

    public StorageInitializationHostedService(IServiceProvider serviceProvider,
        ILogger<StorageInitializationHostedService> logger)
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibDbContext>();
        logger.LogInformation("Start of Database migrations if needed");
        if (dbContext.Database.IsRelational())
            await dbContext.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrated");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}