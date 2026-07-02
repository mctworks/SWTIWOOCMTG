using System;
using System.Collections.Generic;
using System.Linq;

/* *** SWTIWOOCMTG (or "Steven Wright Thinks It's Wrong Only One Company Makes This Game") ******** *\ 
C# board game heavily inspired by Monopoly (specifically Parker Brothers' rules, plus "House Rules" 
unauthorized by both Parker Brothers and Hasbro.) Conceptual prototype by Michael C. Thompson (MCT630). 
This work is not affiliated with, authorized, or endorsed by Hasbro, Inc. or any of its affiliates. */

#region Enums

enum SpaceType
{
    Payday,  // PAYDAY ("Go" in the other game) — ₿200 on pass/land
    Property,
    Rail,
    Utility,
    Prison,         // Just Visiting — no effect
    Guilty,         // Go directly to Prison
    Lotto,          // Jackpot — takes the pool
    IrsAudit,       // "Income Tax" in the other game
    Socialism,      // "Luxury Tax" in the other game
    RiskCard,       // "Chance" in the other game
    TacticsCard     // Underhanded Tactics card ("Community Chest" in the other game)
}

enum PropertyGroup
{
    None,
    Magenta, LimeGreen, White, Gold,
    MahoganyBrown, Red, JadeGreen, NeonBlue,
    Rail, Utility
}

enum Neighborhood
{
    None, Downtown, Westside, NorthernDistrict, EastVines
}

enum PrisonStatus { Free, MinimumSecurity, GenPop }

#endregion

// ── Entry Point ─────────────────────────────────────────────────────────────────

class Program
{
    static void Main()
    {
        var game = new Game();
        game.Setup();
        game.Run();
    }
}

// ── Models ──────────────────────────────────────────────────────────────────────

class BoardSpace
{
    public string        Name                { get; set; } = "";
    public SpaceType     Type                { get; set; }
    public PropertyGroup Group               { get; set; } = PropertyGroup.None;
    public Neighborhood  Neighborhood        { get; set; } = Neighborhood.None; // WIP: Gentrefication mechanics
    public int           Price               { get; set; }
    public int           UpgradeCost         { get; set; }
    // Rent[0] = base unimproved, Rent[1..5] = Lv.1–5  (railroads: [0]=1 owned … [3]=4 owned)
    public int[]         Rent                { get; set; } = Array.Empty<int>();
    public Player?       Owner               { get; set; }
    public int           Level               { get; set; } = 0;
    public bool          CityCollectsNextRent { get; set; } = false; // triggered by "tribute" cards

    public bool IsOwnable => Type is SpaceType.Property or SpaceType.Rail or SpaceType.Utility;
}

class Player
{
    public string          Name                  { get; set; }
    public int             Money                 { get; set; }
    public int             Position              { get; set; } = 0;
    public int             HP                    { get; set; } = 6;
    public PrisonStatus    Prison                { get; set; } = PrisonStatus.Free;
    public int             TurnsInPrison         { get; set; } = 0;
    public int             LawyerTokens          { get; set; } = 0;
    public List<Card>      GoojfCards            { get; set; } = new(); // Get Out of Jail Free
    public List<BoardSpace> Properties           { get; set; } = new();
    public HashSet<int>    DoubledRentPositions  { get; set; } = new(); // board indices where next rent is 2x
    public bool            IsEliminated          { get; set; } = false;

    public Player(string name, int startingMoney = 1500)
    {
        Name  = name;
        Money = startingMoney;
    }

    public void CollectMoney(int amount) => Money += amount;

    // Returns true if paid, false if bankrupt. Sets deficit to the shortfall.
    public bool PayMoney(int amount, out int deficit)
    {
        deficit = 0;
        if (Money >= amount) { Money -= amount; return true; }
        deficit = amount - Money;
        return false;
    }

    public void GainHP(int n = 1) => HP = Math.Min(6, HP + n);
    public void LoseHP(int n = 1) => HP = Math.Max(0, HP - n);
}

class HouseRules // WIP: Placeholder for House Rule settings. Currently only "default" rules are implemented.
{
    public bool Jackpot = true; // Whether the Lotto space and related mechanics are in play. If false, LOTTERY space becomes PUBLIC PARKING, and becomes a free resting spot.
    public bool PrisonGenPop = true; // Whether the Prison space has two levels (Minimum Security and Gen Pop) with different effects, or is a single level with a flat penalty. If false, all prison sentences are treated as Minimum Security.
    public bool PropertyColorGroups = false; // Whether properties are grouped by color with increasing rent and upgrade costs, or are all independent. If false, all properties are treated as the same group with flat rent and upgrade costs.
    public bool Gentrification = false; // WIP mechanic: If all properties in a neighborhood are owned by the same player, all property rent is increased by 50% while utility costs are doubled. If false, neighborhood is just flavor text with no gameplay effect.
}

class Card
{
    public string               Text    { get; set; }
    public bool                 IsGoojf { get; set; }
    public Action<Player, Game>? Effect { get; set; }

    public Card(string text, Action<Player, Game>? effect = null, bool isGoojf = false)
    {
        Text    = text;
        Effect  = effect;
        IsGoojf = isGoojf;
    }
}

class Deck
{
    private readonly List<Card> _cards;
    private int _index = 0;

    public Deck(IEnumerable<Card> cards, Random rng)
    {
        _cards = cards.OrderBy(_ => rng.Next()).ToList();
    }

    public Card Draw()
    {
        var card = _cards[_index];
        _index = (_index + 1) % _cards.Count;
        return card;
    }

    // Called when a GOOJF card is used — reinsert at back of deck
    public void ReturnCard(Card card)
    {
        _cards.Remove(card);
        _cards.Add(card);
    }
}

// ── Game ────────────────────────────────────────────────────────────────────────

class Game
{
    public  List<Player>  Players     { get; } = new();
    public  BoardSpace[]  Board       { get; } = new BoardSpace[40];
    public  Deck          RiskDeck    { get; private set; } = null!;
    public  Deck          TacticsDeck { get; private set; } = null!;
    public  int           Jackpot     { get; private set; } = 500;
    private int           _turnIndex  = 0;
    private readonly Random _rng      = new();

    // ── Setup ──────────────────────────────────────────────────────────────────

    public void Setup()
    {
        Console.Clear();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║    hey, kids, let's play some Monop... I mean SWTIWOOCMTG     ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝\n");

        InitBoard();
        InitDecks();
        SetupPlayers();
    }

    private void SetupPlayers()
    {
        int humanCount = 0;
        while (humanCount < 1 || humanCount > 8)
        {
            Console.Write("Number of human players (1–8): ");
            int.TryParse(Console.ReadLine(), out humanCount);
        }

        // TODO: CPU player support
        for (int i = 0; i < humanCount; i++)
        {
            string name = "";
            while (string.IsNullOrWhiteSpace(name))
            {
                Console.Write($"Player {i + 1} name: ");
                name = Console.ReadLine() ?? "";
            }
            Players.Add(new Player(name.Trim()));
        }

        Console.WriteLine("\nPlayers registered:");
        foreach (var p in Players)
            Console.WriteLine($"  {p.Name} — ₿{p.Money} | {p.HP} HP");
        Console.WriteLine($"\nLotto Pool starts at: ₿{Jackpot}\n");
        Console.WriteLine("Press Enter to begin.");
        Console.ReadLine();
    }

    // ── Main Loop ──────────────────────────────────────────────────────────────

    public void Run()
    {
        while (Players.Count(p => !p.IsEliminated) > 1)
        {
            var player = Players[_turnIndex];
            if (!player.IsEliminated) TakeTurn(player);
            _turnIndex = (_turnIndex + 1) % Players.Count;
        }

        var winner = Players.FirstOrDefault(p => !p.IsEliminated);
        Console.WriteLine(winner != null
            ? $"\n{winner.Name} wins with ₿{winner.Money}. But really, no one wins when one entity dominates all sides of the market. Soon, everything will collapse under a bubble. Enjoy the top while it lasts, {winner.Name}..."
            : "\nEveryone's eliminated. Nobody wins. That's Capitalism for you. Play stupid games, get stupid prizes.");
    }

    // ── Turn ───────────────────────────────────────────────────────────────────

    private void TakeTurn(Player player)
    {
        Console.WriteLine($"\n{"─",0}{"",50}");
        Console.WriteLine($"▶ {player.Name}  |  ₿{player.Money}  |  {player.HP}/6 HP  |  [{player.Position}] {Board[player.Position].Name}");
        if (player.LawyerTokens > 0) Console.WriteLine($"  Lawyer Tokens: {player.LawyerTokens}");
        if (player.Prison != PrisonStatus.Free)
        {
            Console.WriteLine($"  Prison Status: {player.Prison} (Turn {player.TurnsInPrison + 1}/3)");
            HandlePrisonTurn(player);
            return;
        }

        int doublesCount = 0;

        while (true)
        {
            Console.Write("\nPress Enter to roll...");
            Console.ReadLine();

            var (d1, d2) = RollDice();
            bool isDoubles = d1 == d2;
            Console.WriteLine($"  Rolled: [{d1}] [{d2}] = {d1 + d2}{(isDoubles ? " — Doubles!" : "")}");

            if (isDoubles)
            {
                doublesCount++;
                if (doublesCount == 3)
                {
                    // Triple doubles: GUILTY. Charge based on the value of the third pair.
                    bool felony = d1 >= 4;
                    Console.WriteLine($"  Triple doubles! {(felony ? "FELONY (double {d1}s)" : $"MISDEMEANOR (double {d1}s)")}");
                    SendToPrison(player, felony ? PrisonStatus.GenPop : PrisonStatus.MinimumSecurity, noPayday: true);
                    return;
                }
            }

            MovePlayer(player, d1 + d2);
            if (player.IsEliminated) return;

            LandOnSpace(player, d1, d2);
            if (player.IsEliminated) return;

            // Keep rolling on doubles; stop otherwise or if sent to prison
            if (!isDoubles || player.Prison != PrisonStatus.Free) break;
            Console.WriteLine("  Doubles — roll again.");
        }
    }

    private void HandlePrisonTurn(Player player)
    {
        // Gen Pop HP drain at the start of each turn inside
        if (player.Prison == PrisonStatus.GenPop)
        {
            player.LoseHP();
            Console.WriteLine($"  Gen Pop HP drain → {player.HP} HP");
            if (player.HP <= 0) { Eliminate(player); return; }
        }

        // Option: GOOJF card
        if (player.GoojfCards.Count > 0)
        {
            Console.Write($"  Use a Get Out of Jail Free card? ({player.GoojfCards.Count} held) (y/n): ");
            if (YesNo())
            {
                var card = player.GoojfCards[0];
                player.GoojfCards.RemoveAt(0);
                // TODO: track deck source to return card correctly; for now just discard
                player.Prison      = PrisonStatus.Free;
                player.TurnsInPrison = 0;
                Console.WriteLine("  Released on the GOOJF card.");
                TakeTurn(player);
                return;
            }
        }

        // Option: Lawyer Token to downgrade GenPop → MinSec
        if (player.Prison == PrisonStatus.GenPop && player.LawyerTokens > 0)
        {
            Console.Write($"  Spend a Lawyer Token to move to Minimum Security? ({player.LawyerTokens} held) (y/n): ");
            if (YesNo())
            {
                player.LawyerTokens--;
                player.Prison = PrisonStatus.MinimumSecurity;
                Console.WriteLine($"  Downgraded to Minimum Security. {player.LawyerTokens} token(s) remaining.");
            }
        }

        // Option: Pay bail ($50) after turn 1
        if (player.TurnsInPrison >= 1)
        {
            Console.Write("  Pay ₿50 bail to get out now? (y/n): ");
            if (YesNo())
            {
                if (player.PayMoney(50, out _))
                {
                    AddToJackpot(50);
                    player.Prison        = PrisonStatus.Free;
                    player.TurnsInPrison = 0;
                    Console.WriteLine("  Bail posted. Take your turn.");
                    TakeTurn(player);
                }
                else Console.WriteLine("  Can't afford bail.");
                return;
            }
        }

        // Roll for doubles to escape
        Console.Write("\nPress Enter to roll to appeal your case...");
        Console.ReadLine();

        var (d1, d2) = RollDice();
        Console.WriteLine($"  Rolled: [{d1}] [{d2}]");
        player.TurnsInPrison++;

        if (d1 == d2)
        {
            Console.WriteLine("  Doubles — your case was successfully appealed for good behavior!");
            player.Prison        = PrisonStatus.Free;
            player.TurnsInPrison = 0;
            MovePlayer(player, d1 + d2);
            if (!player.IsEliminated) LandOnSpace(player, d1, d2);
        }
        else if (player.TurnsInPrison >= 3)
        {
            // Three turns up: force bail
            Console.WriteLine("  Three turns served. Forced bail: ₿50."); //TO-DO: There shouldn't be forced bail if the player served their 3 turn sentence. Fix this logic
            if (!player.PayMoney(50, out _))
                Console.WriteLine($"  {player.Name} can't cover bail — TODO: bankruptcy liquidation.");
            else
                AddToJackpot(50);

            player.Prison        = PrisonStatus.Free;
            player.TurnsInPrison = 0;
            MovePlayer(player, d1 + d2);
            if (!player.IsEliminated) LandOnSpace(player, d1, d2);
        }
        else
        {
            Console.WriteLine($"  Still in Prison. Turn {player.TurnsInPrison}/3.");
        }
    }

    // ── Movement ───────────────────────────────────────────────────────────────

    // Normal step-based movement (checks Payday crossing)
    public void MovePlayer(Player player, int steps)
    {
        int oldPos = player.Position;
        int newPos = (oldPos + steps) % 40;

        if (newPos < oldPos || (oldPos == 0 && steps > 0 && newPos == 0))
        {
            // Crossed or landed on position 0
            player.CollectMoney(200);
            player.GainHP();
            Console.WriteLine($"  ✦ Passed PAYDAY! +₿200, +1 HP  →  ₿{player.Money} | {player.HP}/6 HP");
        }

        player.Position = newPos;
        Console.WriteLine($"  → [{newPos}] {Board[newPos].Name}");
    }

    // Transportation (card effects). collectPayday: true if should award ₿200 for wrapping past 0.
    public void MovePlayerTo(Player player, int target, bool collectPayday = true)
    {
        int oldPos = player.Position;
        player.Position = target;

        if (collectPayday && target != oldPos && target < oldPos)
        {
            player.CollectMoney(200);
            player.GainHP();
            Console.WriteLine($"  ✦ Passed PAYDAY! +₿200, +1 HP  →  ₿{player.Money} | {player.HP}/6 HP");
        }

        Console.WriteLine($"  → [{target}] {Board[target].Name}");
    }

    // ── Space Landing ──────────────────────────────────────────────────────────

    // Public wrapper so card lambdas can trigger landing effects
    public void LandOnSpace(Player player, int d1, int d2)
    {
        if (player.IsEliminated) return;
        var space = Board[player.Position];

        switch (space.Type)
        {
            case SpaceType.Payday:
                // ₿200 already handled in MovePlayer; landing directly on it is treated the same.
                Console.WriteLine("  Landed on PAYDAY! Collect ₿200, gain 1 HP, and catch your breath!");
                break;

            case SpaceType.Property:
                HandlePropertyLanding(player, space);
                break;

            case SpaceType.Rail:
                HandleRailroadLanding(player, space);
                break;

            case SpaceType.Utility:
                HandleUtilityLanding(player, space, d1 + d2);
                break;

            case SpaceType.Prison:
                Console.WriteLine("  Just Visiting. Nothing happens.");
                break;

            case SpaceType.Guilty:
                HandleGuilty(player);
                break;

            case SpaceType.Lotto:
                HandleLotto(player);
                break;

            case SpaceType.IrsAudit:
                HandleIrsAudit(player);
                break;

            case SpaceType.Socialism:
                Console.WriteLine($"  SOCIALISM! Pay ₿100 luxury tax.");
                if (player.PayMoney(100, out _)) AddToJackpot(100);
                else HandleBankruptcy(player, null, 100);
                break;

            case SpaceType.RiskCard:
                Console.WriteLine("  Drew a RISK card.");
                var risk = RiskDeck.Draw();
                Console.WriteLine($"  \"{risk.Text}\"");
                if (risk.IsGoojf) { player.GoojfCards.Add(risk); Console.WriteLine("  (Kept — Get Out of Jail Free)"); }
                else risk.Effect?.Invoke(player, this);
                break;

            case SpaceType.TacticsCard:
                Console.WriteLine("  Drew an Underhanded Tactics card.");
                var tactics = TacticsDeck.Draw();
                Console.WriteLine($"  \"{tactics.Text}\"");
                if (tactics.IsGoojf) { player.GoojfCards.Add(tactics); Console.WriteLine("  (Kept — Get Out of Jail Free)"); }
                else tactics.Effect?.Invoke(player, this);
                break;
        }
    }

    // ── Property Handlers ──────────────────────────────────────────────────────

    private void HandlePropertyLanding(Player player, BoardSpace space)
    {
        if (space.Owner == null)
        {
            Console.Write($"  {space.Name} — unowned (₿{space.Price}). Buy? (y/n): ");
            if (YesNo()) PurchaseSpace(player, space);
        }
        else if (space.Owner != player)
        {
            int rent = space.Rent[space.Level];

            // Tribute card: city collects instead of owner (one-time)
            if (space.CityCollectsNextRent)
            {
                Console.WriteLine($"  Tribute to the City! ₿{rent} → Lotto pool.");
                AddToJackpot(rent);
                space.CityCollectsNextRent = false;
                return;
            }

            // Doubled rent card
            if (player.DoubledRentPositions.Contains(player.Position))
            {
                rent *= 2;
                player.DoubledRentPositions.Remove(player.Position);
                Console.WriteLine($"  Doubled rent triggered!");
            }

            Console.WriteLine($"  {space.Name} — owned by {space.Owner.Name}. Rent: ₿{rent}");
            if (player.PayMoney(rent, out int deficit))
                space.Owner.CollectMoney(rent);
            else
                HandleBankruptcy(player, space.Owner, rent);
        }
        else
        {
            Console.WriteLine($"  You own {space.Name} (Lv.{space.Level}).");
            OfferUpgrade(player, space);
        }
    }

    private void HandleRailroadLanding(Player player, BoardSpace space)
    {
        if (space.Owner == null)
        {
            Console.Write($"  {space.Name} — unowned (₿{space.Price}). Buy? (y/n): ");
            if (YesNo()) PurchaseSpace(player, space);
        }
        else if (space.Owner != player)
        {
            int owned = space.Owner.Properties.Count(p => p.Group == PropertyGroup.Rail);
            // Railroad Rent: [25, 50, 100, 200] for 1–4 owned
            int rent = owned switch { 1 => 25, 2 => 50, 3 => 100, 4 => 200, _ => 25 };
            Console.WriteLine($"  {space.Name} — owned by {space.Owner.Name}. Rent: ₿{rent} ({owned} railroad(s))");
            if (player.PayMoney(rent, out _))
                space.Owner.CollectMoney(rent);
            else
                HandleBankruptcy(player, space.Owner, rent);
        }
        else Console.WriteLine($"  You own {space.Name}.");
    }

    private void HandleUtilityLanding(Player player, BoardSpace space, int diceTotal)
    {
        bool isHospital    = space.Name == "Saint Huck's Hospital";
        bool isCourthouse  = space.Name == "Buford T. Justice Memorial Courthouse";

        // Special effects on landing regardless of ownership
        if (isHospital)
        {
            player.GainHP();
            Console.WriteLine($"  Saint Huck's Hospital: {player.Name} recovers 1 HP → {player.HP} HP");
        }
        if (isCourthouse)
        {
            player.LawyerTokens++;
            Console.WriteLine($"  Buford T. Justice Courthouse: {player.Name} earns a Lawyer Token ({player.LawyerTokens} total)");
        }

        if (space.Owner == null)
        {
            Console.Write($"  {space.Name} — unowned (₿{space.Price}). Buy? (y/n): ");
            if (YesNo()) PurchaseSpace(player, space);
        }
        else if (space.Owner != player)
        {
            int owned  = space.Owner.Properties.Count(p => p.Group == PropertyGroup.Utility);
            int mult   = owned == 2 ? 10 : 4;
            int rent   = diceTotal * mult;
            Console.WriteLine($"  {space.Name} — owned by {space.Owner.Name}. Rent: {diceTotal} × {mult} = ₿{rent}");
            if (player.PayMoney(rent, out _))
                space.Owner.CollectMoney(rent);
            else
                HandleBankruptcy(player, space.Owner, rent);
        }
        else Console.WriteLine($"  You own {space.Name}.");
    }

    private void PurchaseSpace(Player player, BoardSpace space)
    {
        if (player.PayMoney(space.Price, out _))
        {
            space.Owner = player;
            player.Properties.Add(space);
            Console.WriteLine($"  Purchased {space.Name}.");
        }
        else Console.WriteLine("  Not enough money.");
    }

    private void OfferUpgrade(Player player, BoardSpace space)
    {
        if (space.Level >= 5) { Console.WriteLine("  Already at max level (Lv.5)."); return; }
        Console.Write($"  Upgrade to Lv.{space.Level + 1} for ₿{space.UpgradeCost}? New rent: ₿{space.Rent[space.Level + 1]} (y/n): ");
        if (YesNo())
        {
            if (player.PayMoney(space.UpgradeCost, out _))
            {
                space.Level++;
                Console.WriteLine($"  {space.Name} → Lv.{space.Level}. Rent now ₿{space.Rent[space.Level]}");
            }
            else Console.WriteLine("  Not enough money.");
        }
    }

    // ── Special Spaces ─────────────────────────────────────────────────────────

    private void HandleGuilty(Player player)
    {
        int roll = _rng.Next(1, 7);
        Console.WriteLine($"  GUILTY! Charge roll: {roll}");
        bool felony = roll >= 4;
        Console.WriteLine(felony ? "  FELONY!" : "  MISDEMEANOR.");
        SendToPrison(player, felony ? PrisonStatus.GenPop : PrisonStatus.MinimumSecurity, noPayday: true);
    }

    private void HandleLotto(Player player)
    {
        Console.WriteLine($"  LOTTO! {player.Name} wins ₿{Jackpot}!");
        player.CollectMoney(Jackpot);
        Jackpot = 500; // reset to minimum
    }

    private void HandleIrsAudit(Player player)
    {
        // Standard set by The Other Game: lesser of ₿200 or 10% of total cash
        int tenPct = (int)(player.Money * 0.1);
        int tax    = Math.Min(200, tenPct);
        Console.WriteLine($"  IRS AUDIT! Pay ₿{tax} (lesser of ₿200 or 10% of ₿{player.Money}).");
        if (player.PayMoney(tax, out _)) AddToJackpot(tax);
        else HandleBankruptcy(player, null, tax);
    }

    // ── Prison ─────────────────────────────────────────────────────────────────

    public void SendToPrison(Player player, PrisonStatus level, bool noPayday = true)
    {
        player.Prison        = level;
        player.TurnsInPrison = 0;
        player.Position      = 10; // Prison space
        Console.WriteLine($"  {player.Name} sent to Prison — {level}.");

        // Auto-prompt lawyer token on felony
        if (level == PrisonStatus.GenPop && player.LawyerTokens > 0)
        {
            Console.Write($"  Spend a Lawyer Token to downgrade to Minimum Security? ({player.LawyerTokens} held) (y/n): ");
            if (YesNo())
            {
                player.LawyerTokens--;
                player.Prison = PrisonStatus.MinimumSecurity;
                Console.WriteLine($"  Downgraded to Minimum Security. {player.LawyerTokens} token(s) remaining.");
            }
        }
    }

    // ── Economy ────────────────────────────────────────────────────────────────

    public void AddToJackpot(int amount)
    {
        Jackpot += amount;
        Console.WriteLine($"  Lotto pool: +₿{amount} → ₿{Jackpot}");
    }

    // Collector pulls [amount] from every other active player
    public void CollectFromAll(Player collector, int amount, bool losersLoseHP = false)
    {
        foreach (var p in Players.Where(p => !p.IsEliminated && p != collector))
        {
            if (p.PayMoney(amount, out _))
            {
                collector.CollectMoney(amount);
                if (losersLoseHP)
                {
                    p.LoseHP();
                    Console.WriteLine($"  {p.Name} paid ₿{amount} and lost 1 HP → {p.HP} HP.");
                    if (p.HP <= 0) Eliminate(p);
                }
                else
                {
                    Console.WriteLine($"  {p.Name} paid ₿{amount} to {collector.Name}.");
                }
            }
            else HandleBankruptcy(p, collector, amount);
        }
    }

    // Payer sends [amount] to every other active player
    public void PayAllPlayers(Player payer, int amount)
    {
        foreach (var p in Players.Where(p => !p.IsEliminated && p != payer))
        {
            if (payer.PayMoney(amount, out _))
            {
                p.CollectMoney(amount);
                Console.WriteLine($"  {payer.Name} paid ₿{amount} to {p.Name}.");
            }
            else
            {
                HandleBankruptcy(payer, null, amount);
                return; // payer is gone
            }
        }
    }

    // ── Bankruptcy & Elimination ───────────────────────────────────────────────

    public void HandleBankruptcy(Player player, Player? creditor, int amount)
    {
        Console.WriteLine($"\n  {player.Name} can't pay ₿{amount}. BANKRUPT.");

        // Liquidate properties back to bank
        foreach (var prop in player.Properties)
        {
            prop.Owner = null;
            prop.Level = 0;
        }
        player.Properties.Clear();

        if (creditor != null)
            creditor.CollectMoney(player.Money);
        else
            AddToJackpot(player.Money);

        player.Money = 0;
        Eliminate(player);
    }

    public void Eliminate(Player player)
    {
        if (player.IsEliminated) return;
        player.IsEliminated = true;
        if (player.Money > 0) { AddToJackpot(player.Money); player.Money = 0; }
        Console.WriteLine($"\n  The harsh world of Capitalism has claimed the life of {player.Name}. They have been eliminated. All properties returned to the bank, and any remaining money added to the Lotto pool. Tots and Pears go out to {player.Name}'s friends and family.");
    }

    // ── Dice ───────────────────────────────────────────────────────────────────

    public (int d1, int d2) RollDice() => (_rng.Next(1, 7), _rng.Next(1, 7));

    // ── Spatial Helpers ────────────────────────────────────────────────────────

    // Returns the index of the nearest space of [type] moving clockwise from currentPos
    public int NearestSpaceOfType(int currentPos, SpaceType type)
    {
        for (int i = 1; i <= 40; i++)
        {
            int idx = (currentPos + i) % 40;
            if (Board[idx].Type == type) return idx;
        }
        return currentPos;
    }

    // Returns the nearest board index from a set of named spaces
    public int NearestSpaceByName(int currentPos, params string[] names)
    {
        for (int i = 1; i <= 40; i++)
        {
            int idx = (currentPos + i) % 40;
            if (names.Contains(Board[idx].Name)) return idx;
        }
        return currentPos;
    }

    // ── Utility ────────────────────────────────────────────────────────────────

    private static bool YesNo()
    {
        string? input = Console.ReadLine()?.Trim().ToLower();
        return input == "y" || input == "yes";
    }

    // ── Board Initialization ───────────────────────────────────────────────────

    private void InitBoard()
    {
        // Rent arrays: [base, Lv1, Lv2, Lv3, Lv4, Lv5]  (same values as 1980s Parker Bros edition)
        // Railroads: not upgraded, rent is determined by owner's railroad count at runtime
        var spaces = new BoardSpace[]
        {
            /* 00 */ new() { Name = "₿ PAYDAY! ₿",                       
                             Type = SpaceType.Payday,
                             Neighborhood = Neighborhood.None },
            /* 01 */ new() { Name = "Lucy's Trailer Park",                   
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.Magenta,
                             Neighborhood = Neighborhood.Downtown,    
                             Price = 60,  
                             UpgradeCost = 50,  
                             Rent = new[] { 2,   10,  30,  90,  160,  250  } },
            /* 02 */ new() { Name = "Underhanded Tactics Card",              
                             Type = SpaceType.TacticsCard,
                             Neighborhood = Neighborhood.None },
            /* 03 */ new() { Name = "Cummings Avenue",                       
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.Magenta,  
                             Neighborhood = Neighborhood.Downtown,  
                             Price = 60,  
                             UpgradeCost = 50,  
                             Rent = new[] { 4,   20,  60,  180, 320,  450  } },
            /* 04 */ new() { Name = "IRS AUDIT",                             
                             Type = SpaceType.IrsAudit,
                             Neighborhood = Neighborhood.None },
            /* 05 */ new() { Name = "Metro Rail Blue Line",                  
                             Type = SpaceType.Rail,  
                             Group = PropertyGroup.Rail,
                             Neighborhood = Neighborhood.Downtown, 
                             Price = 200 },
            /* 06 */ new() { Name = "The Hotel Mary Chang",                     
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.LimeGreen,
                             Neighborhood = Neighborhood.Downtown,
                             Price = 100, 
                             UpgradeCost = 50,  
                             Rent = new[] { 6,   30,  90,  270, 400,  550  } },
            /* 07 */ new() { Name = "RISK Card",                             
                             Type = SpaceType.RiskCard,
                             Neighborhood = Neighborhood.None },
            /* 08 */ new() { Name = "Toad Suck Park",                        
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.LimeGreen,
                             Neighborhood = Neighborhood.Downtown,
                             Price = 100, 
                             UpgradeCost = 50,  
                             Rent = new[] { 6,   30,  90,  270, 400,  550  } },
            /* 09 */ new() { Name = "Lebowski Boulevard",                    
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.LimeGreen,
                             Neighborhood = Neighborhood.Downtown,
                             Price = 120, 
                             UpgradeCost = 50,  
                             Rent = new[] { 8,   40,  100, 300, 450,  600  } },
            /* 10 */ new() { Name = "PRISON!",                               
                             Type = SpaceType.Prison,
                             Neighborhood = Neighborhood.None },
            /* 11 */ new() { Name = "Soucross Place",                           
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.White,
                             Neighborhood = Neighborhood.Westside,     
                             Price = 140, 
                             UpgradeCost = 100, 
                             Rent = new[] { 10,  50,  150, 450, 625,  750  } },
            /* 12 */ new() { Name = "Buford T. Justice Memorial Courthouse", 
                             Type = SpaceType.Utility,   
                             Group = PropertyGroup.Utility, 
                             Neighborhood = Neighborhood.Westside, 
                             Price = 150 },
            /* 13 */ new() { Name = "The Swan",                              
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.White,
                             Neighborhood = Neighborhood.Westside,     
                             Price = 140, 
                             UpgradeCost = 100, 
                             Rent = new[] { 10,  50,  150, 450, 625,  750  } },
            /* 14 */ new() { Name = "White Avenue",                          
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.White,
                             Neighborhood = Neighborhood.Westside,     
                             Price = 160, 
                             UpgradeCost = 100, 
                             Rent = new[] { 12,  60,  180, 500, 700,  900  } },
            /* 15 */ new() { Name = "Metro Rail White Line",                 
                             Type = SpaceType.Rail,  
                             Group = PropertyGroup.Rail,
                             Neighborhood = Neighborhood.Westside,
                             Price = 200 },
            /* 16 */ new() { Name = "Dick Valentine International Airport",  
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.Gold,
                             Neighborhood = Neighborhood.Westside,   
                             Price = 180, 
                             UpgradeCost = 100, 
                             Rent = new[] { 14,  70,  200, 550, 750,  950  } },
            /* 17 */ new() { Name = "Underhanded Tactics Card",              // ← this was missing
                             Type = SpaceType.TacticsCard,
                             Neighborhood = Neighborhood.None },      
            /* 18 */ new() { Name = "Racist Ass Rock",                       
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.Gold,
                             Neighborhood = Neighborhood.Westside,   
                             Price = 180, 
                             UpgradeCost = 100, 
                             Rent = new[] { 14,  70,  200, 550, 750,  950  } },
            /* 19 */ new() { Name = "University Square",                     
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.Gold,
                             Neighborhood = Neighborhood.Westside,
                             Price = 200, 
                             UpgradeCost = 100, 
                             Rent = new[] { 16,  80,  220, 600, 800,  1000 } },
            /* 20 */ new() { Name = "LOTTO!",                                
                             Type = SpaceType.Lotto,
                             Neighborhood = Neighborhood.None },
            /* 21 */ new() { Name = "Truxton Trickham Boulevard",            
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.MahoganyBrown,
                             Neighborhood = Neighborhood.NorthernDistrict,      
                             Price = 220, 
                             UpgradeCost = 150, 
                             Rent = new[] { 18,  90,  250, 700, 875,  1050 } },
            /* 22 */ new() { Name = "RISK Card",                             
                             Type = SpaceType.RiskCard,
                             Neighborhood = Neighborhood.None },
            /* 23 */ new() { Name = "Red Light District",                   
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.MahoganyBrown,
                             Neighborhood = Neighborhood.NorthernDistrict,      
                             Price = 220, 
                             UpgradeCost = 150, 
                             Rent = new[] { 18,  90,  250, 700, 875,  1050 } },
            /* 24 */ new() { Name = "Five Spoons Square",                    
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.MahoganyBrown,
                             Neighborhood = Neighborhood.NorthernDistrict,      
                             Price = 240, 
                             UpgradeCost = 150, 
                             Rent = new[] { 20,  100, 300, 750, 925,  1100 } },
            /* 25 */ new() { Name = "Metro Rail Red Line",                   
                             Type = SpaceType.Rail,  
                             Group = PropertyGroup.Rail,
                             Neighborhood = Neighborhood.NorthernDistrict, 
                             Price = 200 },
            /* 26 */ new() { Name = "Lily Pippa Park",                       
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.Red,   
                             Neighborhood = Neighborhood.NorthernDistrict,      
                             Price = 260, 
                             UpgradeCost = 150, 
                             Rent = new[] { 22,  110, 330, 800, 975,  1150 } },
            /* 27 */ new() { Name = "Walter Hills",                          
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.Red,   
                             Neighborhood = Neighborhood.NorthernDistrict,      
                             Price = 260, 
                             UpgradeCost = 150, 
                             Rent = new[] { 22,  110, 330, 800, 975,  1150 } },
            /* 28 */ new() { Name = "Saint Huck's Hospital",                 
                             Type = SpaceType.Utility,   
                             Group = PropertyGroup.Utility,
                             Neighborhood = Neighborhood.NorthernDistrict,  
                             Price = 150 },
            /* 29 */ new() { Name = "Dobbs Vineyard",                        
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.Red,   
                             Neighborhood = Neighborhood.NorthernDistrict,      
                             Price = 280, 
                             UpgradeCost = 150, 
                             Rent = new[] { 24,  120, 360, 850, 1025, 1200 } },
            /* 30 */ new() { Name = "GUILTY!",                               
                             Type = SpaceType.Guilty,
                             Neighborhood = Neighborhood.None },
            /* 31 */ new() { Name = "The Iguana Nightclub",                  
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.JadeGreen,
                             Neighborhood = Neighborhood.EastVines,    
                             Price = 300, 
                             UpgradeCost = 200, 
                             Rent = new[] { 26,  130, 390, 900, 1100, 1275 } },
            /* 32 */ new() { Name = "North Sobochek Avenue",                 
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.JadeGreen,
                             Neighborhood = Neighborhood.EastVines,    
                             Price = 300, 
                             UpgradeCost = 200, 
                             Rent = new[] { 26,  130, 390, 900, 1100, 1275 } },
            /* 33 */ new() { Name = "Underhanded Tactics Card",              
                             Type = SpaceType.TacticsCard,
                             Neighborhood = Neighborhood.None },
            /* 34 */ new() { Name = "Buckingham Green",                      
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.JadeGreen,    
                             Neighborhood = Neighborhood.EastVines,    
                             Price = 320, 
                             UpgradeCost = 200, 
                             Rent = new[] { 28,  150, 450, 1000,1200, 1400 } },
            /* 35 */ new() { Name = "Metro Rail Gold Line",                  
                             Type = SpaceType.Rail,  
                             Group = PropertyGroup.Rail,
                             Neighborhood = Neighborhood.EastVines, 
                             Price = 200 },
            /* 36 */ new() { Name = "RISK Card",                             
                             Type = SpaceType.RiskCard,
                             Neighborhood = Neighborhood.None },
            /* 37 */ new() { Name = "Studio Anni Plaza",                     
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.NeonBlue, 
                             Neighborhood = Neighborhood.EastVines,
                             Price = 350, 
                             UpgradeCost = 200, 
                             Rent = new[] { 35,  175, 500, 1100,1300, 1500 } },
            /* 38 */ new() { Name = "SOCIALISM!",                            
                             Type = SpaceType.Socialism,
                             Neighborhood = Neighborhood.None },
            /* 39 */ new() { Name = "MCT630 Island",                         
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.NeonBlue, 
                             Neighborhood = Neighborhood.EastVines,
                             Price = 400, 
                             UpgradeCost = 200, 
                             Rent = new[] { 50,  200, 600, 1400,1700, 2000 } },
        };

        for (int i = 0; i < spaces.Length; i++)
            Board[i] = spaces[i];
    }

    // ── Deck Initialization ────────────────────────────────────────────────────

    private void InitDecks()
    {
        RiskDeck    = new Deck(BuildRiskCards(),    _rng);
        TacticsDeck = new Deck(BuildTacticsCards(), _rng);
    }

    private List<Card> BuildRiskCards() => new()
    {
        new Card(
            "First Class Ticket to MCT630 Island. (Look out for blinding neon lights!)",
            (p, g) => {
                g.MovePlayerTo(p, 39, collectPayday: true);
                g.LandOnSpace(p, 0, 0);
            }
        ),
        new Card(
            "Black Market Work Hacks were Super Effective — Advance to Payday, Recover 1HP and Collect ₿200",
            (p, g) => {
                g.MovePlayerTo(p, 0, collectPayday: false);
                p.CollectMoney(200);
                p.GainHP();
                Console.WriteLine($"  {p.Name} → PAYDAY! +₿200, +1 HP  →  ₿{p.Money} | {p.HP}/6 HP");
            }
        ),
        new Card(
            "Take a Bullet Train to Five Blades Square! (If you pass Payday, Recover 1HP and Collect ₿200)",
            (p, g) => {
                g.MovePlayerTo(p, 24, collectPayday: true);
                g.LandOnSpace(p, 0, 0);
            }
        ),
        new Card(
            "Take the public bus to Salem Place! (If you pass Payday, Recover 1HP and Collect ₿200)",
            (p, g) => {
                g.MovePlayerTo(p, 11, collectPayday: true);
                g.LandOnSpace(p, 0, 0);
            }
        ),
        new Card(
            "Advance to the nearest Metro Rail Station! If unowned, you may buy it. If owned, roll and pay owner 10× that roll.",
            (p, g) => {
                int target = g.NearestSpaceOfType(p.Position, SpaceType.Rail);
                g.MovePlayerTo(p, target, collectPayday: true);
                var space = g.Board[target];
                if (space.Owner != null && space.Owner != p)
                {
                    var (d1, d2) = g.RollDice();
                    int rent = (d1 + d2) * 10;
                    Console.WriteLine($"  Special rent: [{d1}]+[{d2}]={d1+d2} × 10 = ₿{rent} → {space.Owner.Name}");
                    if (p.PayMoney(rent, out _)) space.Owner.CollectMoney(rent);
                    else g.HandleBankruptcy(p, space.Owner, rent);
                }
                else g.LandOnSpace(p, 0, 0);
            }
        ),
        new Card(
            "Advance to whichever is closest: Buford T. Justice Memorial Courthouse or Saint Huck's Hospital. If owned, roll and pay owner 10× that roll.",
            (p, g) => {
                int target = g.NearestSpaceByName(p.Position,
                    "Buford T. Justice Memorial Courthouse", "Saint Huck's Hospital");
                g.MovePlayerTo(p, target, collectPayday: true);
                var space = g.Board[target];
                if (space.Owner != null && space.Owner != p)
                {
                    var (d1, d2) = g.RollDice();
                    int rent = (d1 + d2) * 10;
                    Console.WriteLine($"  Special Fees: [{d1}]+[{d2}]={d1+d2} × 10 = ₿{rent} → {space.Owner.Name}");
                    if (p.PayMoney(rent, out _)) space.Owner.CollectMoney(rent);
                    else g.HandleBankruptcy(p, space.Owner, rent);
                }
                else g.LandOnSpace(p, 0, 0);
            }
        ),
        new Card(
            "Take the Metro Rail to the Blue Line! (If you pass Payday, Recover 1HP and Collect ₿200)",
            (p, g) => {
                g.MovePlayerTo(p, 5, collectPayday: true);
                g.LandOnSpace(p, 0, 0);
            }
        ),
        new Card(
            "Insider trading tip pays off! Collect ₿50.",
            (p, g) => { p.CollectMoney(50); Console.WriteLine($"  {p.Name} collects ₿50 → ₿{p.Money}"); }
        ),
        new Card(
            "Go Back 3 Spaces!",
            (p, g) => {
                // No Payday awarded for moving backward
                p.Position = (p.Position - 3 + 40) % 40;
                Console.WriteLine($"  → [{p.Position}] {g.Board[p.Position].Name}");
                g.LandOnSpace(p, 0, 0);
            }
        ),
        new Card(
            "Uh OH! You were caught doing a FELONY! Go directly to PRISON's Gen Pop. No Payday!",
            (p, g) => g.SendToPrison(p, PrisonStatus.GenPop, noPayday: true)
        ),
        new Card(
            "Bloodsucking HOA Fees! For each property Lv.1 or higher, pay ₿25 per level.",
            (p, g) => {
                int total = p.Properties.Where(pr => pr.Level >= 1).Sum(pr => 25 * pr.Level);
                Console.WriteLine($"  HOA Fees: ₿{total}");
                if (p.PayMoney(total, out _)) g.AddToJackpot(total);
                else g.HandleBankruptcy(p, null, total);
            }
        ),
        new Card(
            "Pay the city ₿15 for permit fees.",
            (p, g) => {
                Console.WriteLine($"  {p.Name} pays ₿15 in permit fees.");
                if (p.PayMoney(15, out _)) g.AddToJackpot(15);
                else g.HandleBankruptcy(p, null, 15);
            }
        ),
        new Card(
            "Pay each player ₿50 to keep quiet about the Diddy Party you went to back in 2005.",
            (p, g) => g.PayAllPlayers(p, 50)
        ),
        new Card(
            "You shorted the market and made a profit! Collect ₿150.",
            (p, g) => { p.CollectMoney(150); Console.WriteLine($"  {p.Name} collects ₿150 → ₿{p.Money}"); }
        ),
        new Card(
            "You seem to be accruing more penalties than a Boston Bruin, but this here card says GET OUT OF JAIL FOR FREE! Keep it for whenever you need to get out of a PRISON situation that no lawyer can handle...",
            isGoojf: true
        ),
        new Card(
            "Geepers, you got caught doing a MISDEMEANOR! Go directly to PRISON's Minimum Security. No Payday!",
            (p, g) => g.SendToPrison(p, PrisonStatus.MinimumSecurity, noPayday: true)
        ),
        new Card(
            "You did a solid for a sleazy law practice. Earn a Lawyer Token!",
            (p, g) => {
                p.LawyerTokens++;
                Console.WriteLine($"  {p.Name} gains a Lawyer Token ({p.LawyerTokens} total)");
            }
        ),
        new Card(
            "The Child of Eye who watches over Buckingham Green has demanded tribute from you, specifically...",
            (p, g) => {
                var bg = g.Board[34]; // Buckingham Green index
                if (bg.Owner == p)
                {
                    // Your property: next visitor pays City instead
                    bg.CityCollectsNextRent = true;
                    Console.WriteLine($"  You own Buckingham Green — next player to land pays the City instead.");
                }
                else if (bg.Owner != null)
                {
                    // Someone else owns it: double rent next time YOU land on it
                    p.DoubledRentPositions.Add(34);
                    Console.WriteLine($"  {bg.Owner.Name} owns Buckingham Green — your next rent there is doubled.");
                }
                else
                {
                    // City owned: must buy at 2× original price
                    int doublePrice = bg.Price * 2;
                    Console.Write($"  Buckingham Green unowned. Tribute: buy for ₿{doublePrice}? (y/n): ");
                    if (YesNo())
                    {
                        if (p.PayMoney(doublePrice, out _))
                        {
                            bg.Owner = p;
                            p.Properties.Add(bg);
                            Console.WriteLine($"  Purchased Buckingham Green for ₿{doublePrice}.");
                        }
                        else Console.WriteLine("  Not enough money.");
                    }
                    bg.CityCollectsNextRent = false; // condition met
                }
            }
        ),
        new Card(
            "Cryptomining Farm needs repairs. Pay ₿20.",
            (p, g) => {
                Console.WriteLine($"  {p.Name} pays ₿20.");
                if (p.PayMoney(20, out _)) g.AddToJackpot(20);
                else g.HandleBankruptcy(p, null, 20);
            }
        ),
        new Card(
            "You OD'ed on some black market Russian Phenazepam and damn near ruined your life in a weekend. Lose 1 HP.",
            (p, g) => {
                p.LoseHP();
                Console.WriteLine($"  {p.Name} loses 1 HP → {p.HP} HP");
                if (p.HP <= 0) g.Eliminate(p);
            }
        ),
    };

    private List<Card> BuildTacticsCards() => new()
    {
        new Card(
            "Hacked the company records to inflate your hours. Advance to Payday, Recover 1HP and Collect ₿200",
            (p, g) => {
                g.MovePlayerTo(p, 0, collectPayday: false);
                p.CollectMoney(200);
                p.GainHP();
                Console.WriteLine($"  {p.Name} → REGULAR INCOME! +₱200, +1 HP  →  ₿{p.Money} | {p.HP}/6 HP");
            }
        ),
        new Card(
            "Internet celebrity memecoin rugpull scam did numbers! Receive ₿200.",
            (p, g) => { p.CollectMoney(200); Console.WriteLine($"  {p.Name} collects ₿200 → ₿{p.Money}"); }
        ),
        new Card(
            "Insurance didn't cover the adhesive bandage you needed last checkup. Pay ₿50.",
            (p, g) => {
                Console.WriteLine($"  {p.Name} pays ₿50.");
                if (p.PayMoney(50, out _)) g.AddToJackpot(50);
                else g.HandleBankruptcy(p, null, 50);
            }
        ),
        new Card(
            "From frivolous PPP government loans invested into a GPU manufacturer, you earn ₿50!",
            (p, g) => { p.CollectMoney(50); Console.WriteLine($"  {p.Name} collects ₿50 → ₿{p.Money}"); }
        ),
        new Card(
            "Blackmailed the pedophile president into giving you a pardon after showing video of him with both a horse AND Bill Clinton on Epstein Island. GET OUT OF JAIL FOR FREE! Keep it, use it to get out of a PRISON situation.",
            isGoojf: true
        ),
        new Card(
            "HA HA! You got caught doing a FELONY! Go directly to PRISON's Gen Pop. No Payday!",
            (p, g) => g.SendToPrison(p, PrisonStatus.GenPop, noPayday: true)
        ),
        new Card(
            "Aww, looks like someone got caught doing a MISDEMEANOR! Go directly to PRISON's Minimum Security. No Payday!",
            (p, g) => g.SendToPrison(p, PrisonStatus.MinimumSecurity, noPayday: true)
        ),
        new Card(
            "You hold Fyre Festival III! Collect ₿50 from each player! Each paying player loses 1 HP.",
            (p, g) => g.CollectFromAll(p, 50, losersLoseHP: true)
        ),
        new Card(
            "Collect ₿100 from buying and gutting out local businesses to sell to soul-sucking corporations!",
            (p, g) => { p.CollectMoney(100); Console.WriteLine($"  {p.Name} collects ₿100 → ₿{p.Money}"); }
        ),
        new Card(
            "Successfully cooked the books this tax season! Collect ₿20.",
            (p, g) => { p.CollectMoney(20); Console.WriteLine($"  {p.Name} collects ₿20 → ₿{p.Money}"); }
        ),
        new Card(
            "Talk everyone into a Multi-Level Marketing CBD cart scam! Collect ₿10 from each player.",
            (p, g) => g.CollectFromAll(p, 10)
        ),
        new Card(
            "Your Pump-And-Dump property scam with Saudi investors pays off! Receive ₿100.",
            (p, g) => { p.CollectMoney(100); Console.WriteLine($"  {p.Name} collects ₿100 → ₿{p.Money}"); }
        ),
        new Card(
            "Bought an overpriced luxury electric vehicle that looks like it was designed by the SNES FX Chip. Pay ₿100 in out-of-state car insurance.",
            (p, g) => {
                Console.WriteLine($"  {p.Name} pays ₿100.");
                if (p.PayMoney(100, out _)) g.AddToJackpot(100);
                else g.HandleBankruptcy(p, null, 100);
            }
        ),
        new Card(
            "Receive ₿25 in consultancy fees for a startup you never even heard of.",
            (p, g) => { p.CollectMoney(25); Console.WriteLine($"  {p.Name} collects ₿25 → ₿{p.Money}"); }
        ),
        new Card(
            "Court found you liable for building code violations. For each Lv.1 or higher, pay ₿45 per level.",
            (p, g) => {
                int total = p.Properties.Where(pr => pr.Level >= 1).Sum(pr => 45 * pr.Level);
                Console.WriteLine($"  Building code violations: ₿{total}");
                if (p.PayMoney(total, out _)) g.AddToJackpot(total);
                else g.HandleBankruptcy(p, null, total);
            }
        ),
        new Card(
            "You won a libel case against a shitposter on social media who resides in the UK. Collect ₿10.",
            (p, g) => { p.CollectMoney(10); Console.WriteLine($"  {p.Name} collects ₿10 → ₿{p.Money}"); }
        ),
        new Card(
            "Tried to run your business with AI with no returns! Pay ₿150 in token fees!",
            (p, g) => {
                Console.WriteLine($"  {p.Name} pays ₿150 in AI token fees.");
                if (p.PayMoney(150, out _)) g.AddToJackpot(150);
                else g.HandleBankruptcy(p, null, 150);
            }
        ),
        new Card(
            "Someone living on White Avenue who likes to ride the white train going towards the airport has been hissing about you and all the barking from your dogs. They're not going to shut up until they syphon some money from you.",
            (p, g) => {
                var wa = g.Board[14]; // White Avenue index
                if (wa.Owner == p)
                {
                    wa.CityCollectsNextRent = true;
                    Console.WriteLine("  You own White Avenue — next player to land pays the City instead.");
                }
                else if (wa.Owner != null)
                {
                    p.DoubledRentPositions.Add(14);
                    Console.WriteLine($"  {wa.Owner.Name} owns White Avenue — your next rent there is doubled.");
                }
                else
                {
                    int doublePrice = wa.Price * 2;
                    Console.Write($"  White Avenue unowned. Buy for ₿{doublePrice}? (y/n): ");
                    if (YesNo())
                    {
                        if (p.PayMoney(doublePrice, out _))
                        {
                            wa.Owner = p;
                            p.Properties.Add(wa);
                            Console.WriteLine($"  Purchased White Avenue for ₿{doublePrice}.");
                        }
                        else Console.WriteLine("  Not enough money.");
                    }
                    wa.CityCollectsNextRent = false;
                }
            }
        ),
    };
}