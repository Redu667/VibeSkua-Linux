using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using Skua.Core.Options;

namespace Skua.Core.ViewModels;

public partial class OptionContainerItemViewModel : ObservableObject
{
    public OptionContainerItemViewModel(IOptionContainer container, IOption option)
    {
        Container = container;
        Option = option;
        Type = option.Type;
        Category = option.Category;
        if (Type.IsEnum)
        {
            string[] enumNames = Enum.GetNames(Type);
            EnumValues = new List<string>(enumNames.Length);
            foreach (string name in enumNames)
                EnumValues.Add(name.Replace('_', ' '));
            SelectedValue = GetValue().ToString()!.Replace('_', ' ');
            return;
        }
        _value = GetValue();
    }

    [ObservableProperty]
    private object _value;

    [ObservableProperty]
    private List<string>? _enumValues;

    [ObservableProperty]
    private string? _selectedValue;

    public IOptionContainer Container { get; }
    public IOption Option { get; }
    public Type Type { get; }
    public string Category { get; }

    // Editor selectors + typed accessors for framework-agnostic views (the
    // Avalonia option dialog binds these; WPF uses its template selector and
    // ignores them). Type is bool, an enum, or otherwise treated as text.
    public bool IsBoolean => Type == typeof(bool);
    public bool IsEnum => Type.IsEnum;
    public bool IsText => !IsBoolean && !IsEnum;

    /// <summary>Two-way bool accessor over <see cref="Value"/> for checkbox binding.</summary>
    public bool BoolValue
    {
        get => Value is bool b ? b : (Value?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false);
        set => Value = value;
    }

    /// <summary>Two-way string accessor over <see cref="Value"/> for text binding.</summary>
    public string TextValue
    {
        get => Value?.ToString() ?? string.Empty;
        set => Value = value;
    }

    private object GetValue()
    {
        object value = typeof(OptionContainer).GetMethod("Get", new Type[] { typeof(IOption) })?
                .MakeGenericMethod(new Type[] { Option.Type })
                .Invoke(Container, new object[] { Option }) ?? string.Empty;
        return value;
    }
}

