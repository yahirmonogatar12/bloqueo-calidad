using System.Text.Json;
using QualityLock.Shared.DTOs;

namespace QualityLock.Client.WinForms.Services;

/// <summary>
/// Drains the local offline event queue (event-queue.jsonl) and replays it
/// against the API once connectivity is restored. Each queued line is a
/// serialized <see cref="StationEventRequest"/>. Unsent events are re-queued so
/// nothing is lost across restarts.
/// </summary>
public class OfflineSyncService(ApiClientService api, LocalStateService localState)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Flushes queued offline events to the API. No-op when the queue is empty or
    /// the backend is unreachable. Safe to call repeatedly (e.g. from a timer).
    /// </summary>
    public async Task<int> FlushAsync(CancellationToken ct = default)
    {
        // Avoid overlapping flushes from concurrent timer ticks.
        if (!await _gate.WaitAsync(0, ct))
            return 0;

        try
        {
            if (!await api.IsAvailableAsync(ct))
                return 0;

            var lines = localState.DrainEventQueue();
            if (lines.Length == 0)
                return 0;

            var events = new List<StationEventRequest>(lines.Length);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var evt = JsonSerializer.Deserialize<StationEventRequest>(line, JsonOpts);
                    if (evt is not null) events.Add(evt);
                }
                catch (JsonException)
                {
                    // Corrupt/legacy line — drop it rather than blocking the queue forever.
                }
            }

            if (events.Count == 0)
                return 0;

            var ok = await api.RecordEventsAsync(events, ct);
            if (!ok)
            {
                // Send failed: put everything back so we retry on the next flush.
                Requeue(lines);
                return 0;
            }

            return events.Count;
        }
        catch
        {
            return 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Requeue(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
                localState.AppendEventQueue(line);
        }
    }
}
