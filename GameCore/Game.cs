using System;
using System.Collections.Generic;
using System.Linq;

namespace SWTIWOOCMTG;

public enum SpaceType
{
    Payday,
    Property,
    Rail,
    Utility,
    Prison,
    Guilty,
    Lotto,
    IrsAudit,
    Socialism,
    RiskCard,
    TacticsCard
}

public enum PropertyGroup
{
    None,
    Magenta,
    LimeGreen,
    White,
    Gold,
    MahoganyBrown,
    Red,
    JadeGreen,
    NeonBlue,
    Rail,
    Utility
}

public enum Neighborhood
{
    None,
    Downtown,
    Westside,
    NorthernDistrict,
    EastVines
}

public enum PrisonStatus
{
    Free,
    MinimumSecurity,
    GenPop
}

public class DebtEntry
{
    public required Player Debtor { get; init; }
    public required int Amount { get; init; }
    public Player? Creditor { get; init; }
}

public enum RollOutcome { Normal, RollAgain, SpeedingToPrison }

public class BoardSpace
{
    public string Name { get; set; } = string.Empty;
    public SpaceType Type { get; set; }
    public PropertyGroup Group { get; set; } = PropertyGroup.None;
    public Neighborhood Neighborhood { get; set; } = Neighborhood.None;
    public int Price { get; set; }
    public int UpgradeCost { get; set; }
    public int[] Rent { get; set; } = Array.Empty<int>();
    public Player? Owner { get; set; }
    public int Level { get; set; }
    public bool CityCollectsNextRent { get; set; }
    public bool IsMortgaged { get; set; }

    public int MortgageValue => Price / 2;
    public int UnmortgageCost => MortgageValue + (int)Math.Ceiling(MortgageValue * 0.10);

    public bool IsOwnable => Type is SpaceType.Property or SpaceType.Rail or SpaceType.Utility;
}

public class Player
{
    public string Name { get; set; }
    public int Money { get; set; }
    public int Position { get; set; }
    public int TokenNumber { get; set; } = 1;
    public int HP { get; set; } = 6;
    public PrisonStatus Prison { get; set; } = PrisonStatus.Free;
    public int TurnsInPrison { get; set; }
    public int LawyerTokens { get; set; }
    public List<Card> GoojfCards { get; set; } = new();
    public List<BoardSpace> Properties { get; set; } = new();
    public HashSet<int> DoubledRentPositions { get; set; } = new();
    public bool IsEliminated { get; set; }

    public Player(string name, int startingMoney = 1500)
    {
        Name = name;
        Money = startingMoney;
    }

    public void CollectMoney(int amount) => Money += amount;

    public bool PayMoney(int amount, out int deficit)
    {
        deficit = 0;
        if (Money >= amount)
        {
            Money -= amount;
            return true;
        }

        deficit = amount - Money;
        return false;
    }

    public void GainHP(int n = 1) => HP = Math.Min(6, HP + n);
    public void LoseHP(int n = 1) => HP = Math.Max(0, HP - n);
}

public class HouseRules
{
    public bool Jackpot { get; set; } = true;
    public bool PrisonGenPop { get; set; } = true;
    public bool PropertyColorGroups { get; set; }
    public bool Gentrification { get; set; }
    public bool ArfenhouseRule { get; set; }
}

public class Card
{
    public string Text { get; set; }
    public bool IsGoojf { get; set; }
    public Action<Player, Game>? Effect { get; set; }
    public Action<Player, Game>? ArfenhouseEffect { get; set; }

    public Card(string text, Action<Player, Game>? effect = null, bool isGoojf = false, Action<Player, Game>? arfenhouseEffect = null)
    {
        Text = text;
        Effect = effect;
        IsGoojf = isGoojf;
        ArfenhouseEffect = arfenhouseEffect;
    }
}

public class Deck
{
    private readonly List<Card> _cards;
    private int _index;

    public Deck(IEnumerable<Card> cards, Random rng)
    {
        _cards = cards.ToList();
        Shuffle(rng);
    }

    public Card Draw()
    {
        if (_cards.Count == 0)
            throw new InvalidOperationException("Deck is empty.");

        var card = _cards[_index];
        _index = (_index + 1) % _cards.Count;
        return card;
    }

    public void ReturnCard(Card card)
    {
        if (!_cards.Contains(card))
        {
            _cards.Add(card);
        }
    }

    private void Shuffle(Random rng)
    {
        for (int i = _cards.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }
    }
}


public class Game
{   
    public void ExportLog(string path) => System.IO.File.WriteAllLines(path, Log);
    public List<Player> Players { get; } = new();
    public BoardSpace[] Board { get; } = new BoardSpace[40];
    public Deck RiskDeck { get; private set; } = null!;
    public Deck TacticsDeck { get; private set; } = null!;
    public List<string> Log { get; } = new();
    public int Jackpot { get; private set; } = 500;
    public int LevelsAvailable { get; private set; } = 32;
    public int MaxesAvailable { get; private set; } = 12;
    public HouseRules Rules { get; } = new();
    public Player? ArfPlayer { get; set; }
    public int ActivePlayerIndex { get; private set; }
    public string? LastCardText { get; private set; }
    public Queue<DebtEntry> PendingDebts { get; } = new();
    public DebtEntry? CurrentDebt => PendingDebts.Count > 0 ? PendingDebts.Peek() : null;
    public BoardSpace? PendingSpecialPurchase { get; private set; }
    public int PendingSpecialPrice { get; private set; }
    public bool IsGameOver => Players.Count > 1 && Players.Count(p => !p.IsEliminated) <= 1;
    public Player? Winner => Players.FirstOrDefault(p => !p.IsEliminated);

    private readonly Random _rng = new();

    public Game()
    {
        InitializeBoard();
        InitializeDecks();
    }

    public Player? CurrentPlayer => Players.ElementAtOrDefault(ActivePlayerIndex);

    public void ClearLastCard() => LastCardText = null;

    public void OfferSpecialPurchase(BoardSpace space, int price)
    {
        PendingSpecialPurchase = space;
        PendingSpecialPrice = price;
    }

    public bool ResolveSpecialPurchase(Player buyer, bool wantsToBuy)
    {
        if (PendingSpecialPurchase == null)
            return false;

        if (wantsToBuy)
        {
            if (buyer.PayMoney(PendingSpecialPrice, out _))
            {
                PendingSpecialPurchase.Owner = buyer;
                buyer.Properties.Add(PendingSpecialPurchase);
                PendingSpecialPurchase = null;
                PendingSpecialPrice = 0;
                return true;
            }
        }

        PendingSpecialPurchase = null;
        PendingSpecialPrice = 0;
        return false;
    }

    public bool TrySetPlayerCount(int humanCount, out string error)
    {
        if (humanCount < 1 || humanCount > 8)
        {
            error = "Number of players must be between 12and 8.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryAddPlayer(string name, out string error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Name can't be blank.";
            return false;
        }

        Players.Add(new Player(name.Trim()));
        error = string.Empty;
        return true;
    }

    public void MovePlayer(Player player, int steps)
    {
        int oldPos = player.Position;
        int newPos = (oldPos + steps) % Board.Length;

        if (newPos < oldPos || (oldPos == 0 && steps > 0 && newPos == 0))
        {
            player.CollectMoney(200);
            player.GainHP();
        }

        player.Position = newPos;
    }

    private int _consecutiveDoubles;

public RollOutcome RegisterRoll(int d1, int d2)
{
    if (d1 != d2)
    {
        _consecutiveDoubles = 0;
        return RollOutcome.Normal;
    }

    _consecutiveDoubles++;
    if (_consecutiveDoubles >= 3)
    {
        _consecutiveDoubles = 0;
        return RollOutcome.SpeedingToPrison;
    }

    return RollOutcome.RollAgain;
}

    public void MovePlayerTo(Player player, int target, bool collectPayday = true)
    {
        int oldPos = player.Position;
        player.Position = target;

        if (collectPayday && target != oldPos && target < oldPos)
        {
            player.CollectMoney(200);
            player.GainHP();
        }
    }

    public void LandOnSpace(Player player, int d1, int d2)
    {
        if (player.IsEliminated)
            return;

        var space = Board[player.Position];

        switch (space.Type)
        {
            case SpaceType.Payday:
                player.CollectMoney(200);
                player.GainHP();
                break;
            case SpaceType.Property:
                Log.Add($"{player.Name} lands on {space.Name}.");
                HandlePropertyLanding(player, space);
                break;
            case SpaceType.Rail:
                HandleRailroadLanding(player, space);
                break;
            case SpaceType.Utility:
                HandleUtilityLanding(player, space, d1 + d2);
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
                HandleSocialism(player);
                break;
            case SpaceType.RiskCard:
                DrawRiskCard(player, d1, d2);
                break;
            case SpaceType.TacticsCard:
                DrawTacticsCard(player, d1, d2);
                break;
            default:
                break;
        }
    }

    public void NextTurn()
    {
        _consecutiveDoubles = 0;

        if (Players.Count == 0) return;

        do
        {
            ActivePlayerIndex = (ActivePlayerIndex + 1) % Players.Count;
        } while (Players[ActivePlayerIndex].IsEliminated && Players.Any(p => !p.IsEliminated));
    }

    public void DrawRiskCard(Player player, int d1, int d2)
    {
        var card = RiskDeck.Draw();
        LastCardText = card.Text;
        Log.Add($"[RISK] {card.Text}");

        if (card.IsGoojf)
        {
            player.GoojfCards.Add(card);
            Log.Add($"{player.Name} keeps this as a Get Out of Jail Free card.");
            return;
        }

        card.Effect?.Invoke(player, this);

        if (Rules.ArfenhouseRule && player == ArfPlayer)
            card.ArfenhouseEffect?.Invoke(player, this);
    }

    public void DrawTacticsCard(Player player, int d1, int d2)
    {
        var card = TacticsDeck.Draw();
        LastCardText = card.Text;
        Log.Add($"[TACTICS] {card.Text}");

        if (card.IsGoojf)
        {
            player.GoojfCards.Add(card);
            Log.Add($"{player.Name} keeps this as a Get Out of Jail Free card.");
            return;
        }

        card.Effect?.Invoke(player, this);

        if (Rules.ArfenhouseRule && player == ArfPlayer)
            card.ArfenhouseEffect?.Invoke(player, this);
    }

    public bool TryPurchaseProperty(Player player, BoardSpace space)
    {
        if (space.Owner != null || !space.IsOwnable)
            return false;

        if (!player.PayMoney(space.Price, out _))
            return false;

        space.Owner = player;
        player.Properties.Add(space);
        return true;
    }

    public bool TryUpgradeProperty(Player player, BoardSpace space, out string error)
{
    if (space.Type != SpaceType.Property)
    {
        error = "Only properties can be upgraded; not rails or utilities.";
        return false;
    }

    if (space.Owner != player)
    {
        error = "You don't own this property.";
        return false;
    }

    if (space.Level >= 5)
    {
        error = "Already at MAX level.";
        return false;
    }

    bool isMaxUpgrade = space.Level == 4; // Lv.4 -> MAX

    if (isMaxUpgrade && MaxesAvailable <= 0)
    {
        error = "Properties are unable to be maximized due to supply shortages. Either sell some property upgrades, or wait for another player to downgrade/sell their properties.";
        return false;
    }
    if (!isMaxUpgrade && LevelsAvailable <= 0)
    {
        error = "Properties are unable to be upgraded due to supply shortages. Either downgrade some MAX properties, or wait for another player to downgrade/sell their properties.";
        return false;
    }

    if (!player.PayMoney(space.UpgradeCost, out _))
    {
        error = "Not enough money to upgrade.";
        return false;
    }

    if (isMaxUpgrade)
    {
        MaxesAvailable--;
        LevelsAvailable += 4; // 4 levels return to the bank
    }
    else
    {
        LevelsAvailable--;
    }

    space.Level++;
    Log.Add($"{player.Name} upgrades {space.Name} to Lv.{(space.Level == 5 ? "MAX" : space.Level.ToString())}.");
    error = "";
    return true;
}

    public bool TryPay(Player payer, int amount, Player? creditor)
{
    if (amount <= 0) return true;

    if (payer.Money >= amount)
    {
        payer.Money -= amount;
        if (creditor != null) creditor.CollectMoney(amount);
        else AddToJackpot(amount);
        return true;
    }

    PendingDebts.Enqueue(new DebtEntry { Debtor = payer, Amount = amount, Creditor = creditor });
    Log.Add($"{payer.Name} owes ₿{amount}{(creditor != null ? $" to {creditor.Name}" : " to the Bank")} and can't cover it. Sell properties/upgrades.");
    return false;
}

    public bool TryPayBail(Player player, out string error)
    {
        int bail = player.Prison == PrisonStatus.GenPop ? 50 : 25;

        if (player.Prison == PrisonStatus.Free)
        {
            error = "Not in prison.";
            return false;
        }

        if (!player.PayMoney(bail, out _))
        {
            error = "Not enough money to pay bail.";
            return false;
        }

        player.Prison = PrisonStatus.Free;
        player.TurnsInPrison = 0;
        Log.Add($"{player.Name} paid ₿{bail} bail and is free.");
        error = "";
        return true;
    }

    public bool TryAppealWithDoubles(Player player, int d1, int d2)
    {
        if (player.Prison == PrisonStatus.Free) return false;

        if (d1 == d2)
        {
            player.Prison = PrisonStatus.Free;
            player.TurnsInPrison = 0;
            Log.Add($"{player.Name} rolled doubles and appealed their case!");
            return true;
        }

        player.TurnsInPrison++;
        Log.Add($"{player.Name} failed to appeal their case ({player.TurnsInPrison}/3 attempts).");

        if (player.TurnsInPrison >= 3)
        {
            player.Prison = PrisonStatus.Free;
            player.TurnsInPrison = 0;
            Log.Add($"{player.Name} did their time and is free from Prison.");
        }

        return false;
    }

    public void TryGoojfCard(Player player, out string error)
    {
        var goojfCard = player.GoojfCards.FirstOrDefault();
        if (goojfCard == null)
        {
            error = "No Get Out of Jail Free card available.";
            return;
        }

        player.GoojfCards.Remove(goojfCard);
        player.Prison = PrisonStatus.Free;
        player.TurnsInPrison = 0;
        Log.Add($"{player.Name} used a Get Out of Jail Free card and has been released.");
        error = "";
    }

    public void TryLawyerToken(Player player, out string error)
    {
        if (player.LawyerTokens <= 0)
        {
            error = "No Lawyer Tokens available.";
            return;
        }

        player.LawyerTokens--;
        player.Prison = PrisonStatus.MinimumSecurity;
        player.TurnsInPrison = 0;
        Log.Add($"{player.Name} used a Lawyer Token and has been moved to Minimum Security.");
        error = "";
    }

    public bool TryDowngradeProperty(Player player, BoardSpace space, out string error)
{
    if (space.Owner != player) { error = "You don't own this property."; return false; }
    if (space.Level <= 0) { error = "No upgrades to sell."; return false; }

    bool wasMax = space.Level == 5;
    int refund = space.UpgradeCost / 2;
    space.Level--;
    player.CollectMoney(refund);

    if (wasMax) { MaxesAvailable++; LevelsAvailable -= 4; }
    else LevelsAvailable++;

    Log.Add($"{player.Name} sells an upgrade on {space.Name} for ₿{refund}. Now Lv.{(space.Level == 0 ? "0 (base)" : space.Level.ToString())}.");
    error = "";
    return true;
}

    public bool TryMortgageProperty(Player player, BoardSpace space, out string error)
    {
        if (space.Owner != player) { error = "You don't own this property."; return false; }
        if (space.Level > 0) { error = "Sell all upgrades on this property first."; return false; }
        if (space.IsMortgaged) { error = "Already mortgaged."; return false; }

        space.IsMortgaged = true;
        player.CollectMoney(space.MortgageValue);
        Log.Add($"{player.Name} mortgages {space.Name} for ₿{space.MortgageValue}.");
        error = "";
        return true;
    }

    public bool TryUnmortgageProperty(Player player, BoardSpace space, out string error)
    {
        if (space.Owner != player) { error = "You don't own this property."; return false; }
        if (!space.IsMortgaged) { error = "Not mortgaged."; return false; }
        if (!player.PayMoney(space.UnmortgageCost, out _)) { error = "Not enough money."; return false; }

        space.IsMortgaged = false;
        Log.Add($"{player.Name} unmortgages {space.Name} for ₿{space.UnmortgageCost}.");
        error = "";
        return true;
    }

    public bool TryResolveDebt(out string error)
{
    var debt = CurrentDebt;
    if (debt == null) { error = "No outstanding debt."; return false; }

    if (debt.Debtor.Money < debt.Amount)
    {
        error = $"{debt.Debtor.Name} is still short ₿{debt.Amount - debt.Debtor.Money}.";
        return false;
    }

    debt.Debtor.Money -= debt.Amount;
    if (debt.Creditor != null) debt.Creditor.CollectMoney(debt.Amount);
    else AddToJackpot(debt.Amount);

    Log.Add($"{debt.Debtor.Name} settles their ₿{debt.Amount} debt.");
    PendingDebts.Dequeue();
    error = "";
    return true;
}

    public void DeclareBankruptcy(Player player)
    {
        Eliminate(player);
        foreach (var prop in player.Properties.ToList())
        {
            prop.Owner = null;
            prop.Level = 0;
            prop.IsMortgaged = false;
        }
        player.Properties.Clear();

        /* if (PlayerInDebt == player)
        {
            PlayerInDebt = null;
            DebtAmount = 0;
            DebtCreditor = null;
        } */

        if (CurrentDebt?.Debtor == player)
        PendingDebts.Dequeue();
    }

    public void AddToJackpot(int amount)
    {
        Jackpot += amount;
    }

    public void CollectFromAll(Player collector, int amount, bool losersLoseHP = false)
    {
        foreach (var other in Players.Where(p => !p.IsEliminated && p != collector))
        {
            if (TryPay(other, amount, collector) && losersLoseHP)
            {
                other.LoseHP();
                if (other.HP <= 0)
                {
                    other.IsEliminated = true;
                }
            }
        }
    }

    public void PayAllPlayers(Player payer, int amount)
    {
        foreach (var other in Players.Where(p => !p.IsEliminated && p != payer))
        {
            if (payer.IsEliminated)
                return;

            TryPay(payer, amount, other);
        }
    }

    public void SendToPrison(Player player, PrisonStatus level, bool noPayday = true)
    {
        player.Prison = level;
        player.TurnsInPrison = 0;
        player.Position = 10;
    }

    public bool PlayerControlsNeighborhood(Player? owner, Neighborhood neighborhood)
    {
        if (owner == null || neighborhood == Neighborhood.None)
            return false;

        var spacesInHood = Board.Where(space => space.Neighborhood == neighborhood && space.IsOwnable).ToArray();
        if (spacesInHood.Length == 0)
            return false;

        return spacesInHood.All(space => space.Owner == owner);
    }

    private void InitializeBoard()
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
                             Price = 200,
                             Rent = new[] { 25, 50, 100, 200 } },
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
                             Price = 200,
                             Rent = new[] { 25, 50, 100, 200 } },
            /* 16 */ new() { Name = "Dick Valentine International Airport",  
                             Type = SpaceType.Property,  
                             Group = PropertyGroup.Gold,
                             Neighborhood = Neighborhood.Westside,   
                             Price = 180, 
                             UpgradeCost = 100, 
                             Rent = new[] { 14,  70,  200, 550, 750,  950  } },
            /* 17 */ new() { Name = "Underhanded Tactics Card",
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
                             Price = 200,
                             Rent = new[] { 25, 50, 100, 200 } },
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
                             Price = 200,
                             Rent = new[] { 25, 50, 100, 200 } },
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

    public void InitializeDecks()
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
            },
            arfenhouseEffect: (p, g) => {
                g.SendToPrison(p, PrisonStatus.GenPop, noPayday: true);
                g.Log.Add($"On the way, {p.Name} goes to Prison on FELONY charges of smuggling fireworks onto MCT630 Island!"); // arfenhouse prison rule example
            }
        ),
        new Card(
            "Black Market \"Work Hacks\" were Super Effective!! Advance to Payday, Recover 1HP and Collect ₿200",
            (p, g) => {
                g.MovePlayerTo(p, 0, collectPayday: false);
                p.CollectMoney(200);
                p.GainHP();
                Log.Add($"  {p.Name} → PAYDAY! +₿200, +1 HP  →  ₿{p.Money} | {p.HP}/6 HP");
            },
            arfenhouseEffect: (p, g) => {
                g.SendToPrison(p, PrisonStatus.MinimumSecurity, noPayday: true);
                g.Log.Add($"Also, {p.Name} is found guilty on misdeameanor \"Work Hack\" possession charges, and goes straight to Minimum Security Prison!"); // arfenhouse prison rule example
            }
        ),
        new Card(
            "Take a Bullet Train to Five Spoons Square! (If you pass Payday, Recover 1HP and Collect ₿200)",
            (p, g) => {
                g.MovePlayerTo(p, 24, collectPayday: true);
                g.LandOnSpace(p, 0, 0);
            }
        ),
        new Card(
            "Take the public bus to Soucross Place! (If you pass Payday, Recover 1HP and Collect ₿200)",
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
                    Log.Add($"  Special rent: [{d1}]+[{d2}]={d1+d2} × 10 = ₿{rent} → {space.Owner.Name}");
                    g.TryPay(p, rent, space.Owner);
                }
                else g.LandOnSpace(p, 0, 0);
            },
            arfenhouseEffect: (p, g) => { 
                g.TryPay(p, 10, null); 
                Log.Add($"  {p.Name} has to pay ₿10 in fines for trying to jump the gate.");
            } //Arfenhouse rule $10 fee
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
                    Log.Add($"  Special Fees: [{d1}]+[{d2}]={d1+d2} × 10 = ₿{rent} → {space.Owner.Name}");
                    g.TryPay(p, rent, space.Owner);
                }
                else g.LandOnSpace(p, 0, 0);
            }
        ),
        new Card(
            "Take the Metro Rail to the Blue Line! (If you pass Payday, Recover 1HP and Collect ₿200)",
            (p, g) => {
                g.MovePlayerTo(p, 5, collectPayday: true);
                g.LandOnSpace(p, 0, 0);
            }, arfenhouseEffect: (p, g) => { 
                g.TryPay(p, 10, null); 
                Log.Add($"  {p.Name} has to pay ₿10 in fines for trying to jump the gate.");
            } //Arfenhouse rule $10 fee
        ),
        new Card(
            "Insider trading tip pays off! Collect ₿50.",
            (p, g) => { p.CollectMoney(50); Log.Add($"  {p.Name} collects ₿50 → ₿{p.Money}"); }, 
            arfenhouseEffect: (p, g) => {
                g.SendToPrison(p, PrisonStatus.MinimumSecurity, noPayday: true);
                g.Log.Add($"Also, {p.Name} is found guilty insider trading, and goes straight to Minimum Security Prison!"); // arfenhouse prison rule example
            }
        ),
        new Card(
            "Go Back 3 Spaces!",
            (p, g) => {
                // No Payday awarded for moving backward
                p.Position = (p.Position - 3 + 40) % 40;
                Log.Add($"  → [{p.Position}] {g.Board[p.Position].Name}");
                g.LandOnSpace(p, 0, 0);
            }
        ),
        new Card(
            "Uh OH! You were caught doing a FELONY! Go directly to PRISON's Gen Pop. No Payday!",
            (p, g) => g.SendToPrison(p, PrisonStatus.GenPop, noPayday: true),
            arfenhouseEffect: (p, g) => { 
                g.TryPay(p, 100, null); 
                Log.Add($"  {p.Name} also has to pay ₿100 in fines.");
            } //Arfenhouse rule $100 fee
        ),
        new Card(
            "Bloodsucking HOA Fees! For each property Lv.1 or higher, pay ₿25 per level.",
            (p, g) => {
                int total = p.Properties.Where(pr => pr.Level >= 1).Sum(pr => 25 * pr.Level);
                Log.Add($"  HOA Fees: ₿{total}");
                g.TryPay(p, total, null);
            }
        ),
        new Card(
            "Pay the city ₿15 for permit fees.",
            (p, g) => {
                Log.Add($"  {p.Name} pays ₿15 in permit fees.");
                g.TryPay(p, 15, null);
            }
        ),
        new Card(
            "Pay each player ₿50 to keep quiet about the Diddy Party you went to back in 2005.",
            (p, g) => g.PayAllPlayers(p, 50),
            arfenhouseEffect: (p, g) => {
                g.SendToPrison(p, PrisonStatus.GenPop, noPayday: true);
                g.Log.Add($"Meanwhile, {p.Name} goes to Prison on FELONY charges for what THEY did at the Diddy Party back in 2005!"); // arfenhouse prison rule example
            }
        ),
        new Card(
            "You shorted the market and made a profit! Collect ₿150.",
            (p, g) => { p.CollectMoney(150); Log.Add($"  {p.Name} collects ₿150 → ₿{p.Money}"); }
        ),
        new Card(
            "You seem to be accruing more penalties than a Boston Bruin, but this here card says GET OUT OF JAIL FOR FREE! Keep it for whenever you need to get out of a PRISON situation that no lawyer can handle...",
            isGoojf: true
        ),
        new Card(
            "Jeepers, you got caught doing a MISDEMEANOR! Go directly to PRISON's Minimum Security. No Payday!",
            (p, g) => g.SendToPrison(p, PrisonStatus.MinimumSecurity, noPayday: true)
        ),
        new Card(
            "You did a solid for a sleazy law practice. Earn a Lawyer Token!",
            (p, g) => {
                p.LawyerTokens++;
                Log.Add($"  {p.Name} gains a Lawyer Token ({p.LawyerTokens} total)");
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
                    Log.Add($"  You own Buckingham Green; next player to land pays the City instead.");
                }
                else if (bg.Owner != null)
                {
                    // Someone else owns it: double rent next time YOU land on it
                    p.DoubledRentPositions.Add(34);
                    Log.Add($"  {bg.Owner.Name} owns Buckingham Green; your next rent there is doubled.");
                }
                else
                {
                    // City owned: must buy at 2× original price
                    g.OfferSpecialPurchase(bg, bg.Price * 2);
                }
            }
        ),
        new Card(
            "Cryptomining Farm needs repairs. Pay ₿20.",
            (p, g) => {
                Log.Add($"  {p.Name} pays ₿20.");
                g.TryPay(p, 20, null);
            }
        ),
        new Card(
            "You OD'ed on some black market Russian Phenazepam and damn near ruined your life in a weekend. Lose 1 HP.",
            (p, g) => {
                p.LoseHP();
                Log.Add($"  {p.Name} loses 1 HP → {p.HP} HP");
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
                Log.Add($"  {p.Name} → REGULAR INCOME! +₱200, +1 HP  →  ₿{p.Money} | {p.HP}/6 HP");
            }
        ),
        new Card(
            "Internet celebrity memecoin rugpull scam did numbers! Receive ₿200.",
            (p, g) => { p.CollectMoney(200); Log.Add($"  {p.Name} collects ₿200 → ₿{p.Money}"); }
        ),
        new Card(
            "Insurance didn't cover the adhesive bandage you needed last checkup. Pay ₿50.",
            (p, g) => {
                Log.Add($"  {p.Name} pays ₿50.");
                g.TryPay(p, 50, null);
            }
        ),
        new Card(
            "From frivolous PPP government loans invested into a GPU manufacturer, you earn ₿50!",
            (p, g) => { p.CollectMoney(50); Log.Add($"  {p.Name} collects ₿50 → ₿{p.Money}"); }
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
            "Collect ₿100 from buying and gutting out local businesses to sell to soul-sucking private equity corporations!",
            (p, g) => { p.CollectMoney(100); Log.Add($"  {p.Name} collects ₿100 → ₿{p.Money}"); }
        ),
        new Card(
            "Successfully cooked the books this tax season! Collect ₿20.",
            (p, g) => { p.CollectMoney(20); Log.Add($"  {p.Name} collects ₿20 → ₿{p.Money}"); }
        ),
        new Card(
            "Talk everyone into a multi-level marketing CBD cart scam! Collect ₿10 from each player.",
            (p, g) => g.CollectFromAll(p, 10),
            arfenhouseEffect: (p, g) => { 
                g.TryPay(p, 50, null); 
                Log.Add($"  {p.Name} gets hooked, buys ₿50 in bad carts and loses 1 HP.");
            } //Arfenhouse rule - NEED TO IMPLEMENT HP LOSS
        ),
        new Card(
            "Your Pump-And-Dump property scam with Saudi investors pays off! Receive ₿100.",
            (p, g) => { p.CollectMoney(100); Log.Add($"  {p.Name} collects ₿100 → ₿{p.Money}"); }
        ),
        new Card(
            "Bought an overpriced luxury electric vehicle that looks like it was designed by the SNES FX Chip. Pay ₿100 in out-of-state car insurance.",
            (p, g) => {
                Log.Add($"  {p.Name} pays ₿100.");
                g.TryPay(p, 100, null);
            }
        ),
        new Card(
            "Receive ₿25 in consultancy fees for a startup you never even heard of.",
            (p, g) => { p.CollectMoney(25); Log.Add($"  {p.Name} collects ₿25 → ₿{p.Money}"); }
        ),
        new Card(
            "Court found you liable for building code violations. For each Lv.1 or higher, pay ₿45 per level.",
            (p, g) => {
                int total = p.Properties.Where(pr => pr.Level >= 1).Sum(pr => 45 * pr.Level);
                Log.Add($"  Building code violations: ₿{total}");
                g.TryPay(p, total, null);
            }
        ),
        new Card(
            "You won a libel case against an online shitposter who resides in the UK! Collect ₿10.",
            (p, g) => { p.CollectMoney(10); Log.Add($"  {p.Name} collects ₿10 → ₿{p.Money}"); }
        ),
        new Card(
            "Tried to run your business with AI with no returns! Pay ₿150 in token fees!",
            (p, g) => {
                Log.Add($"  {p.Name} pays ₿150 in AI token fees.");
                g.TryPay(p, 150, null);
            }
        ),
        new Card(
            "Someone living on White Avenue who likes to ride the white train going towards the airport has been hissing about you and all the barking from your dogs. They're not going to shut up until they syphon some money from you.",
            (p, g) => {
                var wa = g.Board[14]; // White Avenue index
                if (wa.Owner == p)
                {
                    wa.CityCollectsNextRent = true;
                    Log.Add("  You own White Avenue; next player to land pays the City instead.");
                }
                else if (wa.Owner != null)
                {
                    p.DoubledRentPositions.Add(14);
                    Log.Add($"  {wa.Owner.Name} owns White Avenue; your next rent there is doubled.");
                }
                else
                {
                    g.OfferSpecialPurchase(wa, wa.Price * 2);
                }
            }
        ),
    };

    // ── Utility ────────────────────────────────────────────────────────────────
    
    private static bool YesNo()
    {
        string? input = Console.ReadLine()?.Trim().ToLower();
        return input == "y" || input == "yes";
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

    public void Eliminate(Player player)
    {
        if (player.IsEliminated) return;
        player.IsEliminated = true;
        if (player.Money > 0) { AddToJackpot(player.Money); player.Money = 0; }
        Log.Add($"\n  The harsh world of Capitalism has claimed the life of {player.Name}. They have been eliminated. All properties returned to the bank, and any remaining money added to the Lotto pool. Tots and Pears go out to {player.Name}'s friends and family.");
    }


    private void HandlePropertyLanding(Player player, BoardSpace space)
    {
        if (space.Owner == null || space.Owner == player || space.IsMortgaged)
            return;

        int rent = space.Rent.ElementAtOrDefault(space.Level);
        if (rent <= 0)
            rent = space.Rent.FirstOrDefault();

        if (TryPay(player, rent, space.Owner))
        {
            Log.Add($"{player.Name} pays ₿{rent} rent to {space.Owner.Name} for {space.Name}.");
        }
    }

    private void HandleRailroadLanding(Player player, BoardSpace space)
    {
        if (space.Owner == null || space.Owner == player || space.IsMortgaged)
            return;

        int ownedCount = space.Owner.Properties.Count(p => p.Type == SpaceType.Rail);
        int rentIndex = Math.Min(ownedCount - 1, space.Rent.Length - 1);
        int rent = space.Rent.ElementAtOrDefault(rentIndex);

        if (TryPay(player, rent, space.Owner))
            Log.Add($"{player.Name} pays ₿{rent} rent to {space.Owner.Name} for {space.Name}.");
    }

    private void HandleUtilityLanding(Player player, BoardSpace space, int diceTotal)
    {
        if (space.Owner == null || space.Owner == player || space.IsMortgaged)
            return;

        int rent = diceTotal * 10;
        if (TryPay(player, rent, space.Owner))
        {
            Log.Add($"{player.Name} pays ₿{rent} rent to {space.Owner.Name} for {space.Name}.");
        }
    }

    private void HandleGuilty(Player player)
    {
        SendToPrison(player, PrisonStatus.GenPop, noPayday: true);
    }

    private void HandleLotto(Player player)
    {
        player.CollectMoney(Jackpot);
        Jackpot = 500;
    }

    private void HandleIrsAudit(Player player)
    {
        TryPay(player, 100, null);
    }

    private void HandleSocialism(Player player)
    {
        TryPay(player, 50, null);
    }
}
