using System.Windows;
using Microsoft.Win32;

namespace Inklane;

/// <summary>The document lifecycle's user decisions, separated from persistence and state changes.</summary>
public interface IDocumentInteraction
{
    int Choose(Window owner, string title, string message, params string[] buttons);
    string? ChooseSavePath(Window owner, SaveFileDialog dialog);
    void ShowMessage(Window owner, string message, string title, MessageBoxImage icon = MessageBoxImage.Information);
}

internal sealed class DefaultDocumentInteraction : IDocumentInteraction
{
    public int Choose(Window owner, string title, string message, params string[] buttons)
        => Dialogs.Choose(owner, title, message, buttons);

    public string? ChooseSavePath(Window owner, SaveFileDialog dialog)
        => dialog.ShowDialog(owner) == true ? dialog.FileName : null;

    public void ShowMessage(Window owner, string message, string title, MessageBoxImage icon = MessageBoxImage.Information)
        => MessageBox.Show(owner, message, title, MessageBoxButton.OK, icon);
}
