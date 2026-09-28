namespace SWTIWOOCMTG;

public enum TurnPhase
{
    AwaitingRoll,
    AwaitingPurchase,
    AwaitingSpecialPurchase,
    ManageProperties,
    SettlingDebt
}

/// <summary>
/// Owns the turn state machine (phase, pending purchase, roll-again, CPU driver) on top of
/// <see cref="Game"/>. UI hosts render its state and forward button presses to its commands.
/// Every command returns false (and changes nothing) if it isn't valid in the current phase.
/// </summary>
public class GameSession
{
    private readonly Func<int, Task> _delay;
    private bool _rollAgainPending;
    private bool _cpuLoopRunning;

    public GameSession(Game game) : this(game, ms => Task.Delay(ms)) { }

    /// <param name="delay">Pacing between CPU actions. Tests pass an instant one.</param>
    public GameSession(Game game, Func<int, Task> delay)
    {
        Game = game;
        _delay = delay;
        DiceSource = () => Game.RollDice();
    }

    public Game Game { get; }
    public TurnPhase Phase { get; private set; } = TurnPhase.AwaitingRoll;
    public BoardSpace? PendingPurchase { get; private set; }
    public Player? ManagingPlayer { get; private set; }
    public int? Die1 { get; private set; }
    public int? Die2 { get; private set; }
    public string CurrentRoll { get; private set; } = "";
    public string Error { get; private set; } = "";

    /// <summary>Where dice come from. Defaults to the game's RNG; tests script it.</summary>
    public Func<(int d1, int d2)> DiceSource { get; set; }

    /// <summary>Raised when the CPU driver changes state, so a host can re-render.</summary>
    public event Action? Changed;
    private void Notify() => Changed?.Invoke();

    // ── Rolling & landing ──────────────────────────────────────────────────────

    public bool Roll()
    {
        var player = Game.CurrentPlayer;
        if (Phase != TurnPhase.AwaitingRoll || player == null || player.Prison != PrisonStatus.Free)
            return false;

        Game.ClearLastCard();
        var (d1, d2) = DiceSource();
        Die1 = d1; Die2 = d2;
        CurrentRoll = $"{player.Name} rolled [{d1}] [{d2}]{(d1 == d2 ? " DOUBLES!" : "")} ({d1 + d2} spaces)";

        var outcome = Game.RegisterRoll(d1, d2);

        if (outcome == RollOutcome.SpeedingToPrison)
        {
            Game.SendToPrison(player, PrisonStatus.GenPop, noPayday: true);
            Game.Log.Add($"{player.Name} rolled doubles three times in a row — busted for reckless speeding and sent straight to Prison!");
            _rollAgainPending = false;
            EndTurn();
            return true;
        }

        _rollAgainPending = outcome == RollOutcome.RollAgain;

        Game.MovePlayer(player, d1 + d2);
        Game.LandOnSpace(player, d1, d2);

        // A card or space may have jailed the player — no bonus roll for doubles.
        if (player.Prison != PrisonStatus.Free)
            _rollAgainPending = false;

        ResolveLanding(player);
        return true;
    }

    /// <summary>After landing: route to debt / special purchase / purchase offer, else end the turn.</summary>
    private void ResolveLanding(Player player)
    {
        if (Game.PendingDebts.Count > 0) { Phase = TurnPhase.SettlingDebt; return; }
        if (Game.PendingSpecialPurchase != null) { Phase = TurnPhase.AwaitingSpecialPurchase; return; }

        var space = Game.Board[player.Position];
        if (space.IsOwnable && space.Owner == null)
        {
            PendingPurchase = space;
            Phase = TurnPhase.AwaitingPurchase;
            return;
        }

        EndTurn();
    }

    private void EndTurn()
    {
        PendingPurchase = null;
        Phase = TurnPhase.AwaitingRoll;

        bool stillActive = Game.CurrentPlayer is { IsEliminated: false };

        if (_rollAgainPending && stillActive)
        {
            _rollAgainPending = false;
        }
        else
        {
            _rollAgainPending = false;
            Game.NextTurn();
        }

        Start(); // picks up here if the new current player is a CPU
    }

    // ── Purchases ──────────────────────────────────────────────────────────────

    public bool Buy()
    {
        var player = Game.CurrentPlayer;
        if (Phase != TurnPhase.AwaitingPurchase || PendingPurchase == null || player == null)
            return false;

        bool bought = Game.TryPurchaseProperty(player, PendingPurchase);
        EndTurn();
        return bought;
    }

    public bool SkipPurchase()
    {
        if (Phase != TurnPhase.AwaitingPurchase) return false;
        EndTurn();
        return true;
    }

    public bool BuySpecialProperty() => ResolveSpecial(true);
    public bool SkipSpecialPurchase() => ResolveSpecial(false);

    private bool ResolveSpecial(bool buy)
    {
        var player = Game.CurrentPlayer;
        if (Phase != TurnPhase.AwaitingSpecialPurchase || player == null) return false;

        Game.ResolveSpecialPurchase(player, buy);
        EndTurn();
        return true;
    }

    // ── Prison ─────────────────────────────────────────────────────────────────

    public bool PayBail()
    {
        var player = Game.CurrentPlayer;
        if (Phase != TurnPhase.AwaitingRoll || player == null) return false;

        bool ok = Game.TryPayBail(player, out var e);
        Error = e;
        return ok;
    }

    public bool UseLawyerToken()
    {
        var player = Game.CurrentPlayer;
        if (Phase != TurnPhase.AwaitingRoll || player == null || player.Prison == PrisonStatus.Free)
            return false;

        bool ok = Game.TryLawyerToken(player, out var e);
        Error = e;
        return ok;
    }

    public bool UseGoojfCard()
    {
        var player = Game.CurrentPlayer;
        if (Phase != TurnPhase.AwaitingRoll || player == null || player.Prison == PrisonStatus.Free)
            return false;

        bool ok = Game.TryGoojfCard(player, out var e);
        Error = e;
        return ok;
    }

    /// <summary>Roll for doubles from prison. Escaping on doubles moves the player and resolves the landing.</summary>
    public bool AttemptAppeal()
    {
        var player = Game.CurrentPlayer;
        if (Phase != TurnPhase.AwaitingRoll || player == null || player.Prison == PrisonStatus.Free)
            return false;

        var (d1, d2) = DiceSource();
        Die1 = d1; Die2 = d2;
        CurrentRoll = $"{player.Name} rolled [{d1}] [{d2}]";
        _rollAgainPending = false;

        if (Game.TryAppealWithDoubles(player, d1, d2))
        {
            Game.MovePlayer(player, d1 + d2);
            Game.LandOnSpace(player, d1, d2);
            ResolveLanding(player);
        }
        else
        {
            EndTurn();
        }
        return true;
    }

    // ── Property management ────────────────────────────────────────────────────

    public void OpenPropertyManager()
    {
        Error = "";
        ManagingPlayer = Game.CurrentDebt?.Debtor ?? Game.CurrentPlayer;
        Phase = TurnPhase.ManageProperties;
    }

    public void ClosePropertyManager()
    {
        Phase = Game.PendingDebts.Count > 0 ? TurnPhase.SettlingDebt : TurnPhase.AwaitingRoll;
        ManagingPlayer = null;
    }

    public bool UpgradeProperty(BoardSpace space)
    {
        if (ManagingPlayer == null) return false;
        bool ok = Game.TryUpgradeProperty(ManagingPlayer, space, out var e);
        Error = e;
        return ok;
    }

    public bool DowngradeProperty(BoardSpace space)
    {
        if (ManagingPlayer == null) return false;
        bool ok = Game.TryDowngradeProperty(ManagingPlayer, space, out var e);
        Error = e;
        return ok;
    }

    public bool MortgageProperty(BoardSpace space)
    {
        if (ManagingPlayer == null) return false;
        bool ok = Game.TryMortgageProperty(ManagingPlayer, space, out var e);
        Error = e;
        return ok;
    }

    public bool UnmortgageProperty(BoardSpace space)
    {
        if (ManagingPlayer == null) return false;
        bool ok = Game.TryUnmortgageProperty(ManagingPlayer, space, out var e);
        Error = e;
        return ok;
    }

    // ── Debt ───────────────────────────────────────────────────────────────────

    public bool PayDebt()
    {
        if (Phase != TurnPhase.SettlingDebt) return false;

        bool ok = Game.TryResolveDebt(out var e);
        Error = e;

        if (ok)
            Phase = Game.PendingDebts.Count > 0 ? TurnPhase.SettlingDebt : TurnPhase.AwaitingRoll;

        if (Game.PendingDebts.Count == 0 && Phase == TurnPhase.AwaitingRoll)
            EndTurn();
        else
            Start(); // remaining debts might belong to a CPU

        return ok;
    }

    public bool DeclareBankruptcy()
    {
        if (Phase != TurnPhase.SettlingDebt) return false;

        var debtor = Game.CurrentDebt?.Debtor;
        if (debtor == null) return false;

        Game.DeclareBankruptcy(debtor);
        Phase = Game.PendingDebts.Count > 0 ? TurnPhase.SettlingDebt : TurnPhase.AwaitingRoll;

        if (Game.PendingDebts.Count == 0)
            EndTurn();
        else
            Start();

        return true;
    }

    // ── CPU driver ─────────────────────────────────────────────────────────────

    /// <summary>Kick the CPU driver (fire-and-forget). Safe to call any time; overlapping runs are ignored.</summary>
    public void Start() { _ = RunCpuTurnsAsync(); }

    public async Task RunCpuTurnsAsync()
    {
        if (_cpuLoopRunning) return;
        _cpuLoopRunning = true;

        try
        {
            while (!Game.IsGameOver && await StepCpuAsync())
                await _delay(150); // brief beat between chained CPU actions
        }
        finally
        {
            _cpuLoopRunning = false;
        }
    }

    /// <summary>Performs one CPU action. Returns false when the next decision belongs to a human.</summary>
    private async Task<bool> StepCpuAsync()
    {
        // Debts are settled by the debtor, who isn't necessarily the current player.
        if (Phase == TurnPhase.SettlingDebt)
        {
            var debtor = Game.CurrentDebt?.Debtor;
            if (debtor?.Ai == null) return false;

            await _delay(500);
            debtor.Ai.HandleDebt(debtor, Game);
            Notify();
            await _delay(300);

            if (Game.CurrentDebt != null && debtor.Money >= Game.CurrentDebt.Amount)
                PayDebt();
            else if (Game.CurrentDebt != null)
                DeclareBankruptcy();

            Notify();
            return true;
        }

        var player = Game.CurrentPlayer;
        if (player?.Ai == null) return false;

        switch (Phase)
        {
            case TurnPhase.AwaitingRoll:
                player.Ai.HandleUpgrades(player, Game);
                Notify();
                await _delay(300);

                if (player.Prison != PrisonStatus.Free)
                {
                    await _delay(500);
                    Game.RunAiJailDecision(player);
                    Notify();
                    await _delay(500);

                    if (player.Prison == PrisonStatus.Free)
                        Roll();
                    else
                        AttemptAppeal(); // still jailed: roll for doubles / serve the turn
                }
                else
                {
                    await _delay(600); // TODO: replace with real dice-roll animation later
                    Roll();
                }
                Notify();
                return true;

            case TurnPhase.AwaitingPurchase:
                if (PendingPurchase == null) return false;
                await _delay(700); // TODO: "considering..." animation later
                if (player.Ai.WantsToBuyProperty(player, PendingPurchase, Game))
                    Buy();
                else
                    SkipPurchase();
                Notify();
                return true;

            case TurnPhase.AwaitingSpecialPurchase:
                if (Game.PendingSpecialPurchase == null) return false;
                await _delay(700);
                Game.ResolvePendingSpecialPurchaseForAi(player);
                EndTurn();
                Notify();
                return true;

            default:
                return false; // manual-only phase (e.g. ManageProperties)
        }
    }
}