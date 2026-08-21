using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Scada.Runtime.Tags;

namespace Scada.App.ViewModels;

public sealed class MonitoringViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Dictionary<string, TagRowViewModel> _rows;
    private readonly List<IDisposable> _subscriptions = [];

    public MonitoringViewModel(ITagCache cache, RuntimeOptions options)
    {
        Rows = new ObservableCollection<TagRowViewModel>(options.Tags.Select(tag => new TagRowViewModel(tag)));
        _rows = Rows.ToDictionary(row => row.TagId, StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            _subscriptions.Add(cache.Subscribe(row.TagId, value => UpdateRow(row.TagId, value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<TagRowViewModel> Rows { get; }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    private void UpdateRow(string tagId, TagValue value)
    {
        if (!_rows.TryGetValue(tagId, out var row))
        {
            return;
        }

        void Update() => row.Update(value);
        if (Application.Current?.Dispatcher.CheckAccess() == true)
        {
            Update();
        }
        else
        {
            Application.Current?.Dispatcher.BeginInvoke(Update);
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
