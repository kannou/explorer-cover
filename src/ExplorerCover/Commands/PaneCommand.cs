using System.Windows.Input;
using ExplorerCover.Core;

namespace ExplorerCover.Commands;

internal sealed class PaneCommand(CommandDispatcher dispatcher, string id, PaneState pane) : ICommand
{
    public bool CanExecute(object? parameter) => dispatcher.CanExecute(id, pane);
    public void Execute(object? parameter) => dispatcher.Execute(id, pane);
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
