using QualityLock.Shared.Constants;

namespace QualityLock.Client.WinForms.Services;

/// <summary>
/// Detects repeated crash-on-startup loops and flips the client into a degraded
/// "safe mode" that does NOT install the aggressive lock (keyboard hook +
/// Task Manager lock), so an administrator can recover the machine.
///
/// Model: every startup is assumed to be a potential crash. <see cref="BeginStartup"/>
/// records the attempt up front; if the UI stays alive long enough,
/// <see cref="MarkStartupHealthy"/> clears the counter. If the process keeps dying
/// before becoming healthy, the counter reaches the threshold and safe mode engages.
/// </summary>
public class SafeModeService(LocalStateService stateService)
{
    public bool IsSafeMode { get; private set; }

    public void Initialize()
    {
        var state = stateService.LoadState();
        IsSafeMode = state.SafeMode;
    }

    /// <summary>
    /// Records a startup attempt. Call once, early in startup. If too many attempts
    /// happen within the safe-mode window without a healthy run in between, safe mode
    /// engages and the method returns true.
    /// </summary>
    public bool BeginStartup()
    {
        var state = stateService.LoadState();
        var windowStart = DateTime.UtcNow.AddMinutes(-AppConstants.SafeModeWindowMinutes);

        // Counter only accumulates within the rolling window.
        if (state.LastCrashUtc is null || state.LastCrashUtc < windowStart)
            state.CrashCounter = 0;

        state.CrashCounter++;
        state.LastCrashUtc = DateTime.UtcNow;

        if (state.CrashCounter >= AppConstants.SafeModeFailureThreshold)
        {
            state.SafeMode = true;
            IsSafeMode = true;
        }

        state.UpdatedAtUtc = DateTime.UtcNow;
        stateService.SaveState(state);
        return IsSafeMode;
    }

    /// <summary>
    /// Marks the current run as healthy: the UI came up and stayed up. Clears the
    /// crash counter so a later, unrelated crash does not inherit old attempts.
    /// Does NOT clear an already-latched safe mode (that requires explicit recovery).
    /// </summary>
    public void MarkStartupHealthy()
    {
        var state = stateService.LoadState();
        state.CrashCounter = 0;
        state.LastCrashUtc = null;
        state.UpdatedAtUtc = DateTime.UtcNow;
        stateService.SaveState(state);
    }

    /// <summary>Explicitly clears safe mode and the crash history (admin recovery).</summary>
    public void ClearSafeMode()
    {
        var state = stateService.LoadState();
        state.SafeMode = false;
        state.CrashCounter = 0;
        state.LastCrashUtc = null;
        state.UpdatedAtUtc = DateTime.UtcNow;
        stateService.SaveState(state);
        IsSafeMode = false;
    }
}
