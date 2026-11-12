namespace Ai;

/// <summary>
/// The stockpile as the computer player spends it in one round of thinking, in whole resources.
/// What it orders is paid here at once, so that it never orders more than the stockpile covers.
/// What it wants and cannot pay yet is held back from everything it considers afterwards, so that
/// cheap purchases never keep an expensive one from being reached. And everything it bought or
/// wanted is added up, as the purchases its workers have to earn.
/// </summary>
internal sealed class AiBudget
{
    private int _stockMaterials;
    private int _stockEnergy;
    private int _materials;
    private int _energy;
    private int _heldMaterials;
    private int _heldEnergy;
    private long _wantedMaterials;
    private long _wantedEnergy;

    /// <summary>Starts a round with what the stockpile holds.</summary>
    public void Reset(int materials, int energy)
    {
        _stockMaterials = materials;
        _stockEnergy = energy;
        _materials = materials;
        _energy = energy;
        _heldMaterials = 0;
        _heldEnergy = 0;
        _wantedMaterials = 0;
        _wantedEnergy = 0;
    }

    /// <summary>The Materials of the round's purchases, bought or still wanted, that the stockpile did not cover at its start.</summary>
    public long MissingMaterials => Math.Max(0, _wantedMaterials - _stockMaterials);

    /// <summary>The Energy of the round's purchases, bought or still wanted, that the stockpile did not cover at its start.</summary>
    public long MissingEnergy => Math.Max(0, _wantedEnergy - _stockEnergy);

    /// <summary>
    /// True when what is left, less what is held back, covers a price. A resource the price has
    /// none of is not asked about: Energy held back for a research does not keep Materials from a
    /// worker or a Generator, which are what earn that Energy.
    /// </summary>
    public bool Covers(int materials, int energy) =>
        (materials <= 0 || materials <= _materials - _heldMaterials)
        && (energy <= 0 || energy <= _energy - _heldEnergy);

    /// <summary>True when what is left covers a price, held back or not: what the simulation will find.</summary>
    public bool StockCovers(int materials, int energy) => materials <= _materials && energy <= _energy;

    /// <summary>Counts a price among the purchases the workers have to earn.</summary>
    public void Want(int materials, int energy)
    {
        _wantedMaterials += materials;
        _wantedEnergy += energy;
    }

    /// <summary>Takes a price out of what is left.</summary>
    public void Pay(int materials, int energy)
    {
        _materials -= materials;
        _energy -= energy;
    }

    /// <summary>Holds a price back from everything considered after this.</summary>
    public void Hold(int materials, int energy)
    {
        _heldMaterials += materials;
        _heldEnergy += energy;
    }
}
