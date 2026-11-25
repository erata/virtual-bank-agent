using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Policies;

namespace VirtualBank.Specifications.Tests;

/// <summary>
/// Design intent for modules that do not exist yet. These policies are NOT registered by any running service;
/// they only keep the planned matrix cases consistent with the permission catalog and the realm. When a module
/// ships, its policies move into that module and the matching cases switch to <c>status: active</c>.
/// </summary>
internal static class PlannedPolicies
{
    public static IReadOnlyList<IResourcePolicy> Create() =>
    [
        // hr (M6)
        new PermissionPolicy("employee.read.v1", "employee", KnownPermissions.EmployeeRead, DisclosureMode.None),
        new PermissionPolicy("employee.salary.read.v1", "employee", KnownPermissions.EmployeeSalaryRead, DisclosureMode.None),
        // operations (M6)
        new PermissionPolicy("operations.read.v1", "operations", KnownPermissions.OperationsRead, DisclosureMode.Generic),
        // audit (M6)
        new PermissionPolicy("audit.read.v1", "audit", KnownPermissions.AuditRead, DisclosureMode.Generic),
        new PermissionPolicy("diagnostics.read.v1", "diagnostics", KnownPermissions.DiagnosticsRead, DisclosureMode.Generic),
        // platform (M6)
        new PermissionPolicy("system.configure.v1", "platform", KnownPermissions.SystemConfigure, DisclosureMode.Generic),
    ];
}
