using System.Text.Json;

namespace DesktopPet.AI.Contracts;

public enum ToolRiskLevel { Low, Medium, High, Forbidden }
public enum ConfirmationPolicy { None, ConfirmOrUndo, Always }
public sealed record AiToolDefinition(string ToolId, string Description, string InputJsonSchema,
    ToolRiskLevel RiskLevel, ConfirmationPolicy ConfirmationPolicy, bool CanUserDisable);
public sealed record AiToolRequest(string ToolId, Guid ConversationId, JsonElement Arguments);
public sealed record AiToolResult(bool Succeeded, string ResultCode);
public interface IAiToolRegistry
{
    IReadOnlyList<AiToolDefinition> GetAvailableTools();
    Task<AiToolResult> ExecuteAsync(AiToolRequest request, CancellationToken ct);
}
// Definition only. Risk validation, user confirmation and execution are Phase 8 work.
