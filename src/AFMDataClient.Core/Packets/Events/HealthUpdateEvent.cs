using Albion.Network;
using AlbionDataAvalonia.Combat.Models;

namespace AlbionDataAvalonia.Network.Events;

public class HealthUpdateEvent : BaseEvent
{
    public long AffectedObjectId { get; }
    public long CauserId => SourceObjectId ?? 0;
    public double HealthChange { get; }
    public double NewHealthValue => ResultingHealth ?? double.NaN;
    public int CausingSpellIndex => SpellIndex ?? 0;
    public long? GameTimeMilliseconds { get; }
    public bool IsValid { get; }
    public PacketFieldState NewHealthState { get; }
    public double? ResultingHealth { get; }
    public long? SourceObjectId { get; }
    public int? SpellIndex { get; }

    public HealthUpdateEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        var target = ActivityPacketValues.Long(parameters, 0);
        var delta = ActivityPacketValues.Double(parameters, 2);
        var resulting = ActivityPacketValues.Double(parameters, 3);
        AffectedObjectId = target.Value;
        HealthChange = delta.Value;
        IsValid = target.State == PacketFieldState.Valid && target.Value > 0 && delta.State == PacketFieldState.Valid;
        NewHealthState = resulting.State;
        ResultingHealth = resulting.Optional;
        SourceObjectId = ActivityPacketValues.Long(parameters, 6).Optional;
        var spell = ActivityPacketValues.Long(parameters, 7).Optional;
        SpellIndex = spell is >= 0 and <= int.MaxValue ? (int)spell.Value : null;
        GameTimeMilliseconds = ActivityPacketValues.Long(parameters, 1).Optional;
    }

    public bool TryNormalize(out CombatHealthEvent healthEvent)
    {
        healthEvent = default!;
        return IsValid && CombatHealthEvent.TryCreate(CauserId, AffectedObjectId, HealthChange,
            NewHealthValue, GameTimeMilliseconds, out healthEvent);
    }
}
