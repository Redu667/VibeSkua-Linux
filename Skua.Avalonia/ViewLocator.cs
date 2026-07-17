using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Skua.Avalonia;

/// <summary>
/// Resolves a View for a given ViewModel by naming convention, mapping
/// <c>Skua.Core.ViewModels.*ViewModel</c> (and <c>Skua.Avalonia.ViewModels.*ViewModel</c>)
/// to <c>Skua.Avalonia.Views.*View</c>. This is what lets a portable Skua.Core
/// ViewModel be dropped into any <c>ContentControl</c> and get its Avalonia view.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        Type vmType = param.GetType();

        // 1) Namespace-preserving convention:
        //    Skua.Core.ViewModels[.Sub].FooViewModel -> Skua.Avalonia.Views[.Sub].FooView
        string convention = vmType.FullName!
            .Replace("Skua.Core.ViewModels", "Skua.Avalonia.Views", StringComparison.Ordinal)
            .Replace("Skua.Avalonia.ViewModels", "Skua.Avalonia.Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        // 2) Flat fallback: Skua.Avalonia.Views.FooView (views for VMs that live
        //    in Skua.Core sub-namespaces but whose views are kept flat).
        string flat = "Skua.Avalonia.Views." +
            vmType.Name.Replace("ViewModel", "View", StringComparison.Ordinal);

        string assembly = typeof(ViewLocator).Assembly.GetName().Name!;
        foreach (string candidate in new[] { convention, flat })
        {
            Type? type = Type.GetType(candidate)
                ?? Type.GetType($"{candidate}, {assembly}");
            if (type is not null)
                return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = $"View not found for {vmType.Name}" };
    }

    public bool Match(object? data) => data is ObservableObject;
}
