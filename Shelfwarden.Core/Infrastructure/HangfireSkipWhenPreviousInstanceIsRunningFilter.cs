using Hangfire.Common;
using Hangfire.States;

namespace Shelfwarden.Infrastructure;

/// <summary>
/// Skips enqueueing a job when another job with the same method fingerprint is already
/// processing. Apply with <c>[SkipWhenPreviousInstanceIsRunning]</c>. One-off jobs that do
/// not carry the attribute are left alone.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipWhenPreviousInstanceIsRunningAttribute : JobFilterAttribute, IElectStateFilter
{
    /// <summary>
    /// When the job is about to be enqueued, replaces that state with deleted if a matching
    /// job is already processing.
    /// </summary>
    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is not EnqueuedState)
        {
            return;
        }

        string fingerprint = context.BackgroundJob.Job.ToString();
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