using System.Text.Json;
using QualityLock.Client.WinForms.Models;
using QualityLock.Shared.Constants;
using QualityLock.Shared.DTOs;

namespace QualityLock.Client.WinForms.Services;

public class LocalStateService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ClientState LoadState()
    {
        try
        {
            if (!File.Exists(AppConstants.ClientStateFile)) return new ClientState();
            var json = File.ReadAllText(AppConstants.ClientStateFile);
            return JsonSerializer.Deserialize<ClientState>(json) ?? new ClientState();
        }
        catch
        {
            return new ClientState();
        }
    }

    public void SaveState(ClientState state)
    {
        Directory.CreateDirectory(AppConstants.LocalDataPath);
        File.WriteAllText(AppConstants.ClientStateFile, JsonSerializer.Serialize(state, JsonOpts));
    }

    public IReadOnlyList<CachedOperatorDto> LoadOperatorCache()
    {
        try
        {
            if (!File.Exists(AppConstants.OperatorCacheFile)) return [];
            var json = File.ReadAllText(AppConstants.OperatorCacheFile);
            return JsonSerializer.Deserialize<List<CachedOperatorDto>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveOperatorCache(IReadOnlyList<CachedOperatorDto> operators)
    {
        Directory.CreateDirectory(AppConstants.LocalDataPath);
        File.WriteAllText(AppConstants.OperatorCacheFile, JsonSerializer.Serialize(operators, JsonOpts));
    }

    private static readonly object QueueLock = new();

    public void AppendEventQueue(string eventJson)
    {
        Directory.CreateDirectory(AppConstants.LocalDataPath);
        lock (QueueLock)
        {
            File.AppendAllText(AppConstants.EventQueueFile, eventJson + Environment.NewLine);
        }
    }

    /// <summary>
    /// Atomically takes ownership of the current queue by renaming it aside, so a
    /// concurrent append cannot lose a line between read and delete. Returns the
    /// drained lines; the caller is responsible for re-queueing any it cannot send.
    /// </summary>
    public string[] DrainEventQueue()
    {
        lock (QueueLock)
        {
            if (!File.Exists(AppConstants.EventQueueFile)) return [];

            var temp = AppConstants.EventQueueFile + ".draining";
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
                File.Move(AppConstants.EventQueueFile, temp);
            }
            catch (IOException)
            {
                return [];
            }

            var lines = File.ReadAllLines(temp);
            File.Delete(temp);
            return lines;
        }
    }
}
