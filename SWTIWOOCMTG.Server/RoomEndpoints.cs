namespace SWTIWOOCMTG.Server;

public static class RoomEndpoints
{
    public static void MapRoomEndpoints(this WebApplication app)
    {
        var rooms = app.MapGroup("/api/rooms");

        rooms.MapPost("/", (GameRoomRegistry registry) =>
        {
            var room = registry.CreateRoom();
            return Results.Created($"/api/rooms/{room.Id}", new CreateRoomResponse(room.Id));
        });

        rooms.MapGet("/", (GameRoomRegistry registry) =>
        {
            var summaries = registry.Rooms
                .Select(r => new RoomSummary(r.Id, r.Game.Players.Count, r.Game.IsGameOver, r.CreatedAtUtc))
                .ToList();
            return Results.Ok(summaries);
        });

        rooms.MapGet("/{id}", async (string id, GameRoomRegistry registry) =>
        {
            var room = registry.Find(id);
            if (room is null) return Results.NotFound();

            var snapshot = await room.RunAsync(GameSnapshotBuilder.Build);
            return Results.Ok(snapshot);
        });

        rooms.MapPost("/{id}/players", async (string id, JoinRoomRequest request, GameRoomRegistry registry) =>
        {
            var room = registry.Find(id);
            if (room is null) return Results.NotFound();

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "Name can't be blank." });

            var (added, error, newPlayerId) = await room.RunAsync(s =>
            {
                bool ok = s.Game.TryAddPlayer(request.Name, out var e);
                Guid? id2 = ok ? s.Game.Players[^1].Id : null;
                return (ok, e, id2);
            });

            if (!added) return Results.BadRequest(new { error });

            var snapshot = await room.RunAsync(GameSnapshotBuilder.Build);
            return Results.Ok(new JoinRoomResponse(newPlayerId!.Value, snapshot));
        });

        rooms.MapDelete("/{id}", (string id, GameRoomRegistry registry) =>
            registry.Remove(id) ? Results.NoContent() : Results.NotFound());
    }
}
