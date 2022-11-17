using System.Diagnostics;
using Humanizer;
using Humanizer.Localisation;

namespace MyHomeLibServer.Core;

public sealed class ProgressTracker
{
    private readonly string _subSystem;
    private readonly long _maxItems;
    private readonly Action<ProgressData> _report;
    private readonly long _reportPeriod;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _lastTicks;
    private long _lastReport;
    private long _itemsProcessed = 0;
    private long _totalProcessed = 0;
    private readonly RunningAverage _average;

    public ProgressTracker(string subSystem, long maxItems, Action<ProgressData>? report = null, ILogger? logger = null, LogLevel logLevel = LogLevel.Information, TimeSpan reportPeriod = default)
    {
        if (report == null)
        {
            if (logger == null)
            {
                report = data => Debug.WriteLine(data.FormatTemplate, data.MessageArgs);
            }
            else
            {
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                report = data => logger.Log(logLevel, data.MessageTemplate, data.MessageArgs);
            }
        }

        _subSystem = subSystem;
        _maxItems = maxItems;
        _report = report;
        _reportPeriod = (long)(reportPeriod == default ? TimeSpan.FromSeconds(3) : reportPeriod).TotalSeconds * Stopwatch.Frequency;
        _lastTicks = _stopwatch.ElapsedTicks;
        _lastReport = _stopwatch.ElapsedTicks;
        _average = new RunningAverage(10);
    }

    public string FormatTemplate { get; } = @"{0}: {1} / {2} ({3:0.00} per minute). Est: {4}";
    public string FormatCompleteTemplate { get; } = @"{0}: {1} items ({2:0.00} per minute). Done in: {3}";
    public string OutputTemplate { get; set; } = @"{name}: {items} / {total} ({speedPerMinute:0.00} per minute). Est: {eta}";
    public string CompleteTemplate { get; set; } = @"{name}: {total} items ({speedPerMinute:0.00} per minute). Done in: {totalTime}";

    public double AverageSpeedSeconds => _average.Average;

    public void Track(long itemsProcessed = 1)
    {
        var elapsedTicks = _stopwatch.ElapsedTicks;
        _itemsProcessed += itemsProcessed;
        _totalProcessed += itemsProcessed;
        var isCompleted = _totalProcessed >= _maxItems;
        if (isCompleted || (elapsedTicks - _lastReport > _reportPeriod))
        {
            var span = (elapsedTicks - _lastTicks) / (double)Stopwatch.Frequency;
            var avgItemsPerSecond = _average.Add(_itemsProcessed / span);
            var eta = _average.Eta(_maxItems - _totalProcessed);
            _lastTicks = elapsedTicks;
            _report.Invoke(new ProgressData
            {
                FormatTemplate = isCompleted ? FormatCompleteTemplate : FormatTemplate,
                MessageTemplate = isCompleted ? CompleteTemplate : OutputTemplate,
                MessageArgs =
                    isCompleted
                        ? new object[]
                        {
                            _subSystem, _totalProcessed, 
                            (_totalProcessed / _stopwatch.Elapsed.TotalMinutes), 
                            _stopwatch.Elapsed.TotalSeconds.Seconds().Humanize(2, minUnit: TimeUnit.Second)
                        }
                        : new object[]
                        {
                            _subSystem, _totalProcessed, _maxItems, avgItemsPerSecond * 60, eta.TotalSeconds.Seconds().Humanize(2, minUnit: TimeUnit.Second)
                        },
            });
            _lastReport = elapsedTicks;
            _itemsProcessed = 0;
        }
    }

    public sealed class ProgressData
    {
        public string MessageTemplate { get; set; }
        public object[] MessageArgs { get; set; }
        public string FormatTemplate { get; set; }
    }
}