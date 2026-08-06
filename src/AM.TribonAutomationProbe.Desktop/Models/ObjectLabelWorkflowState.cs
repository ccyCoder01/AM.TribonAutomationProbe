using System.IO;

namespace AM.TribonAutomationProbe.Desktop.Models;

public enum ObjectLabelWorkflowStage
{
    Idle,
    Validating,
    WaitingForWorker,
    ParsingResult,
    ReadyToApply,
    Applying,
    Completed,
    Cancelled,
    Failed
}

public sealed record ConsoleWorkflowSettings(
    string ConsolePath,
    string BridgeRoot,
    int TimeoutMs,
    int PollIntervalMs)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConsolePath))
        {
            throw new ArgumentException(
                "Console executable path is required.",
                nameof(ConsolePath));
        }

        var fullConsolePath = Path.GetFullPath(ConsolePath);

        if (!File.Exists(fullConsolePath))
        {
            throw new FileNotFoundException(
                "Console executable was not found.",
                fullConsolePath);
        }

        if (!string.Equals(
                Path.GetExtension(fullConsolePath),
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The product workflow requires a published Console .exe.",
                nameof(ConsolePath));
        }

        if (string.IsNullOrWhiteSpace(BridgeRoot))
        {
            throw new ArgumentException(
                "FileBridge root is required.",
                nameof(BridgeRoot));
        }

        var fullBridgeRoot = Path.GetFullPath(BridgeRoot);

        if (!Directory.Exists(fullBridgeRoot))
        {
            throw new DirectoryNotFoundException(
                $"FileBridge root was not found: {fullBridgeRoot}");
        }

        if (TimeoutMs is < 1000 or > 3600000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TimeoutMs),
                "Timeout must be between 1,000 and 3,600,000 milliseconds.");
        }

        if (PollIntervalMs is < 50 or > 10000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PollIntervalMs),
                "Poll interval must be between 50 and 10,000 milliseconds.");
        }

        if (PollIntervalMs >= TimeoutMs)
        {
            throw new ArgumentException(
                "Poll interval must be lower than the timeout.");
        }
    }
}

public sealed record WorkflowProgress(
    double Percent,
    string Message,
    bool IsIndeterminate = false);

public sealed record BridgeActivitySnapshot(
    int InboxFileCount,
    int ProcessingFileCount)
{
    public bool IsIdle =>
        InboxFileCount == 0 &&
        ProcessingFileCount == 0;
}
