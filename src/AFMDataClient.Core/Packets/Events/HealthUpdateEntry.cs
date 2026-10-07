namespace AlbionDataAvalonia.Network.Events;

/// <summary>One signed, unrounded change at its original sparse batch index.</summary>
public sealed record HealthUpdateEntry(long AffectedObjectId, long CauserId, double HealthChange,
    double NewHealthValue, int CausingSpellIndex, long? GameTimeMilliseconds)
{
    public int EntryIndex { get; init; }
    public bool IsValid => AffectedObjectId > 0 && double.IsFinite(HealthChange);
    public PacketFieldState NewHealthState { get; init; }
    public double? ResultingHealth { get; init; }
    public long? SourceObjectId { get; init; }
    public int? SpellIndex { get; init; }
}
