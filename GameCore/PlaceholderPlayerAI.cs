namespace SWTIWOOCMTG;

/// <summary>
/// Minimal placeholder CPU logic — just enough for a full game loop to complete
/// without hanging on a missing human decision. Swap out for real heuristics later.
/// </summary>
public class PlaceholderPlayerAI : IPlayerAI
{
public bool WantsToBuyProperty(Player player, BoardSpace space, Game game)
{
    if (space.Group != PropertyGroup.None)
    {
        var groupSpaces = game.Board.Where(s => s.Group == space.Group && s.IsOwnable).ToList();
        int ownedByMe = groupSpaces.Count(s => s.Owner == player);
        int ownedByOthers = groupSpaces.Count(s => s.Owner != null && s.Owner != player);

        // Already own part of this group and no one else has muscled in — prioritize completing it.
        if (ownedByMe > 0 && ownedByOthers == 0)
            return player.Money - space.Price >= 100; // lower cash bar, this purchase matters more
    }

    int cashCushion = 200;
    return player.Money - space.Price >= cashCushion;
}

    // Rule: buy if cash is at or below starting money (1500), otherwise pass.
    // Rule: buy if flush (cash > 1500), OR if buying this completes ownership of the
// rest of its color group — a monopoly's worth more than the cash saved skipping it.
public bool WantsToBuySpecialPurchase(Player player, BoardSpace space, int price, Game game)
{
    bool ownsRestOfGroup = space.Group != PropertyGroup.None &&
        game.Board.Where(s => s.Group == space.Group && s != space)
                  .All(s => s.Owner == player);

    if (ownsRestOfGroup)
        return true;

    return player.Money > 1500;
}

    public void HandleDebt(Player player, Game game)
{
    int amountOwed = game.CurrentDebt?.Amount ?? 0;
    if (amountOwed <= 0) return;

    // 1. Sell upgrades first — half-value refund is the cheapest way to raise cash,
    //    sell off the least valuable (lowest-level) upgrades last, highest first,
    //    since selling one level off five expensive maxed properties raises more
    //    than gutting one property down to zero.
    var upgraded = player.Properties
        .Where(p => p.Type == SpaceType.Property && p.Level > 0)
        .OrderByDescending(p => p.Level)
        .ToList();

    foreach (var prop in upgraded)
    {
        if (player.Money >= amountOwed) return;
        game.TryDowngradeProperty(player, prop, out _);
    }

    // Re-check: selling upgrades might have been enough.
    if (player.Money >= amountOwed) return;

    // 2. Mortgage remaining unmortgaged properties, cheapest/least useful first —
    //    prioritize mortgaging single (non-monopoly) properties before breaking up
    //    a completed color group, since a monopoly is worth more intact.
    var mortgageable = player.Properties
        .Where(p => p.IsOwnable && !p.IsMortgaged && p.Level == 0)
        .OrderBy(p => IsPartOfMonopoly(p, player, game) ? 1 : 0) // non-monopoly properties first
        .ThenBy(p => p.MortgageValue)
        .ToList();

    foreach (var prop in mortgageable)
    {
        if (player.Money >= amountOwed) return;
        game.TryMortgageProperty(player, prop, out _);
    }

    // If still short after selling everything sellable, TryResolveDebt/DeclareBankruptcy
    // in the turn loop will handle it — nothing more this AI can do.
    }

    private bool IsPartOfMonopoly(BoardSpace space, Player player, Game game)
    {
        if (space.Group == PropertyGroup.None) return false;
        return game.Board.Where(s => s.Group == space.Group && s.IsOwnable).All(s => s.Owner == player);
    }

    // Rule: get out of Gen Pop by any means necessary if it can afford to; otherwise sit tight.
    public void HandleJailTurn(Player player, Game game)
    {
        if (player.Prison == PrisonStatus.Free)
            return;

        int bailCost = player.Prison == PrisonStatus.GenPop ? 50 : 25;

        if (player.Money >= bailCost)
        {
            // Cheapest guaranteed way out: a free GOOJF card beats spending cash on bail.
            if (player.GoojfCards.Count > 0)
                game.TryGoojfCard(player, out _);
            else
                game.TryPayBail(player, out _);
        }
        else if (player.Money < 200)
        {
            // Can't afford bail — sit tight. If stuck in Gen Pop specifically, spend a
            // free Lawyer Token to at least drop to Minimum rather than do nothing.
            if (player.Prison == PrisonStatus.GenPop && player.LawyerTokens > 0)
                game.TryLawyerToken(player, out _);

            // Otherwise: wait it out — appeal-on-doubles (rolled elsewhere in the turn)
            // or the 3-turn cap eventually releases them.
        }
        // Note: bailCost tops out at 50, so "can't afford bail" is always also "< 200"
        // today — the two conditions can't diverge until bail costs change.
    }

    public void HandleUpgrades(Player player, Game game)
{
    int cashCushion = 200;

    var ownedGroups = player.Properties
        .Where(p => p.Type == SpaceType.Property && p.Group != PropertyGroup.None)
        .Select(p => p.Group)
        .Distinct()
        .Where(g => game.Board.Where(s => s.Group == g && s.IsOwnable).All(s => s.Owner == player));

    foreach (var group in ownedGroups)
    {
        // Build evenly: always upgrade the group's currently-lowest-level property first.
        var groupProps = player.Properties
            .Where(p => p.Group == group && p.Type == SpaceType.Property && !p.IsMortgaged)
            .OrderBy(p => p.Level)
            .ToList();

        foreach (var prop in groupProps)
        {
            if (prop.Level >= 5) continue;
            if (player.Money - prop.UpgradeCost < cashCushion) return; // out of safe cash, stop entirely

            bool isMaxUpgrade = prop.Level == 4;
            if (isMaxUpgrade && game.MaxesAvailable <= 0) continue;
            if (!isMaxUpgrade && game.LevelsAvailable <= 0) continue;

            game.TryUpgradeProperty(player, prop, out _);
            return; // one upgrade per call keeps pacing sane; loop will call again next turn
        }
    }
}


}