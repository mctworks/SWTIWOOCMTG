namespace SWTIWOOCMTG.Server;

public class RoomCleanupService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaxRoomAge = TimeSpan.FromHours(2);

    private readonly GameRoomRegistry _registry;
    private readonly ILogger<RoomCleanupService> _logger;

    public RoomCleanupService(GameRoomRegistry registry, ILogger<RoomCleanupService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            int removed = _registry.RemoveIdleRooms(MaxRoomAge);
            if (removed > 0)
                _logger.LogInformation("Removed {Count} idle room(s).", removed);
        }
    }
}
