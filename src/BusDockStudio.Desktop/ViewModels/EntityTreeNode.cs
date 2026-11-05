using System.Collections.ObjectModel;
using BusDockStudio.Core.Models;

namespace BusDockStudio.Desktop.ViewModels;

public sealed class EntityTreeNode(string title, MessageSource? source = null, bool isGroup = false)
{
    public string Title { get; } = title;
    public MessageSource? Source { get; } = source;
    public bool IsGroup { get; } = isGroup;
    public double FontSize => IsGroup ? 13 : 12;
    public bool IsExpanded { get; set; }
    public ObservableCollection<EntityTreeNode> Children { get; } = [];
    public bool IsMessageSource => Source is not null;
}
