using DesktopPet.Application.Commands;

namespace DesktopPet.Tests.Unit;

public sealed class CommandRegistryTests
{
    private sealed class TestCommand : IAppCommand
    {
        public CommandId Id => CommandId.OpenControlCenter;
        public int Calls { get; private set; }
        public Task<CommandResult> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new CommandResult(CommandStatus.Completed));
        }
    }

    [Fact]
    public async Task RegistryDispatchesOnlyExplicitlyRegisteredCommands()
    {
        var command = new TestCommand();
        var registry = new CommandRegistry([command]);
        Assert.Equal(CommandStatus.Completed, (await registry.ExecuteAsync(command.Id, default)).Status);
        Assert.Equal(CommandStatus.Unavailable, (await registry.ExecuteAsync(CommandId.OpenAi, default)).Status);
        Assert.Equal(1, command.Calls);
        Assert.Equal(command.Id, Assert.Single(registry.RegisteredCommands));
    }
    [Fact]
    public void DuplicateIdsAreRejected() => Assert.Throws<ArgumentException>(() =>
        new CommandRegistry([new TestCommand(), new TestCommand()]));

    [Fact]
    public async Task CancellationPreventsDispatch()
    {
        var command = new TestCommand();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CommandRegistry([command]).ExecuteAsync(command.Id, cancellation.Token));
        Assert.Equal(0, command.Calls);
    }
}
