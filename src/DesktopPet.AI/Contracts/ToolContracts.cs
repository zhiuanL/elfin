using System.Text.Json;

namespace DesktopPet.AI.Contracts;

public enum ToolRiskLevel { Low, Medium, High, Forbidden }
public enum ConfirmationPolicy { None, ConfirmOrUndo, Always }
public enum MediumConfirmationPreference { AlwaysAsk, AllowReversibleWithoutPrompt }
public enum ToolExecutionStatus { Success, Denied, ValidationError, NotFound, Conflict, Failed, Cancelled }
public enum ToolConfirmationResult { NotRequired, Allowed, Denied }

public sealed record AiToolDefinition(string ToolId, string Description, string InputJsonSchema,
    ToolRiskLevel RiskLevel, ConfirmationPolicy ConfirmationPolicy, bool CanUserDisable);
public sealed record AiToolRequest(string ToolCallId, string ToolId, Guid ConversationId, string ArgumentsJson);
public sealed record AiToolResult(ToolExecutionStatus Status, string ResultCode, JsonElement? Data = null)
{
    public bool Succeeded => Status == ToolExecutionStatus.Success;
    public string ToModelJson() => JsonSerializer.Serialize(new { status = Status.ToString(), code = ResultCode, data = Data });
    public static AiToolResult Success(string code, object? data = null) =>
        new(ToolExecutionStatus.Success, code, data is null ? null : JsonSerializer.SerializeToElement(data));
}
public sealed record AiToolState(AiToolDefinition Definition, bool Enabled, bool CanChangeEnabled);
public sealed record ToolConfirmationRequest(string ToolId, string Description, string ParameterSummary, ToolRiskLevel RiskLevel);
public sealed record AiToolAuditEntry(Guid Id, DateTimeOffset TimestampUtc, Guid ConversationId,
    string ToolCallId, string ToolId, ToolRiskLevel RiskLevel, string ParameterSummary,
    ToolConfirmationResult ConfirmationResult, ToolExecutionStatus ExecutionStatus,
    long DurationMilliseconds, string? ErrorCategory);

public interface IAiTool
{
    AiToolDefinition Definition { get; }
    Task<AiToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct);
}
public interface IToolConfirmationService
{
    Task<bool> ConfirmAsync(ToolConfirmationRequest request, CancellationToken ct);
}
public interface IAiToolAuditRepository
{
    Task SaveAsync(AiToolAuditEntry entry, CancellationToken ct);
    Task<IReadOnlyList<AiToolAuditEntry>> ListRecentAsync(int limit, CancellationToken ct);
}
public interface IAiToolSchemaValidator
{
    bool TryValidate(string schema, JsonElement arguments, out string errorCode);
}
public interface IAiToolRegistry
{
    bool ToolsEnabled { get; }
    MediumConfirmationPreference MediumConfirmationPreference { get; }
    IReadOnlyList<AiToolDefinition> GetAvailableTools();
    IReadOnlyList<AiToolState> GetToolStates();
    Task SetToolsEnabledAsync(bool enabled, CancellationToken ct);
    Task SetToolEnabledAsync(string toolId, bool enabled, CancellationToken ct);
    Task SetMediumConfirmationPreferenceAsync(MediumConfirmationPreference preference, CancellationToken ct);
    Task<IReadOnlyList<AiToolAuditEntry>> GetRecentAuditAsync(int limit, CancellationToken ct);
    Task<AiToolResult> ExecuteAsync(AiToolRequest request, CancellationToken ct);
}

public static class AiToolProtocolName
{
    public static string Encode(string toolId) => toolId.Replace(".", "__", StringComparison.Ordinal);
}
