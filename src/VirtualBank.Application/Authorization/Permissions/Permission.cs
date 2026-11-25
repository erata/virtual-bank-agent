namespace VirtualBank.Application.Authorization.Permissions;

/// <summary>
/// Canonical, language-independent permission identifier such as <c>credit.approve</c>.
/// Business code checks permissions, never role names.
/// </summary>
public readonly record struct Permission(string Id)
{
    public override string ToString() => Id;
}

/// <summary>Permissions referenced directly by policies. The full catalog lives in policies/permissions.json.</summary>
public static class KnownPermissions
{
    public static readonly Permission CustomerRead = new("customer.read");
    public static readonly Permission CustomerReadSelf = new("customer.read.self");
    public static readonly Permission CustomerReadAssigned = new("customer.read.assigned");
    public static readonly Permission CustomerReadSensitive = new("customer.read-sensitive");
    public static readonly Permission CustomerPaymentWrite = new("customer.payment.write");
    public static readonly Permission CreditRead = new("credit.read");
    public static readonly Permission CreditNoteWrite = new("credit.note.write");
    public static readonly Permission CreditApprove = new("credit.approve");
    public static readonly Permission CreditRequest = new("credit.request");
    public static readonly Permission AccountCash = new("account.cash");
    public static readonly Permission EmployeeRead = new("employee.read");
    public static readonly Permission EmployeeSalaryRead = new("employee.salary.read");
    public static readonly Permission OperationsRead = new("operations.read");
    public static readonly Permission AuditRead = new("audit.read");
    public static readonly Permission DiagnosticsRead = new("diagnostics.read");
    public static readonly Permission DocumentRead = new("document.read");
    public static readonly Permission DocumentIngest = new("document.ingest");
    public static readonly Permission DocumentAclManage = new("document.acl.manage");
    public static readonly Permission SystemConfigure = new("system.configure");
    public static readonly Permission AssistantUse = new("assistant.use");
}
