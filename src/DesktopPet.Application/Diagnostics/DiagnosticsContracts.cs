using DesktopPet.Application.Configuration;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Application.Diagnostics;

public enum AppEvent { Starting, Started, Stopping, MigrationApplied, SettingsRecovered, Failure,
    BehaviorSelected, StateChanged, CharacterSwitched, SchedulerStarted, SchedulerStopped, EmotionChanged, DecisionFallback }
public enum ErrorCode { StartupFailed, DatabaseMigrationFailed, AiStorageUnavailable, UnhandledException, CommandFailed }
public enum ErrorOrigin { Startup, Dispatcher, AppDomain, BackgroundTask, Command, AiStorage }
public sealed record AppFailure(ErrorCode Code, ErrorOrigin Origin, Guid CorrelationId, DateTimeOffset TimestampUtc);

// No free-form text, exception messages, chat content, configuration or credentials enter this API.
public sealed record AppLogEntry(AppEvent Event, DateTimeOffset TimestampUtc,
    ErrorCode? ErrorCode = null, ErrorOrigin? Origin = null, Guid? CorrelationId = null,
    BehaviorId? Behavior = null, PetPrimaryState? State = null);
public interface IAppLogger
{
    void Configure(LogOptions options);
    void Write(AppLogEntry entry);
}
public interface IExceptionHandler
{
    AppFailure Report(Exception exception, ErrorCode code, ErrorOrigin origin);
}
public sealed class ExceptionHandler(IAppLogger logger, TimeProvider timeProvider) : IExceptionHandler
{
    public AppFailure Report(Exception exception, ErrorCode code, ErrorOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var failure = new AppFailure(code, origin, Guid.NewGuid(), timeProvider.GetUtcNow());
        logger.Write(new(AppEvent.Failure, failure.TimestampUtc, code, origin, failure.CorrelationId));
        return failure;
    }
}
