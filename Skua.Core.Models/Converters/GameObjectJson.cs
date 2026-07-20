using Newtonsoft.Json;

namespace Skua.Core.Models.Converters;

/// <summary>
/// Shared JSON settings for deserializing values read out of the running game
/// (every <c>GetGameObject</c>/<c>CallGameFunction</c>/array read in
/// <c>IFlashUtil</c>). Adds <see cref="FlexibleIntegerConverter"/> so integer
/// fields survive Ruffle's <c>.0</c>-formatted numbers instead of throwing and
/// silently blanking the whole object.
/// </summary>
public static class GameObjectJson
{
    /// <summary>Deserialization settings for game-object JSON. Reuse the single
    /// instance — JSON.NET settings are safe to share across threads for reads.</summary>
    public static readonly JsonSerializerSettings Settings = new()
    {
        Converters = { FlexibleIntegerConverter.Instance },
    };
}
