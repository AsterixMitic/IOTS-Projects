using WebDashboard.Models;

namespace WebDashboard.Services;

// Deljeno in-memory stanje dashboard-a. MqttBackgroundService ga puni,
// Blazor komponente se pretplaćuju na OnChange i re-renderuju.
public sealed class DashboardState
{
    private const int MaxItems = 30;
    private readonly object _lock = new();

    private readonly List<AnalyticsSummary> _summaryHistory = new();
    private readonly List<CepEvent> _recentEvents = new();
    private readonly List<ReadingMessage> _recentReadings = new();
    private readonly Dictionary<string, int> _eventCounts = new();

    public AnalyticsSummary? LatestSummary { get; private set; }
    public long TotalReadings { get; private set; }
    public bool MqttConnected { get; private set; }

    public event Action? OnChange;

    // Snapshot kopije (pod lock-om) — bezbedno čitanje iz Blazor komponenti dok MQTT nit piše.
    public IReadOnlyList<AnalyticsSummary> SnapshotSummaries() { lock (_lock) return _summaryHistory.ToArray(); }
    public IReadOnlyList<CepEvent> SnapshotEvents() { lock (_lock) return _recentEvents.ToArray(); }
    public IReadOnlyList<ReadingMessage> SnapshotReadings() { lock (_lock) return _recentReadings.ToArray(); }
    public IReadOnlyDictionary<string, int> SnapshotEventCounts() { lock (_lock) return new Dictionary<string, int>(_eventCounts); }

    public void SetSummary(AnalyticsSummary summary)
    {
        lock (_lock)
        {
            LatestSummary = summary;
            _summaryHistory.Insert(0, summary);
            if (_summaryHistory.Count > MaxItems) _summaryHistory.RemoveAt(_summaryHistory.Count - 1);
        }
        Notify();
    }

    public void AddEvent(CepEvent evt)
    {
        lock (_lock)
        {
            _recentEvents.Insert(0, evt);
            if (_recentEvents.Count > MaxItems) _recentEvents.RemoveAt(_recentEvents.Count - 1);
            var type = evt.Type ?? "UNKNOWN";
            _eventCounts[type] = _eventCounts.GetValueOrDefault(type) + 1;
        }
        Notify();
    }

    // Očitavanja stižu učestalo — mutiramo bez notifikacije; UI se osvežava tajmerom (Notify()).
    public void AddReading(ReadingMessage reading)
    {
        lock (_lock)
        {
            TotalReadings++;
            _recentReadings.Insert(0, reading);
            if (_recentReadings.Count > MaxItems) _recentReadings.RemoveAt(_recentReadings.Count - 1);
        }
    }

    public void SetConnected(bool connected)
    {
        MqttConnected = connected;
        Notify();
    }

    public void Notify() => OnChange?.Invoke();
}
