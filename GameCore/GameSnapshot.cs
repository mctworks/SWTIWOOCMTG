using System;
using System.Collections.Generic;
using System.Linq;

namespace SWTIWOOCMTG;

/// <summary>
/// Flat, serializable view of a BoardSpace. Ownership is by player Id, not by reference,
/// so this has no cycles and round-trips through JSON cleanly.
/// </summary>
public record BoardSpaceSnapshot(
    int Index,
    string Name,
    SpaceType Type,
    PropertyGroup Group,
    Neighborhood Neighborhood,
    int Price,
    int UpgradeCost,
    int[] Rent,
    Guid? OwnerId,
    int Level,
    bool CityCollectsNextRent,
    bool IsMortgaged,
    int MortgageValue,
    int UnmortgageCost,
    bool IsOwnable);

/// <summary>
/// Flat, serializable view of a Player. Owned properties are listed by board index rather
/// than by reference. GOOJF cards are a count only, since card text isn't needed client-side
/// (LastCardText on the snapshot already carries the most recent draw's flavor text).
/// </summary>
public record PlayerSnapshot(
    Guid Id,
    string Name,
    bool IsCpu,
    int Money,
    int Position,
    int TokenNumber,
    int HP,
    PrisonStatus Prison,
    int TurnsInPrison,
    int LawyerTokens,
    int GoojfCardCount,
    int[] PropertyIndices,
    int[] DoubledRentPositions,
    bool IsEliminated);

public record DebtSnapshot(Guid DebtorId, int Amount, Guid? CreditorId);

public record HouseRulesSnapshot(
    bool Jackpot,
    bool PrisonGenPop,
    bool PropertyColorGroups,
    bool Gentrification,
    bool ArfenhouseRule);

/// <summary>
/// A complete, serializable view of a GameSession at one point in time. Everything that
/// referred to a Player or BoardSpace by object reference in the live game refers to it
/// here by Guid or board index instead, so this can cross a network boundary as-is.
/// </summary>
public record GameSnapshot(
    IReadOnlyList<PlayerSnapshot> Players,
    IReadOnlyList<BoardSpaceSnapshot> Board,
    int Jackpot,
    int LevelsAvailable,
    int MaxesAvailable,
    HouseRulesSnapshot Rules,
    Guid? ArfPlayerId,
    Guid? CurrentPlayerId,
    string? LastCardText,
    IReadOnlyList<DebtSnapshot> PendingDebts,
    int? PendingSpecialPurchaseIndex,
    int PendingSpecialPrice,
    bool IsGameOver,
    Guid? WinnerId,
    TurnPhase Phase,
    int? PendingPurchaseIndex,
    Guid? ManagingPlayerId,
    int? Die1,
    int? Die2,
    string CurrentRoll,
    string Error,
    IReadOnlyList<string> Log);

public static class GameSnapshotBuilder
{
    public static GameSnapshot Build(GameSession session)
    {
        var game = session.Game;

        var players = game.Players.Select(p => new PlayerSnapshot(
            p.Id,
            p.Name,
            p.Ai != null,
            p.Money,
            p.Position,
            p.TokenNumber,
            p.HP,
            p.Prison,
            p.TurnsInPrison,
            p.LawyerTokens,
            p.GoojfCards.Count,
            p.Properties.Select(s => Array.IndexOf(game.Board, s)).ToArray(),
            p.DoubledRentPositions.ToArray(),
            p.IsEliminated
        )).ToList();

        var board = game.Board.Select((s, i) => new BoardSpaceSnapshot(
            i,
            s.Name,
            s.Type,
            s.Group,
            s.Neighborhood,
            s.Price,
            s.UpgradeCost,
            s.Rent,
            s.Owner?.Id,
            s.Level,
            s.CityCollectsNextRent,
            s.IsMortgaged,
            s.MortgageValue,
            s.UnmortgageCost,
            s.IsOwnable
        )).ToList();

        var debts = game.PendingDebts.Select(d => new DebtSnapshot(
            d.Debtor.Id,
            d.Amount,
            d.Creditor?.Id
        )).ToList();

        var rules = new HouseRulesSnapshot(
            game.Rules.Jackpot,
            game.Rules.PrisonGenPop,
            game.Rules.PropertyColorGroups,
            game.Rules.Gentrification,
            game.Rules.ArfenhouseRule);

        int? pendingSpecialIndex = game.PendingSpecialPurchase == null
            ? null
            : Array.IndexOf(game.Board, game.PendingSpecialPurchase);

        int? pendingPurchaseIndex = session.PendingPurchase == null
            ? null
            : Array.IndexOf(game.Board, session.PendingPurchase);

        // Capped so the payload stays small regardless of game length. This is narration for
        // a client's log/chat view, not a source of truth, so history beyond this doesn't matter;
        // the snapshot's other fields are the actual state.
        var recentLog = game.Log.TakeLast(30).ToList();

        return new GameSnapshot(
            players,
            board,
            game.Jackpot,
            game.LevelsAvailable,
            game.MaxesAvailable,
            rules,
            game.ArfPlayer?.Id,
            game.CurrentPlayer?.Id,
            game.LastCardText,
            debts,
            pendingSpecialIndex,
            game.PendingSpecialPrice,
            game.IsGameOver,
            game.Winner?.Id,
            session.Phase,
            pendingPurchaseIndex,
            session.ManagingPlayer?.Id,
            session.Die1,
            session.Die2,
            session.CurrentRoll,
            session.Error,
            recentLog
        );
    }
}