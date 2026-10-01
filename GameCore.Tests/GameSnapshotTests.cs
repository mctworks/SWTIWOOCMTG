using System.Linq;
using System.Text.Json;
using SWTIWOOCMTG;
using Xunit;

namespace SWTIWOOCMTG.Tests;

public class GameSnapshotTests
{
    private static (GameSession s, Game g, Player a, Player b) NewSession()
    {
        var g = new Game(new Random(1));
        g.TryAddPlayer("A", out _);
        g.TryAddPlayer("B", out _);
        var s = new GameSession(g, _ => Task.CompletedTask);
        return (s, g, g.Players[0], g.Players[1]);
    }

    [Fact]
    public void Build_MapsOwnershipByPlayerId_NotByReference()
    {
        var (s, g, a, _) = NewSession();
        var space = g.Board[1];
        space.Owner = a;
        a.Properties.Add(space);

        var snap = GameSnapshotBuilder.Build(s);

        Assert.Equal(a.Id, snap.Board[1].OwnerId);

        var playerDto = snap.Players.Single(p => p.Id == a.Id);
        Assert.Contains(1, playerDto.PropertyIndices);
    }

    [Fact]
    public void Build_UnownedSpace_HasNullOwnerId()
    {
        var (s, _, _, _) = NewSession();
        var snap = GameSnapshotBuilder.Build(s);
        Assert.Null(snap.Board[3].OwnerId);
    }

    [Fact]
    public void Build_PendingDebt_ReferencesDebtorAndCreditorById()
    {
        var (s, g, a, b) = NewSession();
        a.Money = 0;
        g.TryPay(a, 30, b);

        var snap = GameSnapshotBuilder.Build(s);

        var debt = Assert.Single(snap.PendingDebts);
        Assert.Equal(a.Id, debt.DebtorId);
        Assert.Equal(b.Id, debt.CreditorId);
        Assert.Equal(30, debt.Amount);
    }

    [Fact]
    public void Build_BankDebt_HasNullCreditorId()
    {
        var (s, g, a, _) = NewSession();
        a.Money = 0;
        g.TryPay(a, 10, null);

        var snap = GameSnapshotBuilder.Build(s);
        Assert.Null(Assert.Single(snap.PendingDebts).CreditorId);
    }

    [Fact]
    public void Build_ReflectsCurrentPlayerAndPhaseAfterARoll()
    {
        var (s, g, a, b) = NewSession();
        a.Position = 36; // +4 -> Payday (0): non-ownable, and non-double so the turn actually advances
        s.DiceSource = () => (1, 3);
        s.Roll();

        var snap = GameSnapshotBuilder.Build(s);

        Assert.Equal(b.Id, snap.CurrentPlayerId);
        Assert.Equal(TurnPhase.AwaitingRoll, snap.Phase);
    }

    [Fact]
    public void Build_PendingPurchase_MapsToBoardIndex()
    {
        var (s, g, a, _) = NewSession();
        a.Position = 0;
        s.DiceSource = () => (1, 0); // lands on index 1, an unowned property

        s.Roll();
        var snap = GameSnapshotBuilder.Build(s);

        Assert.Equal(1, snap.PendingPurchaseIndex);
        Assert.Equal(TurnPhase.AwaitingPurchase, snap.Phase);
    }

    [Fact]
    public void Snapshot_SerializesToJsonWithoutErrors()
    {
        var (s, g, a, b) = NewSession();
        g.TryPurchaseProperty(a, g.Board[1]);
        a.Money = 0;
        g.TryPay(a, 20, b);

        var snap = GameSnapshotBuilder.Build(s);
        var json = JsonSerializer.Serialize(snap);

        Assert.Contains(a.Id.ToString(), json);
        Assert.Contains(b.Id.ToString(), json);
    }

    [Fact]
    public void Build_IncludesRecentLogLines()
    {
        var (s, g, a, _) = NewSession();
        a.Position = 0;
        s.DiceSource = () => (1, 0); // lands on index 1, a Property space: LandOnSpace always logs this

        s.Roll();
        var snap = GameSnapshotBuilder.Build(s);

        Assert.NotEmpty(snap.Log);
        Assert.Contains(snap.Log, line => line.Contains("lands on"));
    }

    [Fact]
    public void Snapshot_RoundTripsThroughJson()
    {
        var (s, g, a, _) = NewSession();
        var snap = GameSnapshotBuilder.Build(s);

        var json = JsonSerializer.Serialize(snap);
        var back = JsonSerializer.Deserialize<GameSnapshot>(json);

        Assert.NotNull(back);
        Assert.Equal(snap.Players.Count, back!.Players.Count);
        Assert.Equal(a.Id, back.Players[0].Id);
        Assert.Equal(snap.Board.Count, back.Board.Count);
    }
}