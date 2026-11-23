using AV.Orch.Core.Models;

namespace AV.Orch.Security;

public static class PermissionPolicyGuard
{
    public static string ToPyRevitPolicyArgument(PermissionPolicy policy) => policy switch
    {
        PermissionPolicy.ReadOnly => "readonly",
        PermissionPolicy.AskBeforeModifications => "ask",
        _ => "readonly"
    };

    public static PermissionPolicy FromPyRevitPolicyString(string? rawPolicy)
    {
        if (string.Equals(rawPolicy?.Trim(), "ask", StringComparison.OrdinalIgnoreCase))
        {
            return PermissionPolicy.AskBeforeModifications;
        }

        // Default to the safest policy (ReadOnly) for unknown, missing, or unsupported values
        return PermissionPolicy.ReadOnly;
    }

    public static OperationResult ValidatePolicyTransition(
        PermissionPolicy currentPolicy,
        PermissionPolicy targetPolicy,
        bool userExplicitlyApproved)
    {
        if (currentPolicy == PermissionPolicy.ReadOnly &&
            targetPolicy == PermissionPolicy.AskBeforeModifications &&
            !userExplicitlyApproved)
        {
            return OperationResult.Fail(
                "Switching from Read-Only to Ask Before Modifications requires explicit user approval.",
                "APPROVAL_REQUIRED",
                "Confirm the permission change in the Permissions view.");
        }

        return OperationResult.Ok($"Permission policy transition to '{targetPolicy}' approved.");
    }

    public static bool IsUnsupportedAutonomousPolicy(string? rawPolicy) =>
        string.Equals(rawPolicy?.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
}

public static class TrustBoundaryDisclosure
{
    public const string ModelDataConfidentialityNotice =
        "Model Data Notice: Information read from Autodesk Revit (element names, parameter values, levels, and query results) " +
        "is transmitted directly from your local machine to the AI provider you connect (OpenAI, Google, or Anthropic) under your own account terms. " +
        "Artificial Vision, Inc. does not receive, proxy, or upload your Revit models. " +
        "pyRevit also stores local agent run records in plaintext under %APPDATA%\\pyRevit\\agent for up to 14 days.";

    public const string PythonExecutionRiskNotice =
        "Execution Boundary Notice: pyRevit's Read-Only policy rolls back Autodesk Revit model changes, but it does NOT sandbox Python " +
        "at the operating-system level. Scripts executed by the pyRevit agent host run with your Windows user privileges. " +
        "Revit transaction rollback only reverts Revit model changes and does NOT undo external file, database, or network side effects.";

    public const string NativeApprovalNotice =
        "Modification Approval: When 'Ask Before Modifications' is enabled, pyRevit displays an approval prompt inside Autodesk Revit " +
        "highlighting affected elements so you can Keep (single undo entry) or Discard (rollback) every change.";
}
