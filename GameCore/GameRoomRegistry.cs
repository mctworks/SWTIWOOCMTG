using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SWTIWOOCMTG;

/// <summary>
/// One hosted game: its Game/GameSession pair, plus a gate that serializes all access to it.
///
/// GameSession.Start() kicks off the CPU driver as fire-and-forget, which is safe for the
/// desktop app because only one thread of control (Blazor's own dispatcher) ever calls into
/// a given GameSession. That stops being true once multiple connections can reach the same
/// room, so every unit of work here goes through RunAsync, which serializes access with a
/// SemaphoreSlim (safe across awaits, unlike lock) and, critically, awaits
/// Session.RunCpuTurnsAsync() after the command runs and before releasing the gate. That
/// turns the fire-and-forget CPU turn into something the caller actually waits on, so a
/// second connection can't reach in mid-CPU-turn. RunCpuTurnsAsync() is idempotent and
/// returns immediately when there's nothing to drive, so calling it unconditionally after
/// every command is cheap and correct even when the command didn't touch a CPU turn at all.
/// </summary>
public class GameRoom
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal GameRoom(string id, Game game, GameSession session, DateTime nowUtc)
    {
        Id = id;
        Game = game;
        Session = session;
        CreatedAtUtc = nowUtc;
        LastActivityUtc = nowUtc;
    }

    public string Id { get; }
    public Game Game { get; }
    public GameSession Session { get; }
    public DateTime CreatedAtUtc { get; }
    public DateTime LastActivityUtc { get; private set; }

    /// <summary>Runs a synchronous command against this room's session with exclusive access,
    /// then drains any CPU turn it triggered before returning.</summary>
    public async Task<T> RunAsync<T>(Func<GameSession, T> command)
    {
        await _gate.WaitAsync();
        try
        {
            LastActivityUtc = DateTime.UtcNow;
            var result = command(Session);
            await Session.RunCpuTurnsAsync();
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Void overload of <see cref="RunAsync{T}"/>.</summary>
    public Task RunAsync(Action<GameSession> command) =>
        RunAsync<object?>(s => { command(s); return null; });

    public bool IsIdleSince(DateTime cutoffUtc) => LastActivityUtc < cutoffUtc;
}

/// <summary>
/// Owns the set of live GameRooms for a process. Unlike the single DI-singleton Game/GameSession
/// the desktop app uses for local play, this supports many concurrent games, each independently
/// addressable by a short room code and safe under concurrent access via GameRoom.RunAsync.
///
/// Has no timer of its own: GameCore stays free of hosting-framework dependencies, so a host
/// (e.g. an IHostedService in the future Kestrel project) is expected to call
/// RemoveIdleRooms periodically on whatever schedule it likes.
/// </summary>
public class GameRoomRegistry
{
    private static readonly char[] Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789".ToCharArray(); // no 0/O/1/I/L

    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();
    private readonly Func<string> _idGenerator;
    private readonly Func<int, Task> _cpuDelay;

    /// <param name="idGenerator">Room ID generator. Defaults to a 6-character code drawn from an
    /// alphabet with ambiguous characters removed. Injectable for tests and for anyone who wants
    /// a different scheme.</param>
    /// <param name="cpuDelay">Pacing passed to every room's GameSession. Production should leave
    /// this as real Task.Delay (the default); tests pass an instant one.</param>
    public GameRoomRegistry(Func<string>? idGenerator = null, Func<int, Task>? cpuDelay = null)
    {
        _idGenerator = idGenerator ?? DefaultIdGenerator;
        _cpuDelay = cpuDelay ?? (ms => Task.Delay(ms));
    }

    public int Count => _rooms.Count;

    public IEnumerable<GameRoom> Rooms => _rooms.Values;

    /// <summary>Creates a new room with a fresh Game/GameSession pair. Retries on the
    /// astronomically unlikely case of a generated ID collision.</summary>
    /// <param name="rng">Optional seeded RNG for the new Game, mainly for deterministic tests.</param>
    public GameRoom CreateRoom(Random? rng = null)
    {
        while (true)
        {
            var id = _idGenerator();
            var game = rng == null ? new Game() : new Game(rng);
            var session = new GameSession(game, _cpuDelay);
            var room = new GameRoom(id, game, session, DateTime.UtcNow);

            if (_rooms.TryAdd(id, room))
                return room;
        }
    }

    public GameRoom? Find(string id) => _rooms.TryGetValue(id, out var room) ? room : null;

    public bool Remove(string id) => _rooms.TryRemove(id, out _);

    public IReadOnlyList<string> ListRoomIds() => _rooms.Keys.ToList();

    /// <summary>Removes every room with no activity since (now - maxAge). Returns how many were removed.</summary>
    public int RemoveIdleRooms(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        int removed = 0;

        foreach (var kvp in _rooms)
        {
            if (kvp.Value.IsIdleSince(cutoff) && _rooms.TryRemove(kvp.Key, out _))
                removed++;
        }

        return removed;
    }

    private static string DefaultIdGenerator()
    {
        Span<char> buffer = stackalloc char[6];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = Alphabet[Random.Shared.Next(Alphabet.Length)];
        return new string(buffer);
    }
}
