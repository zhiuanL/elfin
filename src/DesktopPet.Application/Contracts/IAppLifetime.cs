namespace DesktopPet.Application.Contracts;

public interface IAppLifetime
{
    bool IsShuttingDown { get; }
    void RequestShutdown(int exitCode = 0);
}
