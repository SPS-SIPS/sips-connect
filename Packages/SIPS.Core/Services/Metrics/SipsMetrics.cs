using System;
using System.Diagnostics;
using Prometheus;

namespace SIPS.Core.Services.Metrics;

/// <summary>
/// Provides Prometheus metrics for SIPS transaction processing.
/// </summary>
public static class SipsMetrics
{
    private static readonly Histogram ProcessingDuration = Prometheus.Metrics.CreateHistogram(
        "sips_transaction_processing_duration_seconds",
        "Duration of SIPS transaction processing steps in seconds.",
        new HistogramConfiguration
        {
            LabelNames = new[] { "Direction", "Type", "Step" },
            // Buckets from 10ms up to 10 seconds
            Buckets = new[] { 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10 }
        });

    /// <summary>
    /// Starts tracking the duration of a specific processing step.
    /// Dispose the returned object to record the metric.
    /// </summary>
    /// <param name="direction">"Incoming" or "Outgoing"</param>
    /// <param name="type">Transaction type, e.g., "Transaction", "Verification", "Return"</param>
    /// <param name="step">The specific step being measured, e.g., "Total", "DbSave", "CoreBankCall"</param>
    /// <returns>An IDisposable that records the metric upon disposal.</returns>
    public static IDisposable TrackStep(string direction, string type, string step)
    {
        return new StepTracker(direction, type, step);
    }

    private readonly struct StepTracker : IDisposable
    {
        private readonly string _direction;
        private readonly string _type;
        private readonly string _step;
        private readonly long _startTimestamp;

        public StepTracker(string direction, string type, string step)
        {
            _direction = direction;
            _type = type;
            _step = step;
            _startTimestamp = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            var elapsedSeconds = Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds;
            ProcessingDuration.WithLabels(_direction, _type, _step).Observe(elapsedSeconds);
        }
    }
}
