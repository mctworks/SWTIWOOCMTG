namespace SWTIWOOCMTG.Server;

public record CreateRoomResponse(string RoomId);

public record RoomSummary(string RoomId, int PlayerCount, bool IsGameOver, DateTime CreatedAtUtc);

public record JoinRoomRequest(string Name);

public record JoinRoomResponse(Guid PlayerId, GameSnapshot Snapshot);
