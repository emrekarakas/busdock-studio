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
    private CancellationTokenSource? operationCancellation;
    private int pageIndex;

    [ObservableProperty] private string connectionName = string.Empty;
    [ObservableProperty] private string connectionString = string.Empty;
    [ObservableProperty] private ConnectionProfile? selectedConnection;
    [ObservableProperty] private EntityTreeNode? selectedNode;
    [ObservableProperty] private MessageRecord? selectedMessage;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private int pageSize = 50;
    [ObservableProperty] private string statusMessage = "Add a connection to begin.";
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isPurgePanelVisible;
    [ObservableProperty] private string purgeConfirmation = string.Empty;
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
    public bool HasSelectedSource => SelectedNode?.Source is not null;
    public bool HasSelectedMessage => SelectedMessage is not null;
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
        var connections = await settings.GetConnectionsAsync(CancellationToken.None);
        foreach (var profile in connections)
        {
            Connections.Add(profile);
        }
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

            var profile = new ConnectionProfile(
                Guid.NewGuid(),
                string.IsNullOrWhiteSpace(ConnectionName) ? result.FullyQualifiedNamespace : ConnectionName.Trim(),
                result.FullyQualifiedNamespace);
            await secretStore.SetAsync(profile.Id, ConnectionString, token);
            await settings.SaveConnectionAsync(profile, token);
            Connections.Add(profile);
            ConnectionString = string.Empty;
            ConnectionName = string.Empty;
            SelectedConnection = profile;
            StatusMessage = "Connection added. Its credential is held in memory only.";
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

        PurgeConfirmation = string.Empty;
        IsPurgePanelVisible = true;
        OnPropertyChanged(nameof(PurgeTargetName));
        OnPropertyChanged(nameof(PurgeSummary));
    }

    [RelayCommand]
    private void ClosePurge() => IsPurgePanelVisible = false;

    [RelayCommand]
    private async Task ConfirmPurgeAsync()
    {
        if (SelectedNode?.Source is not { } source || SelectedConnection is not { } connection || IsBusy)
        {
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
            var request = new PurgeRequest(source, connection.FullyQualifiedNamespace, PurgeConfirmation, source.ApproximateCount, TimeSpan.FromMinutes(5));
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
                await LoadPageCoreAsync(secret, source, pageCursors[pageIndex], CancellationToken.None);
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
        ClearMessages();
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
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(HasSelectedSource));
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
        EntityNodes.Clear();
        foreach (var queue in entities.Where(item => item.Kind == EntityKind.Queue).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var node = new EntityTreeNode($"Queue  {queue.Name}");
            AddSubqueues(node, queue.Name, EntityKind.Queue, null, queue.IsPartitioned,
                queue.ApproximateActiveCount, queue.ApproximateDeadLetterCount);
            EntityNodes.Add(node);
        }

        foreach (var topic in entities.Where(item => item.Kind == EntityKind.Topic).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var topicNode = new EntityTreeNode($"Topic  {topic.Name}");
            foreach (var subscription in entities.Where(item => item.Kind == EntityKind.Subscription && item.ParentName == topic.Name)
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var subscriptionNode = new EntityTreeNode($"Subscription  {subscription.Name}");
                AddSubqueues(subscriptionNode, subscription.Name, EntityKind.Subscription, topic.Name, subscription.IsPartitioned,
                    subscription.ApproximateActiveCount, subscription.ApproximateDeadLetterCount);
                topicNode.Children.Add(subscriptionNode);
            }
            EntityNodes.Add(topicNode);
        }
    }

    private static void AddSubqueues(
        EntityTreeNode parent,
        string entityName,
        EntityKind kind,
        string? topicName,
        bool partitioned,
        long? activeCount,
        long? deadLetterCount)
    {
        parent.Children.Add(new("Active", new(entityName, kind, topicName, MessageSubqueue.Active, partitioned, activeCount)));
        parent.Children.Add(new("Dead-letter", new(entityName, kind, topicName, MessageSubqueue.DeadLetter, partitioned, deadLetterCount)));
        parent.Children.Add(new("Transfer dead-letter", new(entityName, kind, topicName, MessageSubqueue.TransferDeadLetter, partitioned)));
    }

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
    }

    private void ClearMessages()
    {
        Messages.Clear();
        FilteredMessages.Clear();
        SelectedMessage = null;
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
