using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BusDockStudio.Core.Models;
using BusDockStudio.Core.Services;
using BusDockStudio.Core.UseCases;

namespace BusDockStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IServiceBusConnectionService connectionService;
    private readonly IEntityBrowser entityBrowser;
    private readonly IMessageBrowser messageBrowser;
    private readonly ISecretStore secretStore;
    private readonly IApplicationSettings settings;
    private readonly PurgeCoordinator purgeCoordinator;
    private readonly Action<string> setTheme;
    private readonly List<long?> pageCursors = [null];
    private IReadOnlyList<EntityDescriptor> loadedEntities = [];
    private CancellationTokenSource? operationCancellation;
    private int pageIndex;

    [ObservableProperty] private string connectionName = string.Empty;
    [ObservableProperty] private string connectionString = string.Empty;
    [ObservableProperty] private ConnectionProfile? selectedConnection;
    [ObservableProperty] private EntityTreeNode? selectedNode;
    [ObservableProperty] private MessageRecord? selectedMessage;
    [ObservableProperty] private string entitySearchText = string.Empty;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private int pageSize = 50;
    [ObservableProperty] private string statusMessage = "Add a connection to begin.";
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isPurgePanelVisible;
    [ObservableProperty] private bool isPurgeAcknowledged;
    [ObservableProperty] private long purgeDeletedCount;
    [ObservableProperty] private string selectedTheme = "System";
    [ObservableProperty] private string selectedBodyView = "Formatted";

    public MainWindowViewModel(
        IServiceBusConnectionService connectionService,
        IEntityBrowser entityBrowser,
        IMessageBrowser messageBrowser,
        IMessagePurgeService purgeService,
        ISecretStore secretStore,
        IApplicationSettings settings,
        IClock clock,
        Action<string>? setTheme = null)
    {
        this.connectionService = connectionService;
        this.entityBrowser = entityBrowser;
        this.messageBrowser = messageBrowser;
        this.secretStore = secretStore;
        this.settings = settings;
        purgeCoordinator = new(purgeService, clock);
        this.setTheme = setTheme ?? (_ => { });
    }

    public ObservableCollection<ConnectionProfile> Connections { get; } = [];
    public ObservableCollection<EntityTreeNode> EntityNodes { get; } = [];
    public ObservableCollection<MessageRecord> Messages { get; } = [];
    public ObservableCollection<MessageRecord> FilteredMessages { get; } = [];
    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];
    public IReadOnlyList<string> BodyViewOptions { get; } = ["Formatted", "Text", "Base64", "Raw (hex)"];
    public string PageLabel => $"Page {pageIndex + 1}";
    public bool CanGoBack => pageIndex > 0 && !IsBusy;
    public bool HasSelectedConnection => SelectedConnection is not null;
    public bool HasSelectedSource => SelectedNode?.Source is not null;
    public bool HasSelectedMessage => SelectedMessage is not null;
    public bool HasMessages => FilteredMessages.Count > 0;
    public string MessageEmptyTitle => HasSelectedSource ? "No messages found" : "Choose an entity";
    public string MessageEmptyDescription => HasSelectedSource
        ? "This view is empty, or no messages match your search. Refresh to check again."
        : "Select an active, dead-letter, or transfer dead-letter view from the connection panel.";
    public string SelectedBody => SelectedMessage is null
        ? string.Empty
        : SelectedBodyView switch
        {
            "Base64" => SelectedMessage.Body.Base64,
            "Raw (hex)" => FormatRaw(SelectedMessage.Body.Bytes),
            _ => SelectedMessage.Body.DisplayText
        };
    public string SelectedMetadata => SelectedMessage is null
        ? string.Empty
        : string.Join(Environment.NewLine, SelectedMessage.SystemProperties.Select(item => $"{item.Key}: {item.Value}")
            .Concat(SelectedMessage.ApplicationProperties.Select(item => $"{item.Key}: {item.Value}")));
    public string PurgeTargetName => SelectedNode?.Source?.EntityName ?? string.Empty;
    public string PurgeSummary => SelectedNode?.Source is { } source && SelectedConnection is { } connection
        ? $"Namespace: {connection.FullyQualifiedNamespace}\nEntity: {source.DisplayName}\nSubqueue: {source.Subqueue}\nApproximate count: {(source.ApproximateCount?.ToString("N0", CultureInfo.CurrentCulture) ?? "unknown")}" 
        : string.Empty;

    public async Task InitializeAsync()
    {
        SelectedTheme = settings.Theme;
        setTheme(SelectedTheme);
        var connections = await settings.GetConnectionsAsync(CancellationToken.None);
        foreach (var profile in connections)
        {
            Connections.Add(profile);
        }

        SelectedConnection = Connections.FirstOrDefault();
    }

    [RelayCommand]
    private void ApplyTheme()
    {
        settings.Theme = SelectedTheme;
        setTheme(SelectedTheme);
    }

    [RelayCommand]
    private async Task AddConnectionAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(ConnectionString))
        {
            ErrorMessage = "Enter a connection string first.";
            return;
        }

        await RunBusyAsync(async token =>
        {
            StatusMessage = "Testing connection...";
            var result = await connectionService.TestAsync(ConnectionString, token);
            if (!result.IsSuccess || result.FullyQualifiedNamespace is null)
            {
                ErrorMessage = result.Message;
                StatusMessage = "Connection was not added.";
                return;
            }

            var existing = Connections.FirstOrDefault(item => string.Equals(
                item.FullyQualifiedNamespace,
                result.FullyQualifiedNamespace,
                StringComparison.OrdinalIgnoreCase));
            var profile = new ConnectionProfile(
                existing?.Id ?? Guid.NewGuid(),
                string.IsNullOrWhiteSpace(ConnectionName) ? result.FullyQualifiedNamespace : ConnectionName.Trim(),
                result.FullyQualifiedNamespace);
            await secretStore.SetAsync(profile.Id, ConnectionString, token);
            try
            {
                await settings.SaveConnectionAsync(profile, token);
            }
            catch
            {
                await secretStore.RemoveAsync(profile.Id, CancellationToken.None);
                throw;
            }

            if (existing is null)
            {
                Connections.Add(profile);
            }
            else
            {
                Connections[Connections.IndexOf(existing)] = profile;
            }

            ConnectionString = string.Empty;
            ConnectionName = string.Empty;
            SelectedConnection = profile;
            StatusMessage = existing is null
                ? "Connection saved securely in macOS Keychain."
                : "Saved connection and credential updated.";
            await LoadEntitiesCoreAsync(profile, token);
        });
    }

    [RelayCommand]
    private async Task RemoveConnectionAsync()
    {
        if (SelectedConnection is not { } profile || IsBusy)
        {
            return;
        }

        await secretStore.RemoveAsync(profile.Id, CancellationToken.None);
        await settings.RemoveConnectionAsync(profile.Id, CancellationToken.None);
        Connections.Remove(profile);
        SelectedConnection = null;
        loadedEntities = [];
        EntitySearchText = string.Empty;
        EntityNodes.Clear();
        ClearMessages();
        StatusMessage = "Connection removed.";
    }

    [RelayCommand]
    private async Task LoadEntitiesAsync()
    {
        if (SelectedConnection is not { } profile || IsBusy)
        {
            return;
        }

        await RunBusyAsync(token => LoadEntitiesCoreAsync(profile, token));
    }

    [RelayCommand]
    private async Task RefreshMessagesAsync()
    {
        if (SelectedNode?.Source is null || SelectedConnection is null || IsBusy)
        {
            return;
        }

        await LoadPageAsync(pageCursors[pageIndex]);
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (SelectedNode?.Source is null || Messages.Count == 0 || IsBusy)
        {
            return;
        }

        var next = Messages[^1].SequenceNumber + 1;
        if (pageIndex == pageCursors.Count - 1)
        {
            pageCursors.Add(next);
        }

        pageIndex++;
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(CanGoBack));
        await LoadPageAsync(pageCursors[pageIndex]);
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (pageIndex == 0 || IsBusy)
        {
            return;
        }

        pageIndex--;
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(CanGoBack));
        await LoadPageAsync(pageCursors[pageIndex]);
    }

    [RelayCommand]
    private void OpenPurge()
    {
        if (SelectedNode?.Source is null)
        {
            return;
        }

        IsPurgeAcknowledged = false;
        IsPurgePanelVisible = true;
        OnPropertyChanged(nameof(PurgeTargetName));
        OnPropertyChanged(nameof(PurgeSummary));
    }

    [RelayCommand]
    private void ClosePurge()
    {
        IsPurgePanelVisible = false;
        IsPurgeAcknowledged = false;
    }

    [RelayCommand]
    private async Task ConfirmPurgeAsync()
    {
        if (SelectedNode?.Source is not { } source || SelectedConnection is not { } connection || IsBusy)
        {
            return;
        }

        if (!IsPurgeAcknowledged)
        {
            ErrorMessage = "Acknowledge the permanent deletion before continuing.";
            return;
        }

        var secret = await secretStore.GetAsync(connection.Id, CancellationToken.None);
        if (secret is null)
        {
            ErrorMessage = "The in-memory credential is unavailable. Add the connection again.";
            return;
        }

        operationCancellation = new();
        IsBusy = true;
        ErrorMessage = string.Empty;
        PurgeDeletedCount = 0;
        try
        {
            var progress = new Progress<PurgeProgress>(value =>
            {
                PurgeDeletedCount = value.DeletedCount;
                StatusMessage = value.Phase;
            });
            var request = new PurgeRequest(source, connection.FullyQualifiedNamespace, source.EntityName, source.ApproximateCount, TimeSpan.FromMinutes(5));
            var result = await purgeCoordinator.ExecuteAsync(secret, request, progress, operationCancellation.Token);
            PurgeDeletedCount = result.DeletedCount;
            StatusMessage = result.Message;
            if (result.Status is PurgeStatus.Failed or PurgeStatus.Rejected or PurgeStatus.TimedOut)
            {
                ErrorMessage = result.Message;
            }

            if (result.Status == PurgeStatus.Succeeded)
            {
                IsPurgePanelVisible = false;
                IsPurgeAcknowledged = false;
                var entities = await entityBrowser.BrowseAsync(secret, CancellationToken.None);
                loadedEntities = entities;
                BuildTree(entities);
                ExpandPathToSource(EntityNodes, source);
                SelectedNode = FindSourceNode(EntityNodes, source);
                if (SelectedNode?.Source is { } refreshedSource)
                {
                    await LoadPageCoreAsync(secret, refreshedSource, pageCursors[pageIndex], CancellationToken.None);
                }

                StatusMessage = result.Message;
            }
        }
        finally
        {
            operationCancellation.Dispose();
            operationCancellation = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelOperation() => operationCancellation?.Cancel();

    partial void OnSelectedConnectionChanged(ConnectionProfile? value)
    {
        EntityNodes.Clear();
        loadedEntities = [];
        EntitySearchText = string.Empty;
        ClearMessages();
        IsPurgePanelVisible = false;
        IsPurgeAcknowledged = false;
        OnPropertyChanged(nameof(HasSelectedConnection));
        if (value is not null)
        {
            LoadEntitiesCommand.Execute(null);
        }
    }

    partial void OnSelectedNodeChanged(EntityTreeNode? value)
    {
        pageIndex = 0;
        pageCursors.Clear();
        pageCursors.Add(null);
        ClearMessages();
        IsPurgePanelVisible = false;
        IsPurgeAcknowledged = false;
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(HasSelectedSource));
        OnPropertyChanged(nameof(MessageEmptyTitle));
        OnPropertyChanged(nameof(MessageEmptyDescription));
        OnPropertyChanged(nameof(PurgeTargetName));
        OnPropertyChanged(nameof(PurgeSummary));
        if (value?.Source is not null)
        {
            RefreshMessagesCommand.Execute(null);
        }
    }

    partial void OnSelectedMessageChanged(MessageRecord? value)
    {
        OnPropertyChanged(nameof(HasSelectedMessage));
        OnPropertyChanged(nameof(SelectedBody));
        OnPropertyChanged(nameof(SelectedMetadata));
    }

    partial void OnSelectedBodyViewChanged(string value) => OnPropertyChanged(nameof(SelectedBody));
    partial void OnEntitySearchTextChanged(string value)
    {
        SelectedNode = null;
        BuildTree(loadedEntities);
    }
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnPageSizeChanged(int value)
    {
        PageSize = Math.Clamp(value, 1, 250);
    }

    private async Task LoadEntitiesCoreAsync(ConnectionProfile profile, CancellationToken token)
    {
        var secret = await secretStore.GetAsync(profile.Id, token)
            ?? throw new InvalidOperationException("The credential is unavailable.");
        StatusMessage = "Loading queues, topics, and subscriptions...";
        var entities = await entityBrowser.BrowseAsync(secret, token);
        loadedEntities = entities;
        BuildTree(entities);
        StatusMessage = entities.Count == 0 ? "No entities found." : $"Loaded {entities.Count:N0} entities.";
    }

    private async Task LoadPageAsync(long? cursor)
    {
        if (SelectedConnection is not { } connection || SelectedNode?.Source is not { } source)
        {
            return;
        }

        await RunBusyAsync(async token =>
        {
            var secret = await secretStore.GetAsync(connection.Id, token)
                ?? throw new InvalidOperationException("The credential is unavailable.");
            await LoadPageCoreAsync(secret, source, cursor, token);
        });
    }

    private async Task LoadPageCoreAsync(string secret, MessageSource source, long? cursor, CancellationToken token)
    {
        StatusMessage = "Peeking messages (read-only)...";
        var page = await messageBrowser.PeekAsync(secret, new(source, PageSize, cursor), token);
        Messages.Clear();
        foreach (var message in page.Messages)
        {
            Messages.Add(message);
        }

        ApplyFilter();
        SelectedMessage = FilteredMessages.FirstOrDefault();
        StatusMessage = Messages.Count == 0
            ? "No messages found in this view."
            : $"Showing {Messages.Count:N0} read-only peek results.";
    }

    private void BuildTree(IReadOnlyList<EntityDescriptor> entities)
    {
        var expandedNodes = CaptureExpandedNodes(EntityNodes);
        EntityNodes.Clear();
        var query = EntitySearchText.Trim();
        var allQueues = entities
            .Where(item => item.Kind == EntityKind.Queue)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var queues = allQueues
            .Where(item => MatchesEntitySearch(item.Name, query))
            .ToArray();
        var queueGroup = new EntityTreeNode($"Queues ({queues.Length:N0})", isGroup: true);
        foreach (var queue in queues)
        {
            var node = new EntityTreeNode(queue.Name);
            AddSubqueues(node, queue.Name, EntityKind.Queue, null, queue.IsPartitioned,
                queue.ApproximateActiveCount, queue.ApproximateDeadLetterCount,
                queue.ApproximateTransferDeadLetterCount);
            queueGroup.Children.Add(node);
        }
        EntityNodes.Add(queueGroup);

        var allTopics = entities
            .Where(item => item.Kind == EntityKind.Topic)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var matchingTopics = new List<(EntityDescriptor Topic, EntityDescriptor[] Subscriptions)>();
        foreach (var topic in allTopics)
        {
            var subscriptions = entities
                .Where(item => item.Kind == EntityKind.Subscription && item.ParentName == topic.Name)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var topicMatches = MatchesEntitySearch(topic.Name, query);
            var matchingSubscriptions = topicMatches
                ? subscriptions
                : subscriptions.Where(item => MatchesEntitySearch(item.Name, query)).ToArray();
            if (topicMatches || matchingSubscriptions.Length > 0)
            {
                matchingTopics.Add((topic, matchingSubscriptions));
            }
        }

        var topicGroup = new EntityTreeNode($"Topics ({matchingTopics.Count:N0})", isGroup: true);
        foreach (var (topic, subscriptions) in matchingTopics)
        {
            var topicNode = new EntityTreeNode(topic.Name);
            foreach (var subscription in subscriptions)
            {
                var subscriptionNode = new EntityTreeNode(subscription.Name);
                AddSubqueues(subscriptionNode, subscription.Name, EntityKind.Subscription, topic.Name, subscription.IsPartitioned,
                    subscription.ApproximateActiveCount, subscription.ApproximateDeadLetterCount,
                    subscription.ApproximateTransferDeadLetterCount);
                topicNode.Children.Add(subscriptionNode);
            }
            topicGroup.Children.Add(topicNode);
        }
        EntityNodes.Add(topicGroup);
        RestoreExpandedNodes(EntityNodes, expandedNodes);
    }

    private static bool MatchesEntitySearch(string value, string query) =>
        string.IsNullOrEmpty(query) || value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static void AddSubqueues(
        EntityTreeNode parent,
        string entityName,
        EntityKind kind,
        string? topicName,
        bool partitioned,
        long? activeCount,
        long? deadLetterCount,
        long? transferDeadLetterCount)
    {
        parent.Children.Add(new($"Active ({FormatCount(activeCount)})",
            new(entityName, kind, topicName, MessageSubqueue.Active, partitioned, activeCount)));
        parent.Children.Add(new($"Dead-letter ({FormatCount(deadLetterCount)})",
            new(entityName, kind, topicName, MessageSubqueue.DeadLetter, partitioned, deadLetterCount)));
        parent.Children.Add(new($"Transfer dead-letter ({FormatCount(transferDeadLetterCount)})",
            new(entityName, kind, topicName, MessageSubqueue.TransferDeadLetter, partitioned, transferDeadLetterCount)));
    }

    private static string FormatCount(long? count) =>
        count?.ToString("N0", CultureInfo.CurrentCulture) ?? "?";

    private static EntityTreeNode? FindSourceNode(
        IEnumerable<EntityTreeNode> nodes,
        MessageSource target)
    {
        foreach (var node in nodes)
        {
            if (node.Source is { } source &&
                source.EntityName == target.EntityName &&
                source.EntityKind == target.EntityKind &&
                source.TopicName == target.TopicName &&
                source.Subqueue == target.Subqueue)
            {
                return node;
            }

            if (FindSourceNode(node.Children, target) is { } child)
            {
                return child;
            }
        }

        return null;
    }

    private static bool ExpandPathToSource(
        IEnumerable<EntityTreeNode> nodes,
        MessageSource target)
    {
        foreach (var node in nodes)
        {
            if (node.Source is { } source && IsSameSource(source, target))
            {
                return true;
            }

            if (ExpandPathToSource(node.Children, target))
            {
                node.IsExpanded = true;
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> CaptureExpandedNodes(IEnumerable<EntityTreeNode> nodes)
    {
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        CaptureExpandedNodes(nodes, string.Empty, expanded);
        return expanded;
    }

    private static void CaptureExpandedNodes(
        IEnumerable<EntityTreeNode> nodes,
        string parentPath,
        ISet<string> expanded)
    {
        foreach (var node in nodes)
        {
            var path = NodePath(parentPath, node);
            if (node.IsExpanded)
            {
                expanded.Add(path);
            }

            CaptureExpandedNodes(node.Children, path, expanded);
        }
    }

    private static void RestoreExpandedNodes(
        IEnumerable<EntityTreeNode> nodes,
        IReadOnlySet<string> expanded,
        string parentPath = "")
    {
        foreach (var node in nodes)
        {
            var path = NodePath(parentPath, node);
            node.IsExpanded = expanded.Contains(path);
            RestoreExpandedNodes(node.Children, expanded, path);
        }
    }

    private static string NodePath(string parentPath, EntityTreeNode node)
    {
        var segment = node.Source is { } source
            ? $"source:{source.EntityKind}:{source.TopicName}:{source.EntityName}:{source.Subqueue}"
            : node.IsGroup
                ? $"group:{node.Title.Split(' ', 2)[0]}"
                : $"node:{node.Title}";
        return $"{parentPath}/{segment}";
    }

    private static bool IsSameSource(MessageSource source, MessageSource target) =>
        source.EntityName == target.EntityName &&
        source.EntityKind == target.EntityKind &&
        source.TopicName == target.TopicName &&
        source.Subqueue == target.Subqueue;

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        FilteredMessages.Clear();
        foreach (var message in Messages.Where(message => string.IsNullOrEmpty(query)
                     || message.MessageId.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || (message.Subject?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                     || message.SequenceNumber.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredMessages.Add(message);
        }

        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(MessageEmptyTitle));
        OnPropertyChanged(nameof(MessageEmptyDescription));
    }

    private void ClearMessages()
    {
        Messages.Clear();
        FilteredMessages.Clear();
        SelectedMessage = null;
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(MessageEmptyTitle));
        OnPropertyChanged(nameof(MessageEmptyDescription));
    }

    private static string FormatRaw(byte[] bytes)
    {
        const int maxBytes = 500_000;
        var length = Math.Min(bytes.Length, maxBytes);
        var value = Convert.ToHexString(bytes.AsSpan(0, length));
        return length == bytes.Length
            ? value
            : $"{value}\n\n[Raw display truncated at {length:N0} of {bytes.Length:N0} bytes]";
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> operation)
    {
        operationCancellation = new();
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            await operation(operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Operation failed ({exception.GetType().Name}). No credentials were logged.";
            StatusMessage = "An error occurred.";
        }
        finally
        {
            operationCancellation.Dispose();
            operationCancellation = null;
            IsBusy = false;
            OnPropertyChanged(nameof(CanGoBack));
        }
    }

    public void Dispose()
    {
        operationCancellation?.Cancel();
        operationCancellation?.Dispose();
        purgeCoordinator.Dispose();
        if (secretStore is IDisposable disposable)
        {
            disposable.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
