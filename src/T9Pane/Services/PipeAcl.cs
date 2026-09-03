using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace T9Pane.Services;

internal static class PipeAcl
{
    public const string AllAppPackages = "S-1-15-2-1";
    public const string NotificationPipe = "T9Pane.Ime";
    public const string CommandPipe = "T9Pane.Ime.Cmd";
    public const string AppContainerNotificationPipe = @"LOCAL\T9Pane.Ime";
    public const string AppContainerCommandPipe = @"LOCAL\T9Pane.Ime.Cmd";

    public static PipeSecurity ForHostAndAppContainer()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(AllAppPackages),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    public static bool AllowsAppContainer(PipeSecurity security) =>
        security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .Any(rule =>
                rule.AccessControlType == AccessControlType.Allow
                && rule.IdentityReference.Value.Equals(AllAppPackages, StringComparison.OrdinalIgnoreCase));
}
