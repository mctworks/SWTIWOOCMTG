using SWTIWOOCMTG;
using Xunit;

namespace SWTIWOOCMTG.Tests;

public class GameRoomRegistryTests
{
    private static readonly Func<int, Task> NoDelay = _ => Task.CompletedTask;

    // ── lifecycle ──────────────────────────────────────────────────────────────

    [Fact]
    public void CreateRoom_ReturnsIndependentRoomsWithUniqueIds()
    {
        var registry = new GameRoomRegistry(cpuDelay: NoDelay);

        var a = registry.CreateRoom();
        var b = registry.CreateRoom();

        Assert.NotEqual(a.Id, b.Id);
        Assert.NotSame(a.Game, b.Game);
        Assert.NotSame(a.Session, b.Session);
        Assert.Equal(2, registry.Count);
    }

    [Fact]
    public void Find_ReturnsTheRoomById_OrNullForUnknownId()
    {
        var registry = new GameRoomRegistry(cpuDelay: NoDelay);
        var room = registry.CreateRoom();

        Assert.Same(room, registry.Find(room.Id));
        Assert.Null(registry.Find("NOSUCHROOM"));
    }

    [Fact]
    public void Remove_DropsTheRoom()
    {
        var registry = new GameRoomRegistry(cpuDelay: NoDelay);
        var room = registry.CreateRoom();

        Assert.True(registry.Remove(room.Id));
        Assert.Null(registry.Find(room.Id));
        Assert.False(registry.Remove(room.Id)); // already gone
    }

    [Fact]
    public void ListRoomIds_ReflectsCurrentRooms()
    {
        var registry = new GameRoomRegistry(cpuDelay: NoDelay);
        var a = registry.CreateRoom();
        var b = registry.CreateRoom();

        var ids = registry.ListRoomIds();

        Assert.Contains(a.Id, ids);
        Assert.Contains(b.Id, ids);
        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public void CreateRoom_RetriesOnIdCollision()
    {
        var calls = 0;
        // First two calls collide on "DUPE"; the third call (for the second room) succeeds.
        string[] scripted = { "DUPE", "DUPE", "UNIQUE" };
        var registry = new GameRoomRegistry(
            idGenerator: () => scripted[Math.Min(calls++, scripted.Length - 1)],
            cpuDelay: NoDelay);

        var first = registry.CreateRoom();
        var second = registry.CreateRoom();

        Assert.Equal("DUPE", first.Id);
        Assert.Equal("UNIQUE", second.Id);
        Assert.Equal(2, registry.Count);
    }

    [Fact]
    public void CreateRoom_WithSeededRng_ProducesDeterministicGame()
    {
        var registry = new GameRoomRegistry(cpuDelay: NoDelay);

        var a = registry.CreateRoom(new Random(42));
        var b = registry.CreateRoom(new Random(42));

        Assert.Equal(a.Game.RollDice(), b.Game.RollDice());
    }

    // ── idle cleanup ───────────────────────────────────────────────────────────

    [Fact]
    public void RemoveIdleRooms_RemovesRoomsOlderThanMaxAge()
    {
        var registry = new GameRoomRegistry(cpuDelay: NoDelay);
        var room = registry.CreateRoom();

        int removed = registry.RemoveIdleRooms(TimeSpan.Zero);

        Assert.Equal(1, removed);
        Assert.Null(registry.Find(room.Id));
    }

    [Fact]
    public async Task RemoveIdleRooms_KeepsRoomsActiveWithinMaxAge()
    {
        var registry = new GameRoomRegistry(cpuDelay: NoDelay);
        var room = registry.CreateRoom();

        await room.RunAsync(s => { }); // touches LastActivityUtc

        int removed = registry.RemoveIdleRooms(TimeSpan.FromMinutes(5));

        Assert.Equal(0, removed);
        Assert.NotNull(registry.Find(room.Id));
    }

    // ── the CPU-drain correctness fix ────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_DrainsTheCpuTurnItTriggers_BeforeReturning()
    {
        var registry = new GameRoomRegistry(cpuDelay: NoDelay);
        var room = registry.CreateRoom(new Random(1));

        room.Game.TryAddPlayer("Human", out _);
        room.Game.TryAddPlayer("CPU", out _);
        var human = room.Game.Players[0];
        var cpu = room.Game.Players[1];
        cpu.Ai = new PlaceholderPlayerAI();

        human.Position = 36; // +4 (non-doubles) -> Payday (0): ends the human's turn cleanly
        room.Session.DiceSource = () => (1, 3);

        await room.RunAsync(s => s.Roll());

        // If the room only ran the human's Roll() and let the CPU's turn fire off
        // unsupervised, the CPU would still be sitting at its starting position (0) and
        // it would still be the CPU's turn by the time control returns here.
        Assert.Equal(4, cpu.Position); // CPU's own roll (also sum 4) already ran: 0 -> 4
        Assert.Same(human, room.Game.CurrentPlayer); // and control is back with the human
    }

    [Fact]
    public async Task RunAsync_NoOpsCleanly_WhenNoCpuTurnIsTriggered()
    {
        var registry = new GameRoomRegistry(cpuDelay: NoDelay);
        var room = registry.CreateRoom();
        room.Game.TryAddPlayer("A", out _);
        room.Game.TryAddPlayer("B", out _); // both human: nothing for RunCpuTurnsAsync to drive

        var result = await room.RunAsync(s => s.Game.TrySetPlayerCount(2, out _));

        Assert.True(result);
    }
}