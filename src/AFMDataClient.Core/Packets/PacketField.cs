using System.Collections;
using System.Globalization;

namespace AlbionDataAvalonia.Network.Events;

/// <summary>Distinguishes omitted optional fields from malformed values.</summary>
public enum PacketFieldState { Missing, Valid, Invalid }

internal readonly record struct PacketField<T>(PacketFieldState State, T Value) where T : struct
{
    public T? Optional => State == PacketFieldState.Valid ? Value : null;
}

internal static class ActivityPacketValues
{
    public static PacketField<long> Long(IReadOnlyDictionary<byte, object> values, byte key) => Read(values, key, Long);
    public static PacketField<double> Double(IReadOnlyDictionary<byte, object> values, byte key) => Read(values, key, Double);
    public static PacketField<bool> Boolean(IReadOnlyDictionary<byte, object> values, byte key) => Read(values, key, Boolean);

    public static PacketField<long> Long(object? value)
    {
        try
        {
            return value switch
            {
                byte or sbyte or short or ushort or int or uint or long => new(PacketFieldState.Valid, Convert.ToInt64(value, CultureInfo.InvariantCulture)),
                ulong number when number <= long.MaxValue => new(PacketFieldState.Valid, (long)number),
                _ => new(PacketFieldState.Invalid, default)
            };
        }
        catch (OverflowException) { return new(PacketFieldState.Invalid, default); }
    }

    public static PacketField<double> Double(object? value)
    {
        if (value is not (byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal))
            return new(PacketFieldState.Invalid, default);
        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return double.IsFinite(number) ? new(PacketFieldState.Valid, number) : new(PacketFieldState.Invalid, default);
    }

    public static PacketField<bool> Boolean(object? value) => value is bool boolean
        ? new(PacketFieldState.Valid, boolean) : new(PacketFieldState.Invalid, default);

    public static string Text(IReadOnlyDictionary<byte, object> values, byte key) =>
        values.TryGetValue(key, out var raw) && raw is string text ? text[..Math.Min(text.Length, 200)] : string.Empty;

    public static Dictionary<int, object?> Indexed(IReadOnlyDictionary<byte, object> values, byte key)
    {
        var result = new Dictionary<int, object?>();
        if (!values.TryGetValue(key, out var raw)) return result;
        if (raw is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                var index = Long(entry.Key).Optional;
                if (index is >= 0 and <= 65535 && result.Count < 65536) result[(int)index.Value] = entry.Value;
            }
        }
        else if (raw is IEnumerable enumerable && raw is not string)
        {
            var index = 0;
            foreach (var item in enumerable)
            {
                if (index >= 65536) break;
                result[index++] = item;
            }
        }
        else result[0] = raw;
        return result;
    }

    public static PacketField<T> At<T>(Dictionary<int, object?> values, int index, Func<object?, PacketField<T>> reader) where T : struct =>
        values.TryGetValue(index, out var raw) ? reader(raw) : new(PacketFieldState.Missing, default);

    private static PacketField<T> Read<T>(IReadOnlyDictionary<byte, object> values, byte key, Func<object?, PacketField<T>> reader) where T : struct =>
        values.TryGetValue(key, out var raw) ? reader(raw) : new(PacketFieldState.Missing, default);
}
