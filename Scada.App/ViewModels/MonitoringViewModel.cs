using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Scada.Runtime.Tags;

namespace Scada.App.ViewModels;

public sealed class MonitoringViewModel : INotifyPropertyChanged, IWorkspaceLifecycle, IDisposable
{
    private readonly object _lifecycleSync = new();
    private readonly ITagCache _cache;
    private readonly Dictionary<string, TagRowViewModel> _rows;
    private readonly List<IDisposable> _subscriptions = [];
    private long _activationGeneration;
    private bool _active;
    private bool _disposed;

    public MonitoringViewModel(ITagCache cache, RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(cache);

        _cache = cache;
        Rows = new ObservableCollection<TagRowViewModel>(options.Tags.Select(tag => new TagRowViewModel(tag)));
        _rows = Rows.ToDictionary(row => row.TagId, StringComparer.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<TagRowViewModel> Rows { get; }

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
        long generation;
        lock (_lifecycleSync)
        {
            if (_disposed || _active)
            {
                return;
            }

            _active = true;
            generation = ++_activationGeneration;
        }

        foreach (var row in Rows)
        {
            var subscription = _cache.Subscribe(
                row.TagId,
                value => UpdateRow(row.TagId, value, generation));

            var keepSubscription = false;
            lock (_lifecycleSync)
            {
                if (_active && _activationGeneration == generation)
                {
                    _subscriptions.Add(subscription);
                    keepSubscription = true;
                }
            }

            if (!keepSubscription)
            {
                subscription.Dispose();
                return;
            }

            if (_cache.TryGet(row.TagId, out var currentValue) && currentValue is not null)
            {
                UpdateRow(row.TagId, currentValue, generation);
            }
        }
    }

    public void Deactivate()
    {
        IDisposable[] subscriptions;
        lock (_lifecycleSync)
        {
            _active = false;
            _activationGeneration++;
            subscriptions = _subscriptions.ToArray();
            _subscriptions.Clear();
        }

        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_lifecycleSync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Deactivate();
    }

    private void UpdateRow(string tagId, TagValue value, long generation)
    {
        if (!_rows.TryGetValue(tagId, out var row))
        {
            return;
        }

        if (!IsCurrentGeneration(generation))
        {
            return;
        }

        void Update()
        {
            if (IsCurrentGeneration(generation))
            {
                row.Update(value);
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Update();
        }
        else
        {
            dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(Update));
        }
    }

    private bool IsCurrentGeneration(long generation)
    {
        lock (_lifecycleSync)
        {
            return _active && !_disposed && _activationGeneration == generation;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class TagRowViewModel(TagDefinition definition) : INotifyPropertyChanged
{
    private object? _value;
    private TagQuality _quality = TagQuality.NotConfigured;
    private DateTimeOffset? _timestamp;

    public event PropertyChangedEventHandler? PropertyChanged;
    public string TagId => definition.Id;
    public string Name => definition.Name;
    public string DeviceId => definition.DeviceId;
    public object? Value { get => _value; private set { _value = value; OnPropertyChanged(); } }
    public TagQuality Quality { get => _quality; private set { _quality = value; OnPropertyChanged(); } }
    public DateTimeOffset? Timestamp { get => _timestamp; private set { _timestamp = value; OnPropertyChanged(); } }

    public void Update(TagValue value)
    {
        Value = value.Value;
        Quality = value.Quality;
        Timestamp = value.Timestamp;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
