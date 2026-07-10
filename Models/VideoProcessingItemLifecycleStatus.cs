namespace PublicVideoLauncher.Models;

public enum VideoProcessingItemLifecycleStatus
{
    Pending,
    Skipped,
    Preparing,
    OpeningPlayer,
    WaitingForTranslationStart,
    WaitingForTranslationReady,
    FinalizingOutput,
    Completed,
    Failed,
    TimedOut,
    Canceled
}
