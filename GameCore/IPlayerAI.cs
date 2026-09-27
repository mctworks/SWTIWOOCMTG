namespace SWTIWOOCMTG;

/// <summary>
/// Decision-point interface for CPU-controlled players. Game calls into these the
/// same way a human's choices arrive from the UI — the AI never calls back into Game
/// except through the methods Game already exposes (TryPurchaseProperty, TryPayBail, etc).
/// </summary>
public interface IPlayerAI
{
    /// <summary>Landed on an unowned Property/Rail/Utility. Return true to buy.</summary>
    bool WantsToBuyProperty(Player player, BoardSpace space, Game game);

    /// <summary>A Risk/Tactics card offered a special-priced purchase. Return true to buy.</summary>
    bool WantsToBuySpecialPurchase(Player player, BoardSpace space, int price, Game game);

    /// <summary>Chance to sell upgrades / mortgage before an outstanding debt is settled.</summary>
    void HandleDebt(Player player, Game game);

    /// <summary>Called at the start of a jailed player's turn to decide how to try to get out.</summary>
    void HandleJailTurn(Player player, Game game);

    /// <summary>Called after a purchase decision and at the start of a turn — a chance to
    /// proactively build or sell upgrades on owned properties.</summary>
    void HandleUpgrades(Player player, Game game);
}