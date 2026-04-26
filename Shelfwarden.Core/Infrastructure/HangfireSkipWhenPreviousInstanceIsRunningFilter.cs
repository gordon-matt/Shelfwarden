using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;

namespace Shelfwarden.Infrastructure;

/// <summary>
/// Hangfire filter that skips a recurring job if a previous instance is still running.
/// Apply with <c>[SkipWhenPreviousInstanceIsRunning]</c>. Useful for long-running scan
/// jobs where overlap would corrupt state.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipWhenPreviousInstanceIsRunningAttribute : JobFilterAttribute, IElectStateFilter
{
    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is not EnqueuedState)
        {
            return;
        }

        var fingerprint = context.BackgroundJob.Job.ToString();
        var monitoringApi = context.Storage.GetMonitoringApi();

        long processingCount = monitoringApi.ProcessingCount();
        if (processingCount == 0)
        {
            return;
        }

        var processing = monitoringApi.ProcessingJobs(0, (int)processingCount);
        bool isAlreadyRunning = processing.Any(pair =>
            pair.Value?.Job?.ToString() == fingerprint);

        if (isAlreadyRunning)
        {
            context.CandidateState = new DeletedState
            {
                Reason = "Skipped because another instance is already running."
            };
        }
    }
}
