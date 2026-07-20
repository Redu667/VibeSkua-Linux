using System.Globalization;
using Newtonsoft.Json;

namespace Skua.Core.Models.Converters;

public class IntConverter : JsonConverter<int>
{
    public override void WriteJson(JsonWriter writer, int value, JsonSerializer serializer)
    {
        writer.WriteValue(value);
    }

    public override int ReadJson(JsonReader reader, Type objectType, int existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.Value is null or (object)"null")
            return 1;

        string s = reader.Value.ToString()!;
        if (int.TryParse(s, out int result))
            return result;
        // Tolerate Ruffle's ".0"-formatted numbers (AS3 Number fields serialize
        // as e.g. "5.0", which int.TryParse rejects) and other numeric forms.
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d))
            return (int)Math.Truncate(d);
        return 1;
    }
}