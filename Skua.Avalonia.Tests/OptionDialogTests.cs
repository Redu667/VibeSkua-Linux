using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Skua.Avalonia.Services;
using Skua.Avalonia.Views;
using Skua.Core.Options;
using Skua.Core.ViewModels;
using Xunit;

namespace Skua.Avalonia.Tests;

/// <summary>
/// The script-options dialog (OptionContainerViewModel). Every configurable
/// script calls OptionContainer.Configure() -> ShowDialog&lt;OptionContainerViewModel&gt;;
/// before this view existed the ViewLocator fell back to "View not found" and
/// the script errored on start. These verify the view resolves and renders a
/// type-appropriate editor per option.
/// </summary>
public class OptionDialogTests
{
    private static OptionContainerViewModel BuildViewModel()
    {
        // A real OptionContainer (only needs an IDialogService) with one option
        // of each editor kind: bool -> checkbox, enum -> combo, string -> text.
        OptionContainer container = new(new DialogService());
        container.Options.Add(new Option<bool>("flag", "Enable flag", "A boolean option", false));
        container.Options.Add(new Option<string>("name", "Name", "A string option", "hello"));
        container.Options.Add(new Option<System.DayOfWeek>("day", "Day", "An enum option", System.DayOfWeek.Monday));
        return new OptionContainerViewModel(container);
    }

    [AvaloniaFact]
    public void OptionContainerView_resolves_and_renders_typed_editors()
    {
        OptionContainerViewModel vm = BuildViewModel();
        HostDialogWindow host = new() { DataContext = vm };
        host.Show();
        Dispatcher.UIThread.RunJobs();

        // The view resolved (no ViewLocator "View not found" fallback).
        Assert.NotNull(host.GetVisualDescendants().OfType<OptionContainerView>().FirstOrDefault());

        // One editor of each kind is present and visible.
        var checkboxes = host.GetVisualDescendants().OfType<CheckBox>().Where(c => c.IsVisible).ToList();
        var combos = host.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsVisible).ToList();
        var textboxes = host.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsVisible).ToList();
        Assert.NotEmpty(checkboxes);
        Assert.NotEmpty(combos);
        Assert.NotEmpty(textboxes);

        host.Close();
    }

    [AvaloniaFact]
    public void Option_items_expose_type_selectors_and_typed_accessors()
    {
        OptionContainerViewModel vm = BuildViewModel();

        OptionContainerItemViewModel flag = vm.Options.First(o => o.Option.Name == "flag");
        OptionContainerItemViewModel name = vm.Options.First(o => o.Option.Name == "name");
        OptionContainerItemViewModel day = vm.Options.First(o => o.Option.Name == "day");

        Assert.True(flag.IsBoolean);
        Assert.True(name.IsText);
        Assert.True(day.IsEnum);

        // Typed accessors round-trip into Value for two-way binding.
        flag.BoolValue = true;
        Assert.Equal(true, flag.Value);
        name.TextValue = "world";
        Assert.Equal("world", name.Value);
    }
}
