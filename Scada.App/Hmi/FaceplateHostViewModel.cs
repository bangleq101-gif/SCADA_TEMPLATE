using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Scada.App.Hmi;
public sealed class FaceplateHostViewModel : INotifyPropertyChanged
{
    private HmiEquipmentContext? _context;
    public event PropertyChangedEventHandler? PropertyChanged;
    public HmiEquipmentContext? Context { get => _context; private set { if (!ReferenceEquals(_context, value)) { _context = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Context))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOpen))); } } }
    public bool IsOpen => Context is not null;
    public void Open(HmiEquipmentContext context) => Context = context;
    public void Close() => Context = null;
}
