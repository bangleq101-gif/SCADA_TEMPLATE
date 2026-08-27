using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Scada.Runtime.Tags;

namespace Scada.App.ViewModels;

/// <summary>
/// Displays a bounded, metadata-filtered page of TagCache values.
/// The view model owns subscriptions only while its workspace is active.
/// </summary>
public sealed class MonitoringViewModel : INotifyPropertyChanged, IWorkspaceLifecycle, IDisposable
{
    public const int DefaultPageSize = 250;
    public const int MaximumPageSize = 500;

    private const string AllDevices = "All devices";
    private readonly object _lifecycleSync = new();
    private readonly ITagCache _cache;
    private readonly IMonitoringDispatcher _dispatcher;
    private readonly IReadOnlyList<TagDefinition> _tags;
    private readonly Dictionary<string, TagRowViewModel> _visibleRowsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SubscriptionSlot> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TagValue> _pendingValues = new(StringComparer.OrdinalIgnoreCase);
    private long _generation;
    private bool _active;
    private bool _disposed;
    private bool _drainScheduled;
    private string _searchText = string.Empty;
    private string _selectedDeviceId = AllDevices;
    private int _pageSize = DefaultPageSize;
    private int _currentPage;
    private int _pageCount = 1;
    private int _totalMatchingTags;

    public MonitoringViewModel(ITagCache cache, RuntimeOptions options)
        : this(cache, options, new WpfMonitoringDispatcher())
    {
    }

    public MonitoringViewModel(ITagCache cache, RuntimeOptions options, IMonitoringDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _cache = cache;
        _dispatcher = dispatcher;
        _tags = options.Tags.ToArray();
        DeviceFilters = new ReadOnlyCollection<string>(
            [AllDevices, .. _tags.Select(tag => tag.DeviceId)
                .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(deviceId => deviceId, StringComparer.OrdinalIgnoreCase)]);
        PageSizeOptions = new ReadOnlyCollection<int>([100, DefaultPageSize, MaximumPageSize]);
        Rows = [];
        PreviousPageCommand = new RelayCommand(_ => MoveToPreviousPage(), _ => CanMoveToPreviousPage);
        NextPageCommand = new RelayCommand(_ => MoveToNextPage(), _ => CanMoveToNextPage);
        RefreshVisibleRows(resetPage: true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TagRowViewModel> Rows { get; }

    public RelayCommand PreviousPageCommand { get; }

    public RelayCommand NextPageCommand { get; }

    public IReadOnlyList<string> DeviceFilters { get; }

    public IReadOnlyList<int> PageSizeOptions { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (SetField(ref _searchText, value))
            {
                RefreshVisibleRows(resetPage: true);
            }
        }
    }

    public string SelectedDeviceId
    {
        get => _selectedDeviceId;
        set
        {
            value = string.IsNullOrWhiteSpace(value) ? AllDevices : value;
            if (SetField(ref _selectedDeviceId, value))
            {
                RefreshVisibleRows(resetPage: true);
            }
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            var bounded = Math.Clamp(value, 1, MaximumPageSize);
            if (SetField(ref _pageSize, bounded))
            {
                RefreshVisibleRows(resetPage: true);
            }
        }
    }

    public int CurrentPage => _currentPage + 1;

    public int PageCount => _pageCount;

    public int TotalMatchingTags => _totalMatchingTags;

    public string PageSummary => _totalMatchingTags == 0
        ? "No matching tags"
        : $"Page {CurrentPage} of {PageCount} · {_totalMatchingTags:N0} tags";

    public bool CanMoveToPreviousPage => _currentPage > 0;

    public bool CanMoveToNextPage => _currentPage + 1 < _pageCount;

    public bool IsActive
    {
        get
        {
            lock (_lifecycleSync)
            {
                return _active && !_disposed;
            }
        }
    }

    public void Activate()
    {
        lock (_lifecycleSync)
        {
            if (_disposed || _active)
            {
                return;
            }

            _active = true;
            _generation++;
        }

        RefreshVisibleRows(resetPage: false);
    }

    public void Deactivate() => StopSubscriptions(markDisposed: false);

    public void Dispose() => StopSubscriptions(markDisposed: true);

    public void MoveToPreviousPage()
    {
        if (_currentPage == 0)
        {
            return;
        }

        _currentPage--;
        RefreshVisibleRows(resetPage: false);
    }

    public void MoveToNextPage()
    {
        if (_currentPage + 1 >= _pageCount)
        {
            return;
        }

        _currentPage++;
        RefreshVisibleRows(resetPage: false);
    }

    private void RefreshVisibleRows(bool resetPage)
    {
        var matchingTags = _tags.Where(MatchesFilter).ToArray();
        var pageCount = Math.Max(1, (int)Math.Ceiling(matchingTags.Length / (double)_pageSize));
        var page = resetPage ? 0 : Math.Clamp(_currentPage, 0, pageCount - 1);
        var visibleTags = matchingTags.Skip(page * _pageSize).Take(_pageSize).ToArray();
        var visibleRows = visibleTags.Select(tag => new TagRowViewModel(tag)).ToArray();
        var desiredTagIds = new HashSet<string>(visibleTags.Select(tag => tag.Id), StringComparer.OrdinalIgnoreCase);
        var subscriptionsToDispose = new List<IDisposable>();
        var subscriptionsToAcquire = new List<TagDefinition>();
        long generation;
        bool active;

        lock (_lifecycleSync)
        {
            if (_disposed)
            {
                return;
            }

            _currentPage = page;
            _pageCount = pageCount;
            _totalMatchingTags = matchingTags.Length;
            generation = _active ? ++_generation : _generation;
            active = _active;
            _pendingValues.Clear();
            _drainScheduled = false;

            _visibleRowsById.Clear();
            foreach (var row in visibleRows)
            {
                _visibleRowsById[row.TagId] = row;
            }

            foreach (var (tagId, slot) in _subscriptions.ToArray())
            {
                if (desiredTagIds.Contains(tagId))
                {
                    Volatile.Write(ref slot.Generation, generation);
                    continue;
                }

                _subscriptions.Remove(tagId);
                subscriptionsToDispose.Add(slot.Subscription);
            }

            if (active)
            {
                subscriptionsToAcquire.AddRange(visibleTags
                    .GroupBy(tag => tag.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Where(tag => !_subscriptions.ContainsKey(tag.Id)));
            }
        }

        foreach (var subscription in subscriptionsToDispose)
        {
            subscription.Dispose();
        }

        Rows.Clear();
        foreach (var row in visibleRows)
        {
            Rows.Add(row);
        }

        NotifyPageStateChanged();

        if (active)
        {
            AcquireSubscriptions(subscriptionsToAcquire, generation);
        }
    }

    private void AcquireSubscriptions(IEnumerable<TagDefinition> tags, long generation)
    {
        foreach (var tag in tags)
        {
            var slot = new SubscriptionSlot(generation);
            var subscription = _cache.Subscribe(
                tag.Id,
                value => QueueUpdate(tag.Id, value, Volatile.Read(ref slot.Generation)));

            var keepSubscription = false;
            lock (_lifecycleSync)
            {
                if (IsCurrentGenerationUnsafe(generation)
                    && _visibleRowsById.ContainsKey(tag.Id)
                    && !_subscriptions.ContainsKey(tag.Id))
                {
                    slot.Subscription = subscription;
                    _subscriptions.Add(tag.Id, slot);
                    keepSubscription = true;
                }
            }

            if (!keepSubscription)
            {
                subscription.Dispose();
                return;
            }

            // Subscribe before taking the TagCache seed so no update can be missed.
            if (_cache.TryGet(tag.Id, out var currentValue) && currentValue is not null)
            {
                QueueUpdate(tag.Id, currentValue, generation);
            }
        }
    }

    private void QueueUpdate(string tagId, TagValue value, long generation)
    {
        var scheduleDrain = false;
        lock (_lifecycleSync)
        {
            if (!IsCurrentGenerationUnsafe(generation) || !_visibleRowsById.ContainsKey(tagId))
            {
                return;
            }

            if (!_pendingValues.TryGetValue(tagId, out var pendingValue) || value.Sequence >= pendingValue.Sequence)
            {
                _pendingValues[tagId] = value;
            }
            if (!_drainScheduled)
            {
                _drainScheduled = true;
                scheduleDrain = true;
            }
        }

        if (scheduleDrain)
        {
            _dispatcher.Enqueue(() => DrainPendingValues(generation));
        }
    }

    private void DrainPendingValues(long generation)
    {
        KeyValuePair<string, TagValue>[] updates;
        lock (_lifecycleSync)
        {
            // Guard both before scheduling and inside the dispatched callback.
            if (!IsCurrentGenerationUnsafe(generation))
            {
                // This callback belongs to a previous activation/page generation.
                // It must not clear pending values or scheduling state owned by a newer one.
                return;
            }

            updates = _pendingValues.ToArray();
            _pendingValues.Clear();
            _drainScheduled = false;
        }

        foreach (var (tagId, value) in updates)
        {
            lock (_lifecycleSync)
            {
                if (!IsCurrentGenerationUnsafe(generation)
                    || !_visibleRowsById.TryGetValue(tagId, out var row))
                {
                    return;
                }

                row.Update(value);
            }
        }
    }

    private void StopSubscriptions(bool markDisposed)
    {
        IDisposable[] subscriptions;
        lock (_lifecycleSync)
        {
            if (_disposed && !markDisposed)
            {
                return;
            }

            if (markDisposed && _disposed)
            {
                return;
            }

            _active = false;
            _generation++;
            if (markDisposed)
            {
                _disposed = true;
            }

            _pendingValues.Clear();
            _drainScheduled = false;
            subscriptions = _subscriptions.Values.Select(slot => slot.Subscription).ToArray();
            _subscriptions.Clear();
        }

        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }
    }

    private bool MatchesFilter(TagDefinition tag)
    {
        if (!string.Equals(_selectedDeviceId, AllDevices, StringComparison.Ordinal)
            && !string.Equals(tag.DeviceId, _selectedDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return true;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        return tag.Id.Contains(_searchText, comparison)
            || tag.Name.Contains(_searchText, comparison)
            || tag.Description.Contains(_searchText, comparison)
            || tag.DeviceId.Contains(_searchText, comparison)
            || tag.Address.Contains(_searchText, comparison);
    }

    private bool IsCurrentGenerationUnsafe(long generation) =>
        _active && !_disposed && _generation == generation;

    private void NotifyPageStateChanged()
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(TotalMatchingTags));
        OnPropertyChanged(nameof(PageSummary));
        OnPropertyChanged(nameof(CanMoveToPreviousPage));
        OnPropertyChanged(nameof(CanMoveToNextPage));
        PreviousPageCommand.Refresh();
        NextPageCommand.Refresh();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class SubscriptionSlot(long generation)
    {
        public long Generation = generation;

        public IDisposable Subscription { get; set; } = NullSubscription.Instance;
    }

    private sealed class NullSubscription : IDisposable
    {
        public static readonly NullSubscription Instance = new();

        public void Dispose()
        {
        }
    }
}

public sealed class TagRowViewModel(TagDefinition definition) : INotifyPropertyChanged
{
    private object? _value;
    private TagQuality _quality = TagQuality.NotConfigured;
    private DateTimeOffset? _timestamp;
    private long _sequence = -1;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TagId => definition.Id;

    public string Name => definition.Name;

    public string DeviceId => definition.DeviceId;

    public string Address => definition.Address;

    public object? Value { get => _value; private set { _value = value; OnPropertyChanged(); } }

    public TagQuality Quality { get => _quality; private set { _quality = value; OnPropertyChanged(); } }

    public DateTimeOffset? Timestamp { get => _timestamp; private set { _timestamp = value; OnPropertyChanged(); } }

    public long Sequence => _sequence;

    public void Update(TagValue value)
    {
        if (value.Sequence < _sequence)
        {
            return;
        }

        _sequence = value.Sequence;
        Value = value.Value;
        Quality = value.Quality;
        Timestamp = value.Timestamp;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
