using System.Collections.Generic;
using Newtonsoft.Json;
using Skua.Core.Models.Converters;
using Skua.Core.Models.Items;
using Xunit;

namespace Skua.Avalonia.Tests;

/// <summary>
/// Regression tests for the Ruffle number-formatting break: AS3 <c>Number</c>
/// fields (e.g. <c>CharItemID</c>) come across from Ruffle as <c>685089465.0</c>
/// (trailing <c>.0</c>) where Windows Flash emitted a bare integer. The stock
/// JSON.NET integer reader throws on that, and because the game-object readers
/// in <c>IFlashUtil</c> swallow the exception and return the default, a full
/// 96-item inventory silently deserialized to an empty list — so scripts saw
/// "you own nothing" and re-bought items. <see cref="GameObjectJson.Settings"/>
/// (with <see cref="FlexibleIntegerConverter"/>) is what the readers use.
/// </summary>
public class InventoryDeserializationTests
{
    // A faithful slice of one item as Ruffle serializes it: note CharItemID,
    // ItemID and iQty all carry the trailing ".0" that used to blow up the parse.
    private const string RuffleItemsJson =
        "[{\"ItemID\":12341.0,\"sName\":\"Battle Oracle Hood\",\"iQty\":1.0," +
        "\"CharItemID\":685089465.0,\"bEquip\":\"0\",\"iLvl\":50.0,\"EnhLvl\":10.0," +
        "\"iCost\":100,\"sType\":\"Helm\",\"bCoins\":false,\"ProcID\":3.0}]";

    [Fact]
    public void Deserializes_ruffle_dotzero_integers_instead_of_blanking_the_list()
    {
        var items = JsonConvert.DeserializeObject<List<InventoryItem>>(RuffleItemsJson, GameObjectJson.Settings);

        Assert.NotNull(items);
        Assert.Single(items);
        InventoryItem hood = items![0];
        Assert.Equal(685089465, hood.CharItemID);
        Assert.Equal(12341, hood.ID);
        Assert.Equal("Battle Oracle Hood", hood.Name);
        Assert.Equal(1, hood.Quantity);
        Assert.Equal(50, hood.Level);
        Assert.Equal(10, hood.EnhancementLevel);
        Assert.Equal(3, hood.ProcID); // the [JsonConverter(IntConverter)] field
    }

    [Fact]
    public void Stock_settings_still_throw_on_dotzero_proving_the_fix_is_what_helps()
    {
        // Without the converter this is exactly the failure the user hit.
        Assert.ThrowsAny<JsonException>(() =>
            JsonConvert.DeserializeObject<List<InventoryItem>>(RuffleItemsJson));
    }

    [Theory]
    [InlineData("685089465.0", 685089465)]
    [InlineData("100", 100)]
    [InlineData("0", 0)]
    [InlineData("\"42\"", 42)]      // stringified number
    [InlineData("\"42.0\"", 42)]    // stringified ".0" number
    [InlineData("-7.0", -7)]
    public void FlexibleIntegerConverter_coerces_numeric_forms(string jsonValue, int expected)
    {
        string json = $"{{\"ItemID\":{jsonValue},\"sName\":\"x\"}}";
        var item = JsonConvert.DeserializeObject<InventoryItem>(json, GameObjectJson.Settings);
        Assert.NotNull(item);
        Assert.Equal(expected, item!.ID);
    }

    [Fact]
    public void Contains_finds_the_item_after_the_fix()
    {
        // Mirrors Bot.Inventory.Contains(...) which iterates the deserialized list.
        var items = JsonConvert.DeserializeObject<List<InventoryItem>>(RuffleItemsJson, GameObjectJson.Settings)!;
        Assert.Contains(items, i => i.Name == "Battle Oracle Hood");
    }
}
