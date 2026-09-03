using System.Collections.Concurrent;
using System.Text.Json;
using DesktopPet.AI.Contracts;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Diagnostics;

namespace DesktopPet.AI.Tools;

public sealed class AiToolRegistry : IAiToolRegistry
{
    private const int MaximumCachedExecutions = 1024;
    private readonly IReadOnlyDictionary<string, IAiTool> _tools;
    private readonly IAiToolSchemaValidator _schema;
    private readonly IToolConfirmationService _confirmation;
    private readonly IAiToolAuditRepository _audit;
    private readonly ISettingsService _settings;
    private readonly IExceptionHandler _exceptions;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Lazy<Task<AiToolResult>>> _executions = new(StringComparer.Ordinal);

    public AiToolRegistry(IEnumerable<IAiTool> tools, IAiToolSchemaValidator schema, IToolConfirmationService confirmation,
        IAiToolAuditRepository audit, ISettingsService settings, IExceptionHandler exceptions, TimeProvider clock)
    {
        _schema = schema; _confirmation = confirmation; _audit = audit; _settings = settings; _exceptions = exceptions; _clock = clock;
        var allowed = tools.Where(tool => tool.Definition.RiskLevel != ToolRiskLevel.Forbidden).ToArray();
        ValidateDefinitions(allowed);
        _tools = allowed.ToDictionary(tool => tool.Definition.ToolId, StringComparer.Ordinal);
    }
    public bool ToolsEnabled => _settings.Current.AiTools.Enabled;
    public MediumConfirmationPreference MediumConfirmationPreference =>
        (MediumConfirmationPreference)_settings.Current.AiTools.MediumConfirmation;
    public IReadOnlyList<AiToolDefinition> GetAvailableTools() => !ToolsEnabled ? [] : _tools.Values
        .Where(tool => !_settings.Current.AiTools.DisabledToolIds.Contains(tool.Definition.ToolId, StringComparer.Ordinal))
        .Select(tool => tool.Definition).OrderBy(item => item.ToolId, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<AiToolState> GetToolStates() => _tools.Values.OrderBy(tool => tool.Definition.ToolId, StringComparer.Ordinal)
        .Select(tool => new AiToolState(tool.Definition,
            !_settings.Current.AiTools.DisabledToolIds.Contains(tool.Definition.ToolId, StringComparer.Ordinal),
            tool.Definition.CanUserDisable)).ToArray();
    public Task SetToolsEnabledAsync(bool enabled, CancellationToken ct) =>
        _settings.UpdateAsync(value => value with { AiTools = value.AiTools with { Enabled = enabled } }, ct);
    public Task SetMediumConfirmationPreferenceAsync(MediumConfirmationPreference preference, CancellationToken ct)
    {
        if (!Enum.IsDefined(preference)) throw new ArgumentOutOfRangeException(nameof(preference));
        return _settings.UpdateAsync(value => value with { AiTools = value.AiTools with
            { MediumConfirmation = (AiMediumConfirmationPreference)preference } }, ct);
    }
    public Task SetToolEnabledAsync(string toolId, bool enabled, CancellationToken ct)
    {
        if (!_tools.TryGetValue(toolId, out var tool)) throw new KeyNotFoundException("Unknown AI tool.");
        if (!tool.Definition.CanUserDisable && !enabled) throw new InvalidOperationException("This AI tool cannot be disabled.");
        return _settings.UpdateAsync(value =>
        {
            var disabled = value.AiTools.DisabledToolIds.ToHashSet(StringComparer.Ordinal);
            if (enabled) disabled.Remove(toolId); else disabled.Add(toolId);
            return value with { AiTools = value.AiTools with { DisabledToolIds = disabled.Order(StringComparer.Ordinal).ToArray() } };
        }, ct);
    }
    public Task<IReadOnlyList<AiToolAuditEntry>> GetRecentAuditAsync(int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        return _audit.ListRecentAsync(limit, ct);
    }
    public async Task<AiToolResult> ExecuteAsync(AiToolRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ToolCallId) || request.ToolCallId.Length > 128)
            return new(ToolExecutionStatus.ValidationError, "invalid_tool_call_id");
        var key = $"{request.ConversationId:N}:{request.ToolCallId}";
        var lazy = _executions.GetOrAdd(key, _ => new(() => ExecuteCoreAsync(request, ct), LazyThreadSafetyMode.ExecutionAndPublication));
        try { return await lazy.Value.WaitAsync(ct); }
        finally { TrimExecutions(); }
    }

    private async Task<AiToolResult> ExecuteCoreAsync(AiToolRequest request, CancellationToken ct)
    {
        var started = _clock.GetTimestamp();
        var summary = AiToolParameterRedactor.Summarize(request.ArgumentsJson);
        var risk = ToolRiskLevel.Forbidden;
        var confirmationResult = ToolConfirmationResult.NotRequired;
        AiToolResult result;
        try
        {
            if (!ToolsEnabled || !_tools.TryGetValue(request.ToolId, out var tool) ||
                _settings.Current.AiTools.DisabledToolIds.Contains(request.ToolId, StringComparer.Ordinal))
                return await AuditedAsync(request, risk, summary, confirmationResult,
                    new(ToolExecutionStatus.Denied, "tool_unavailable"), started);
            risk = tool.Definition.RiskLevel;
            JsonDocument arguments;
            try { arguments = JsonDocument.Parse(request.ArgumentsJson, new JsonDocumentOptions { MaxDepth = 16 }); }
            catch (JsonException) { return await AuditedAsync(request, risk, summary, confirmationResult,
                new(ToolExecutionStatus.ValidationError, "invalid_json"), started); }
            using (arguments)
            {
                if (!_schema.TryValidate(tool.Definition.InputJsonSchema, arguments.RootElement, out var schemaError))
                    return await AuditedAsync(request, risk, summary, confirmationResult,
                        new(ToolExecutionStatus.ValidationError, schemaError), started);
                if (RequiresConfirmation(tool.Definition))
                {
                    var allowed = await _confirmation.ConfirmAsync(new(tool.Definition.ToolId, tool.Definition.Description, summary, risk), ct);
                    confirmationResult = allowed ? ToolConfirmationResult.Allowed : ToolConfirmationResult.Denied;
                    if (!allowed) return await AuditedAsync(request, risk, summary, confirmationResult,
                        new(ToolExecutionStatus.Denied, "user_denied"), started);
                }
                result = await tool.ExecuteAsync(arguments.RootElement, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { result = new(ToolExecutionStatus.Cancelled, "cancelled"); }
        catch (KeyNotFoundException) { result = new(ToolExecutionStatus.NotFound, "not_found"); }
        catch (ArgumentException) { result = new(ToolExecutionStatus.ValidationError, "invalid_parameter"); }
        catch (InvalidOperationException) { result = new(ToolExecutionStatus.Conflict, "conflict"); }
        catch (Exception exception)
        {
            _exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.Command);
            result = new(ToolExecutionStatus.Failed, "execution_failed");
        }
        return await AuditedAsync(request, risk, summary, confirmationResult, result, started);
    }
    private bool RequiresConfirmation(AiToolDefinition definition) => definition.RiskLevel == ToolRiskLevel.High ||
        definition.ConfirmationPolicy == ConfirmationPolicy.Always ||
        definition.RiskLevel == ToolRiskLevel.Medium &&
        (definition.ConfirmationPolicy != ConfirmationPolicy.ConfirmOrUndo ||
         MediumConfirmationPreference == MediumConfirmationPreference.AlwaysAsk);
    private async Task<AiToolResult> AuditedAsync(AiToolRequest request, ToolRiskLevel risk, string summary,
        ToolConfirmationResult confirmation, AiToolResult result, long started)
    {
        var duration = Math.Max(0, (long)_clock.GetElapsedTime(started).TotalMilliseconds);
        var audit = new AiToolAuditEntry(Guid.NewGuid(), _clock.GetUtcNow(), request.ConversationId, request.ToolCallId,
            request.ToolId, risk, summary, confirmation, result.Status, duration,
            result.Succeeded ? null : result.ResultCode);
        try { await _audit.SaveAsync(audit, CancellationToken.None); }
        catch (Exception exception) { _exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.AiStorage); }
        return result;
    }
    private void TrimExecutions()
    {
        if (_executions.Count <= MaximumCachedExecutions) return;
        foreach (var item in _executions.Where(item => item.Value.IsValueCreated && item.Value.Value.IsCompleted)
                     .Take(_executions.Count - MaximumCachedExecutions)) _executions.TryRemove(item.Key, out _);
    }
    private static void ValidateDefinitions(IEnumerable<IAiTool> tools)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            var definition = tool.Definition;
            if (!identifiers.Add(definition.ToolId)) throw new InvalidOperationException($"Duplicate AI tool: {definition.ToolId}");
            if (definition.ToolId.Length is 0 or > 64 || definition.ToolId.Any(c => !(char.IsLetter(c) || char.IsDigit(c) || c is '.' or '-' or '_')))
                throw new InvalidOperationException("Invalid AI tool identifier.");
            if (string.IsNullOrWhiteSpace(definition.Description) || definition.Description.Length > 1024)
                throw new InvalidOperationException("Invalid AI tool description.");
            if (definition.RiskLevel == ToolRiskLevel.Low && definition.ConfirmationPolicy != ConfirmationPolicy.None ||
                definition.RiskLevel == ToolRiskLevel.High && definition.ConfirmationPolicy != ConfirmationPolicy.Always)
                throw new InvalidOperationException("Invalid AI tool risk policy.");
            using var _ = JsonDocument.Parse(definition.InputJsonSchema);
        }
    }
}
