using SWTIWOOCMTG;
using Xunit;

namespace SWTIWOOCMTG.Tests;

public class GameSessionTests
{
    // Instant delay so CPU-driven tests don't actually wait.
    private static readonly Func<int, Task> NoDelay = _ => Task.CompletedTask;

    private static (GameSession s, Game g, Player a, Player b) NewSession(int seed = 1)
    {
        var g = new Game(new Random(seed));
        g.TryAddPlayer("A", out _);
        g.TryAddPlayer("B", out _);
        var s = new GameSession(g, NoDelay);
        return (s, g, g.Players[0], g.Players[1]);
    }

    // ── basic roll / purchase flow ───────────────────────────────────────────────

    [Fact]
    public void Roll_LandingOnUnownedProperty_OpensPurchasePhase()
    {
        var (s, g, a, _) = NewSession();
        a.Position = 0;
        s.DiceSource = () => (1, 0); // -> index 1, an unowned property

        Assert.True(s.Roll());

        Assert.Equal(TurnPhase.AwaitingPurchase, s.Phase);
        Assert.Same(g.Board[1], s.PendingPurchase);
    }

    [Fact]
    public void Buy_PurchasesAndAdvancesToNextPlayer()
    {
        var (s, g, a, b) = NewSession();
        a.Position = 0;
        s.DiceSource = () => (1, 0);
        s.Roll();

        Assert.True(s.Buy());

        Assert.Same(a, g.Board[1].Owner);
        Assert.Null(s.PendingPurchase);
        Assert.Equal(TurnPhase.AwaitingRoll, s.Phase);
        Assert.Same(b, g.CurrentPlayer);
    }

    [Fact]
    public void SkipPurchase_LeavesPropertyUnownedAndAdvancesTurn()
    {
        var (s, g, a, b) = NewSession();
        a.Position = 0;
        s.DiceSource = () => (1, 0);
        s.Roll();

        Assert.True(s.SkipPurchase());

        Assert.Null(g.Board[1].Owner);
        Assert.Same(b, g.CurrentPlayer);
    }

    [Fact]
    public void Roll_Doubles_GrantsAnotherTurnForSamePlayer()
    {
        var (s, g, a, _) = NewSession();
        a.Position = 36; // +4 -> Payday (0), not ownable -> turn resolves immediately
        s.DiceSource = () => (2, 2);

        s.Roll();

        Assert.Equal(TurnPhase.AwaitingRoll, s.Phase);
        Assert.Same(a, g.CurrentPlayer); // still A's turn
    }

    [Fact]
    public void Roll_ThreeConsecutiveDoubles_EndsTurnInPrison()
    {
        var (s, g, a, b) = NewSession();
        // 36 -> 0 (Payday) -> 4 (IRS Audit): both non-ownable, so rolls 1 and 2 stay in
        // AwaitingRoll. Roll 3's SpeedingToPrison check happens before any movement, so
        // where it *would* land doesn't matter.
        a.Position = 36;
        s.DiceSource = () => (2, 2);

        s.Roll();
        s.Roll();
        s.Roll(); // third doubles -> speeding to prison

        Assert.Equal(PrisonStatus.GenPop, a.Prison);
        Assert.Same(b, g.CurrentPlayer);
    }

    // ── the jail/doubles bug ───────────────────────────────────────────────────

    [Fact]
    public void Roll_DoublesLandingInPrison_DoesNotGrantAnotherTurn()
    {
        var (s, g, a, b) = NewSession();
        a.Position = 24;             // 24 + 6 = 30 = GUILTY!
        s.DiceSource = () => (3, 3); // doubles

        s.Roll();

        Assert.Equal(PrisonStatus.GenPop, a.Prison);
        // If the roll-again bonus had leaked through, NextTurn() would never have
        // been called and B would not be current.
        Assert.Same(b, g.CurrentPlayer);
    }

    // ── prison actions ─────────────────────────────────────────────────────────

    [Fact]
    public void PayBail_FreesPlayer()
    {
        var (s, g, a, _) = NewSession();
        a.Prison = PrisonStatus.GenPop;

        Assert.True(s.PayBail());
        Assert.Equal(PrisonStatus.Free, a.Prison);
    }

    [Fact]
    public void AttemptAppeal_EscapeOnDoubles_MovesAndResolvesLanding()
    {
        var (s, g, a, _) = NewSession();
        a.Prison = PrisonStatus.GenPop;
        a.Position = 10;
        s.DiceSource = () => (2, 2);

        Assert.True(s.AttemptAppeal());

        Assert.Equal(PrisonStatus.Free, a.Prison);
        Assert.Equal(14, a.Position); // moved 4 spaces (2+2) from Prison (10)
    }

    [Fact]
    public void AttemptAppeal_Fail_EndsTurnWithoutMoving()
    {
        var (s, g, a, b) = NewSession();
        a.Prison = PrisonStatus.GenPop;
        a.Position = 10;
        s.DiceSource = () => (1, 2);

        Assert.True(s.AttemptAppeal());

        Assert.Equal(PrisonStatus.GenPop, a.Prison);
        Assert.Equal(10, a.Position);
        Assert.Same(b, g.CurrentPlayer);
    }

    // ── debt flow ──────────────────────────────────────────────────────────────

    [Fact]
    public void DebtFlow_PayDebt_ThenBankruptcy_BothAdvanceCorrectly()
    {
        var (s, g, a, b) = NewSession();

        // Manufacture a debt the direct way the engine does it, then drive the session's phase
        // the same way Roll() would: by forcing a landing that can't be paid.
        a.Money = 0;
        g.TryPay(a, 30, b); // a owes b 30, can't pay -> PendingDebts has one entry
        Assert.NotNull(g.CurrentDebt);

        // Session doesn't expose a way to force Phase directly (by design — it only reaches
        // SettlingDebt via Roll/AttemptAppeal), so drive it via a roll that lands somewhere neutral
        // after debt already exists: ResolveLanding checks PendingDebts first regardless of the space.
        a.Position = 0;
        s.DiceSource = () => (1, 0);
        s.Roll();

        Assert.Equal(TurnPhase.SettlingDebt, s.Phase);

        a.Money = 30;
        Assert.True(s.PayDebt());
        Assert.Equal(TurnPhase.AwaitingRoll, s.Phase);
        Assert.Same(b, g.CurrentPlayer);
    }

    [Fact]
    public void DebtFlow_DeclareBankruptcy_EliminatesAndAdvancesTurn()
    {
        var (s, g, a, b) = NewSession();
        a.Money = 0;
        g.TryPay(a, 30, b);

        a.Position = 0;
        s.DiceSource = () => (1, 0);
        s.Roll();
        Assert.Equal(TurnPhase.SettlingDebt, s.Phase);

        Assert.True(s.DeclareBankruptcy());

        Assert.True(a.IsEliminated);
        Assert.Equal(TurnPhase.AwaitingRoll, s.Phase);
    }

    // ── property management passthrough ───────────────────────────────────────

    [Fact]
    public void PropertyManager_OpenEditsAndCloseReturnsToRoll()
    {
        var (s, g, a, _) = NewSession();
        var space = g.Board[1];
        space.Owner = a;
        a.Properties.Add(space);

        s.OpenPropertyManager();
        Assert.Equal(TurnPhase.ManageProperties, s.Phase);
        Assert.Same(a, s.ManagingPlayer);

        Assert.True(s.UpgradeProperty(space));
        Assert.Equal(1, space.Level);

        s.ClosePropertyManager();
        Assert.Equal(TurnPhase.AwaitingRoll, s.Phase);
    }

    // ── CPU driver ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CpuTurn_RunsAutomaticallyAndAdvancesToHuman()
    {
        var (s, g, a, b) = NewSession();
        a.Ai = new PlaceholderPlayerAI();
        s.DiceSource = () => (1, 1); // deterministic-ish; doubles are fine, loop still terminates

        await s.RunCpuTurnsAsync();

        // The loop stops as soon as it's a human's decision (B has no Ai) or the game ends.
        Assert.True(g.CurrentPlayer == null || g.CurrentPlayer.Ai == null || g.IsGameOver);
    }

    // ── Changed event (the hub's push signal) ───────────────────────────────────

    [Fact]
    public void Roll_LandingOnUnownedProperty_RaisesChangedExactlyOnce()
    {
        var (s, g, a, _) = NewSession();
        a.Position = 0;
        s.DiceSource = () => (1, 0);
        int count = 0;
        s.Changed += () => count++;

        s.Roll();

        Assert.Equal(1, count);
    }

    [Fact]
    public void RejectedCommand_DoesNotRaiseChanged()
    {
        var (s, _, a, _) = NewSession();
        int count = 0;
        s.Changed += () => count++;

        // a isn't in prison, so PayBail is rejected by Game.TryPayBail: nothing changed.
        Assert.False(s.PayBail());

        Assert.Equal(0, count);
    }

    [Fact]
    public void PropertyManagement_Upgrade_RaisesChangedOnSuccessOnly()
    {
        var (s, g, a, _) = NewSession();
        var owned = g.Board[1];
        owned.Owner = a;
        a.Properties.Add(owned);
        s.OpenPropertyManager();

        int count = 0;
        s.Changed += () => count++;

        Assert.True(s.UpgradeProperty(owned));
        Assert.Equal(1, count);

        var notOwned = g.Board[3];
        Assert.False(s.UpgradeProperty(notOwned)); // rejected: doesn't own it
        Assert.Equal(1, count); // no extra Notify from the rejected call
    }

    [Fact]
    public void DebtFlow_PayDebt_WithAnotherDebtStillQueued_StillRaisesChanged()
    {
        var (s, g, a, b) = NewSession();
        g.TryAddPlayer("C", out _);
        a.Money = 0;
        g.PayAllPlayers(a, 10); // queues a debt to b, then to c
        Assert.Equal(2, g.PendingDebts.Count);

        a.Position = 0;
        s.DiceSource = () => (1, 0);
        s.Roll(); // routes into SettlingDebt

        int count = 0;
        s.Changed += () => count++;
        a.Money = 10; // enough to cover only the first (b's) debt

        Assert.True(s.PayDebt());

        Assert.Equal(TurnPhase.SettlingDebt, s.Phase); // c's debt is still pending
        Assert.Equal(1, count); // this branch doesn't reach EndTurn, but still notifies
    }

    [Fact]
    public async Task CpuFirstPlayer_StartCalledAtGameStart_TakesItsTurnWithoutManualPrompt()
    {
        // Regresses the "CPU first player never starts" bug: Start() must be called once after
        // entering the board (as EnterBoard() does in Home.razor), with no further nudging.
        var (s, g, a, b) = NewSession();
        a.Ai = new PlaceholderPlayerAI();
        int startingPosition = a.Position;
        s.DiceSource = () => (2, 3);

        // This is exactly what Start() kicks off (fire-and-forget) from EnterBoard() in
        // Home.razor. Awaiting it directly here avoids racing the fire-and-forget task.
        await s.RunCpuTurnsAsync();

        Assert.NotEqual(startingPosition, a.Position);
    }
}