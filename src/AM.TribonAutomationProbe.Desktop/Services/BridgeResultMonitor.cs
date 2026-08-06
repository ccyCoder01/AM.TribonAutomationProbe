using System.IO;
using AM.TribonAutomationProbe.Desktop.Models;

namespace AM.TribonAutomationProbe.Desktop.Services;

public sealed class BridgeResultMonitor
{
    public BridgeActivitySnapshot Capture(string bridgeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgeRoot);

        var root = Path.GetFullPath(bridgeRoot);
        var inbox = Path.Combine(root, "inbox");
        var processing = Path.Combine(root, "processing");

        return new BridgeActivitySnapshot(
            CountFiles(inbox),
            CountFiles(processing));
    }

    public void EnsureIdle(string bridgeRoot)
    {
        var snapshot = Capture(bridgeRoot);

        if (snapshot.IsIdle)
        {
            return;
        }

        throw new InvalidOperationException(
            "FileBridge is not idle. " +
            $"inbox={snapshot.InboxFileCount}, " +
            $"processing={snapshot.ProcessingFileCount}. " +
            "Complete or archive the existing command before starting another workflow.");
    }

    private static int CountFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        return Directory.EnumerateFiles(
                directory,
                "*",
                SearchOption.TopDirectoryOnly)
            .Count();
    }
}
