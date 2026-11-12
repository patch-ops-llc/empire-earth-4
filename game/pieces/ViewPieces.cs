namespace Game;

/// <summary>
/// The pieces of the view, in the order they add themselves to a match: the order of the layers
/// in the world, of the readouts in the top bar and of the rows in the order table.
/// </summary>
/// <remarks>
/// This is the one line a new piece adds outside its own folder, beside its part of the smoke
/// test. Each piece of the plan has a line of its own below, and a piece adds its line directly
/// under it, so that two pieces never add at the same place and their pull requests merge in
/// either order. A piece the plan does not name yet adds a line for itself above "Later pieces".
/// </remarks>
public static class ViewPieces
{
    public static IReadOnlyList<ViewPiece> All { get; } =
    [
        // The match itself: who the player is in it, who has left, and the way out.
        new MatchPiece(),

        // Units: selection, the move order and its mark.
        new UnitsPiece(),

        // 11v. Combat: health bars, shots and hits, the fighting orders.
        new CombatPiece(),

        // 13v. Economy: resources and supply, deposits, the work order.
        new EconomyPiece(),

        // 14v. Research: the grid.
        new ResearchPiece(),

        // 15v. Construction and production: structures, the build menu, the production panel.
        new StructuresPiece(),
        new BuildPiece(),

        // 16bv. Structures in combat: damage shown on structures, Towers shooting, the attack order on a structure.
        new SiegePiece(),

        // 17v. Fog of war.
        new FogPiece(),

        // 24v. Minimap.
        new MinimapPiece(),

        // Inspection: what the selected units are, accent badges, the epoch announcement.
        new InspectionPiece(),

        // 19v. Perks: splash, Charge, Regeneration and Reserves as the player sees them.
        new PerksPiece(),

        // Later pieces.
    ];
}
