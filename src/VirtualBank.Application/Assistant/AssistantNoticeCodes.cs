namespace VirtualBank.Application.Assistant;

/// <summary>
/// Deterministic notice codes produced by the assistant module. They are emitted by application code
/// after a gate or use-case decision, never by the model, so a prompt cannot add, remove or reword them.
/// </summary>
public static class AssistantNoticeCodes
{
    public const string AssistantUseRequired = "ASSISTANT_USE_REQUIRED";
    public const string MachinePrincipalNotAllowed = "ASSISTANT_MACHINE_PRINCIPAL_NOT_ALLOWED";
    public const string UnknownTool = "ASSISTANT_UNKNOWN_TOOL";
    public const string ToolNotPermitted = "ASSISTANT_TOOL_NOT_PERMITTED";
    public const string InvalidToolArguments = "ASSISTANT_INVALID_TOOL_ARGUMENTS";
    public const string ToolCallLimitReached = "ASSISTANT_TOOL_CALL_LIMIT_REACHED";
    public const string WriteLimitReached = "ASSISTANT_WRITE_LIMIT_REACHED";
    public const string HumanConfirmationRequired = "ASSISTANT_HUMAN_CONFIRMATION_REQUIRED";
    public const string ToolFailed = "ASSISTANT_TOOL_FAILED";
    public const string ModelUnavailable = "ASSISTANT_MODEL_UNAVAILABLE";
    public const string NoDocumentEvidence = "ASSISTANT_NO_DOCUMENT_EVIDENCE";
    public const string NoVerifiedData = "ASSISTANT_NO_VERIFIED_DATA";
    public const string NoVerifiedFinancialData = "ASSISTANT_NO_VERIFIED_FINANCIAL_DATA";
}
