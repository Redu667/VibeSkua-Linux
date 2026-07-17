using Microsoft.Xaml.Behaviors;
using System.Windows.Controls;
using System.Windows.Input;

namespace Skua.WPF;

public class TextBoxOnlyFloatingPointBehavior : Behavior<TextBox>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PreviewTextInput += AssociatedObject_PreviewTextInput;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.PreviewTextInput -= AssociatedObject_PreviewTextInput;
        base.OnDetaching();
    }

    private void AssociatedObject_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (char c in e.Text)
        {
            if (!char.IsDigit(c) && c != '.')
            {
                e.Handled = true;
                return;
            }
        }
    }
}
