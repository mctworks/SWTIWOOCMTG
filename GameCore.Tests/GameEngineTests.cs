using SWTIWOOCMTG;
using Xunit;

namespace SWTIWOOCMTG.Tests;

public class GameEngineTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static (Game g, Player a, Player b) NewGame(int seed = 1)
    {
        var g = new Game(new Random(seed));
        g.TryAddPlayer("A", out _);
        g.TryAddPlayer("B", out _);
        return (g, g.Players[0], g.Players[1]);
    }

    private static BoardSpace Give(Game g, Player owner, int index, int level = 0)
    {
        var s = g.Board[index];
        s.Owner = owner;
        s.Level = level;
        owner.Properties.Add(s);
        return s;
    }

    // ── seeded RNG ─────────────────────────────────────────────────────────────

    [Fact]
    public void SameSeed_GivesSameDiceAndDeckOrder()
    {
        var g1 = new Game(new Random(42));
        var g2 = new Game(new Random(42));

        for (int i = 0; i < 20; i++)
            Assert.Equal(g1.RollDice(), g2.RollDice());

        for (int i = 0; i < 10; i++)
            Assert.Equal(g1.RiskDeck.Draw().Text, g2.RiskDeck.Draw().Text);
    }

    // ── minor fixes ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void TrySetPlayerCount_AllowsTwoToEight(int count, bool expected)
    {
        var g = new Game();
        Assert.Equal(expected, g.TrySetPlayerCount(count, out var error));
        if (!expected) Assert.Contains("2 and 8", error);
    }

    [Fact]
    public void TryGoojfCard_UsesCardAndFreesPlayer()
    {
        var (g, a, _) = NewGame();
        a.Prison = PrisonStatus.GenPop;

        Assert.False(g.TryGoojfCard(a, out _)); // none held

        a.GoojfCards.Add(new Card("x", isGoojf: true));
        Assert.True(g.TryGoojfCard(a, out _));
        Assert.Equal(PrisonStatus.Free, a.Prison);
        Assert.Empty(a.GoojfCards);
    }

    [Fact]
    public void TryLawyerToken_ReturnsFalseWithoutToken_TrueWithToken()
    {
        var (g, a, _) = NewGame();
        a.Prison = PrisonStatus.GenPop;

        Assert.False(g.TryLawyerToken(a, out _));

        a.LawyerTokens = 1;
        Assert.True(g.TryLawyerToken(a, out _));
        Assert.Equal(PrisonStatus.MinimumSecurity, a.Prison);
    }

    // ── double Payday ──────────────────────────────────────────────────────────

    [Fact]
    public void LandingExactlyOnPayday_PaysOnce()
    {
        var (g, a, _) = NewGame();
        a.Position = 35;
        a.HP = 3;

        g.MovePlayer(a, 5);
        g.LandOnSpace(a, 2, 3);

        Assert.Equal(0, a.Position);
        Assert.Equal(1700, a.Money);
        Assert.Equal(4, a.HP);
    }

    [Fact]
    public void PassingPayday_PaysOnce()
    {
        var (g, a, _) = NewGame();
        a.Position = 38;
        a.HP = 3;

        g.MovePlayer(a, 5);          // 38 -> 3
        g.LandOnSpace(a, 2, 3);      // unowned property, no payment

        Assert.Equal(3, a.Position);
        Assert.Equal(1700, a.Money);
        Assert.Equal(4, a.HP);
    }

    [Fact]
    public void NotReachingPayday_PaysNothing()
    {
        var (g, a, _) = NewGame();
        a.Position = 10;

        g.MovePlayer(a, 7);

        Assert.Equal(17, a.Position);
        Assert.Equal(1500, a.Money);
    }

    // ── card rent modifiers ────────────────────────────────────────────────────

    [Fact]
    public void DoubledRentPosition_DoublesRentExactlyOnce()
    {
        var (g, a, b) = NewGame();
        Give(g, b, 1);                       // Lucy's Trailer Park, base rent 2
        a.DoubledRentPositions.Add(1);
        a.Position = 1;

        g.LandOnSpace(a, 0, 0);
        Assert.Equal(1496, a.Money);         // paid 4
        Assert.Equal(1504, b.Money);
        Assert.DoesNotContain(1, a.DoubledRentPositions);

        g.LandOnSpace(a, 0, 0);
        Assert.Equal(1494, a.Money);         // back to 2
        Assert.Equal(1506, b.Money);
    }

    [Fact]
    public void CityCollectsNextRent_PaysJackpotNotOwner_AndOwnerLandingDoesNotConsumeIt()
    {
        var (g, a, b) = NewGame();
        var space = Give(g, b, 1);
        space.CityCollectsNextRent = true;
        int jackpotBefore = g.Jackpot;

        b.Position = 1;
        g.LandOnSpace(b, 0, 0);              // owner lands: nothing happens
        Assert.True(space.CityCollectsNextRent);

        a.Position = 1;
        g.LandOnSpace(a, 0, 0);

        Assert.Equal(1498, a.Money);
        Assert.Equal(1500, b.Money);         // owner got nothing
        Assert.Equal(jackpotBefore + 2, g.Jackpot);
        Assert.False(space.CityCollectsNextRent);
    }

    // ── house rules that were never read ───────────────────────────────────────

    [Fact]
    public void JackpotRuleOff_BankPaymentsAndLottoDoNothing()
    {
        var (g, a, _) = NewGame();
        g.Rules.Jackpot = false;
        int before = g.Jackpot;

        g.TryPay(a, 100, null);
        Assert.Equal(before, g.Jackpot);
        Assert.Equal(1400, a.Money);

        a.Position = 20;
        g.LandOnSpace(a, 0, 0);
        Assert.Equal(1400, a.Money);
    }

    [Fact]
    public void JackpotRuleOn_LottoPaysPotAndResetsIt()
    {
        var (g, a, _) = NewGame();
        g.AddToJackpot(100);                 // 600
        a.Position = 20;

        g.LandOnSpace(a, 0, 0);

        Assert.Equal(2100, a.Money);
        Assert.Equal(500, g.Jackpot);
    }

    [Fact]
    public void GenPopRuleOff_GenPopSentencesBecomeMinimumSecurity()
    {
        var (g, a, _) = NewGame();
        g.Rules.PrisonGenPop = false;

        g.SendToPrison(a, PrisonStatus.GenPop);

        Assert.Equal(PrisonStatus.MinimumSecurity, a.Prison);
        Assert.Equal(10, a.Position);
    }

    // ── elimination paths ──────────────────────────────────────────────────────

    [Fact]
    public void Bankruptcy_ReleasesPropertiesAndReturnsUpgradeSupply()
    {
        var (g, a, _) = NewGame();
        var s1 = Give(g, a, 1, level: 3);
        var s3 = Give(g, a, 3, level: 5);
        s3.IsMortgaged = true;
        a.Money = 40;
        int levels = g.LevelsAvailable, maxes = g.MaxesAvailable, jackpot = g.Jackpot;

        g.DeclareBankruptcy(a);

        Assert.True(a.IsEliminated);
        Assert.Empty(a.Properties);
        Assert.All(new[] { s1, s3 }, s =>
        {
            Assert.Null(s.Owner);
            Assert.Equal(0, s.Level);
            Assert.False(s.IsMortgaged);
        });
        Assert.Equal(levels + 3, g.LevelsAvailable);
        Assert.Equal(maxes + 1, g.MaxesAvailable);
        Assert.Equal(jackpot + 40, g.Jackpot);
    }

    [Fact]
    public void HpElimination_ViaCollectFromAll_ReleasesProperties()
    {
        var (g, a, b) = NewGame();
        var s = Give(g, b, 1, level: 2);
        b.HP = 1;
        int levels = g.LevelsAvailable;

        g.CollectFromAll(a, 50, losersLoseHP: true);

        Assert.True(b.IsEliminated);
        Assert.Null(s.Owner);
        Assert.Equal(0, s.Level);
        Assert.Equal(levels + 2, g.LevelsAvailable);
    }

    [Fact]
    public void Bankruptcy_DropsEveryQueuedDebtOfTheDebtor()
    {
        var (g, a, _) = NewGame();
        g.TryAddPlayer("C", out _);

        g.PayAllPlayers(a, 2000);            // A can't cover either B or C
        Assert.Equal(2, g.PendingDebts.Count);

        g.DeclareBankruptcy(a);

        Assert.Empty(g.PendingDebts);
    }

    [Fact]
    public void Elimination_RedirectsDebtsOwedToTheEliminatedPlayerToTheBank()
    {
        var (g, a, b) = NewGame();
        g.TryPay(b, 5000, a);                // B owes A, can't pay
        Assert.Same(a, g.CurrentDebt!.Creditor);

        g.Eliminate(a);

        Assert.Same(b, g.CurrentDebt!.Debtor);
        Assert.Null(g.CurrentDebt.Creditor);
    }

    // ── GOOJF duplication ──────────────────────────────────────────────────────

    [Fact]
    public void Deck_HeldGoojfCard_NeverReappearsUntilReturned()
    {
        var goojf = new Card("free", isGoojf: true);
        var deck = new Deck(new[] { new Card("a"), new Card("b"), goojf }, new Random(1));

        var drawn = Enumerable.Range(0, 30).Select(_ => deck.Draw()).ToList();
        Assert.Equal(1, drawn.Count(c => ReferenceEquals(c, goojf)));

        deck.ReturnCard(goojf);
        var afterReturn = Enumerable.Range(0, 3).Select(_ => deck.Draw()).ToList();
        Assert.Contains(afterReturn, c => ReferenceEquals(c, goojf));
    }

    [Fact]
    public void Deck_ReturnCard_IgnoresCardsFromAnotherDeckAndDoubleReturns()
    {
        var goojf = new Card("free", isGoojf: true);
        var deck = new Deck(new[] { new Card("a"), goojf }, new Random(1));
        var foreign = new Card("foreign", isGoojf: true);

        while (!ReferenceEquals(deck.Draw(), goojf)) { }
        Assert.Equal(1, deck.Count);

        deck.ReturnCard(foreign);
        Assert.Equal(1, deck.Count);

        deck.ReturnCard(goojf);
        deck.ReturnCard(goojf);
        Assert.Equal(2, deck.Count);
    }

    // ── Property Color Groups = classic build rules ────────────────────────────
    // Magenta group = board[1] + board[3] (upgrade cost 50 each).

    [Fact]
    public void ColorGroupsOff_UpgradeNeedsNoMonopoly_AndMortgageIgnoresMates()
    {
        var (g, a, _) = NewGame();
        Give(g, a, 1);
        Give(g, a, 3);

        Assert.True(g.TryUpgradeProperty(a, g.Board[1], out _));
        Assert.True(g.TryUpgradeProperty(a, g.Board[1], out _));   // uneven is fine
        Assert.True(g.TryMortgageProperty(a, g.Board[3], out _));  // mate has upgrades, fine
    }

    [Fact]
    public void ColorGroupsOn_UpgradeRequiresWholeGroup()
    {
        var (g, a, _) = NewGame();
        g.Rules.PropertyColorGroups = true;
        Give(g, a, 1);                       // board[3] stays unowned

        Assert.False(g.TryUpgradeProperty(a, g.Board[1], out var error));
        Assert.Contains("color group", error);
        Assert.Equal(0, g.Board[1].Level);

        Give(g, a, 3);
        Assert.True(g.TryUpgradeProperty(a, g.Board[1], out _));
    }

    [Fact]
    public void ColorGroupsOn_MustBuildEvenly()
    {
        var (g, a, _) = NewGame();
        g.Rules.PropertyColorGroups = true;
        Give(g, a, 1);
        Give(g, a, 3);

        Assert.True(g.TryUpgradeProperty(a, g.Board[1], out _));   // 1/0
        Assert.False(g.TryUpgradeProperty(a, g.Board[1], out _));  // would be 2/0
        Assert.True(g.TryUpgradeProperty(a, g.Board[3], out _));   // 1/1
        Assert.True(g.TryUpgradeProperty(a, g.Board[1], out _));   // 2/1
        Assert.Equal(2, g.Board[1].Level);
        Assert.Equal(1, g.Board[3].Level);
    }

    [Fact]
    public void ColorGroupsOn_MortgagedGroupMateBlocksUpgrade()
    {
        var (g, a, _) = NewGame();
        g.Rules.PropertyColorGroups = true;
        Give(g, a, 1);
        Give(g, a, 3);
        Assert.True(g.TryMortgageProperty(a, g.Board[3], out _));

        Assert.False(g.TryUpgradeProperty(a, g.Board[1], out _));
    }

    [Fact]
    public void ColorGroupsOn_MustSellEvenly()
    {
        var (g, a, _) = NewGame();
        g.Rules.PropertyColorGroups = true;
        Give(g, a, 1, level: 2);
        Give(g, a, 3, level: 1);

        Assert.False(g.TryDowngradeProperty(a, g.Board[3], out _)); // 1 -> 0 while mate is 2
        Assert.True(g.TryDowngradeProperty(a, g.Board[1], out _));  // 2 -> 1
        Assert.True(g.TryDowngradeProperty(a, g.Board[3], out _));  // 1 -> 0 (mate is 1)
    }

    [Fact]
    public void ColorGroupsOn_CannotMortgageWhileAnyGroupMemberIsUpgraded()
    {
        var (g, a, _) = NewGame();
        g.Rules.PropertyColorGroups = true;
        Give(g, a, 1, level: 1);
        Give(g, a, 3);

        Assert.False(g.TryMortgageProperty(a, g.Board[3], out _));

        Assert.True(g.TryDowngradeProperty(a, g.Board[1], out _));
        Assert.True(g.TryMortgageProperty(a, g.Board[3], out _));
    }

    [Fact]
    public void ColorGroupsOn_AiDebtTriageSellsEvenlyUntilCovered()
    {
        var (g, a, _) = NewGame();
        g.Rules.PropertyColorGroups = true;
        Give(g, a, 1, level: 3);
        Give(g, a, 3, level: 3);
        a.Money = 10;
        g.TryPay(a, 80, null);               // needs 70 more -> three sales at 25

        new PlaceholderPlayerAI().HandleDebt(a, g);

        Assert.True(a.Money >= 80);
        Assert.Equal(3, g.Board[1].Level + g.Board[3].Level); // 6 levels - 3 sold
        Assert.True(Math.Abs(g.Board[1].Level - g.Board[3].Level) <= 1);
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentDeckOrder()
    {
        var g1 = new Game(new Random(1));
        var g2 = new Game(new Random(2));
        var o1 = Enumerable.Range(0, 10).Select(_ => g1.RiskDeck.Draw().Text).ToList();
        var o2 = Enumerable.Range(0, 10).Select(_ => g2.RiskDeck.Draw().Text).ToList();
        Assert.NotEqual(o1, o2);
    }
}