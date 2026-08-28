namespace DesktopPet.Domain.Pets;

public sealed class RecentBehaviorMemory(RuntimePolicy policy)
{
    private readonly Queue<BehaviorExecution> _recent = new();
    private readonly Dictionary<BehaviorId, DateTimeOffset> _last = [];
    public void Record(BehaviorId behavior, DateTimeOffset now)
    {
        _last[behavior] = now;
        _recent.Enqueue(new(behavior, now));
        Prune(now);
    }
    public RecentBehaviorContext Snapshot(DateTimeOffset now)
    {
        Prune(now);
        return new(Array.AsReadOnly(_recent.ToArray()),
            new System.Collections.ObjectModel.ReadOnlyDictionary<BehaviorId, DateTimeOffset>(new Dictionary<BehaviorId, DateTimeOffset>(_last)));
    }
    private void Prune(DateTimeOffset now)
    {
        while (_recent.Count > 0 && (_recent.Count > policy.RecentCapacity || now - _recent.Peek().StartedAtUtc > policy.RecentWindow))
            _recent.Dequeue();
    }
    public void Clear() { _recent.Clear(); _last.Clear(); }
}
