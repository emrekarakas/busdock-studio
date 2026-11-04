using System.Collections.ObjectModel;
using BusDockStudio.Core.Models;

namespace BusDockStudio.Desktop.ViewModels;

public sealed class EntityTreeNode(string title, MessageSource? source = null)
{
    public string Title { get; } = title;
    public MessageSource? Source { get; } = source;
    public ObservableCollection<EntityTreeNode> Children { get; } = [];
    public bool IsMessageSource => Source is not null;
}
