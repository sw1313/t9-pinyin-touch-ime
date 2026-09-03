using T9Pane.Native;

namespace T9Pane.Services;

internal static class UiAccessToken
{
    public static bool Has()
    {
        if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), NativeMethods.TokenQuery, out var token))
        {
            return false;
        }

        try
        {
            return NativeMethods.GetTokenInformation(token, NativeMethods.TokenUiAccess, out var value, sizeof(int), out _)
                   && value != 0;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }
}
