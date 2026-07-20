using System.Globalization;
using Newtonsoft.Json;

namespace Skua.Core.Models.Converters;

/// <summary>
/// Deserializes integer-typed fields tolerantly. Windows Flash serialized
/// AS3 numbers as bare integers (<c>685089465</c>), but Ruffle's AVM2 formats
/// integral <c>Number</c> fields with a trailing <c>.0</c> (<c>685089465.0</c>).
/// The stock JSON.NET integer reader rejects that, throwing mid-object — and
/// because <see cref="Skua.Core.Interfaces"/>'s game-object readers swallow the
/// exception and return the default, a whole object silently collapses (e.g. a
/// 96-item inventory deserializes to an empty list, so scripts think you own
/// nothing and re-buy items you already have).
/// <para>
/// This converter accepts integers, floats (truncated), booleans, and numeric
/// strings, coercing them to the target integer type and clamping to its range.
/// It is read-only (<see cref="CanWrite"/> is <c>false</c>), so serialization —
/// e.g. <c>SetGameObject</c> — is unaffected.
/// </para>
/// </summary>
public sealed class FlexibleIntegerConverter : JsonConverter
{
    public static readonly FlexibleIntegerConverter Instance = new();

    public override bool CanWrite => false;

    public override bool CanConvert(Type objectType)
    {
        Type t = Nullable.GetUnderlyingType(objectType) ?? objectType;
        return t == typeof(int) || t == typeof(long) || t == typeof(short)
            || t == typeof(byte) || t == typeof(sbyte) || t == typeof(uint)
            || t == typeof(ulong) || t == typeof(ushort);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotSupportedException($"{nameof(FlexibleIntegerConverter)} is read-only.");

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        Type t = Nullable.GetUnderlyingType(objectType) ?? objectType;
        bool nullable = Nullable.GetUnderlyingType(objectType) is not null;

        switch (reader.TokenType)
        {
            case JsonToken.Integer:
            case JsonToken.Float:
                return Coerce(t, Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture));
            case JsonToken.Boolean:
                return Coerce(t, (bool)reader.Value! ? 1m : 0m);
            case JsonToken.String:
                string s = ((string)reader.Value!).Trim();
                if (s.Length != 0 && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d))
                    return Coerce(t, d);
                return nullable ? null : Activator.CreateInstance(t);
            case JsonToken.Null:
            default:
                return nullable ? null : Activator.CreateInstance(t);
        }
    }

    private static object Coerce(Type t, decimal value)
    {
        decimal v = Math.Truncate(value);
        if (t == typeof(int)) return (int)Clamp(v, int.MinValue, int.MaxValue);
        if (t == typeof(long)) return (long)Clamp(v, long.MinValue, long.MaxValue);
        if (t == typeof(short)) return (short)Clamp(v, short.MinValue, short.MaxValue);
        if (t == typeof(byte)) return (byte)Clamp(v, byte.MinValue, byte.MaxValue);
        if (t == typeof(sbyte)) return (sbyte)Clamp(v, sbyte.MinValue, sbyte.MaxValue);
        if (t == typeof(uint)) return (uint)Clamp(v, uint.MinValue, uint.MaxValue);
        if (t == typeof(ulong)) return (ulong)Clamp(v, ulong.MinValue, ulong.MaxValue);
        if (t == typeof(ushort)) return (ushort)Clamp(v, ushort.MinValue, ushort.MaxValue);
        return Convert.ChangeType(v, t, CultureInfo.InvariantCulture);
    }

    private static decimal Clamp(decimal v, decimal min, decimal max)
        => v < min ? min : v > max ? max : v;
}
