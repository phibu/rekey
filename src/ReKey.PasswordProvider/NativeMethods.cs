using System.Runtime.InteropServices;

namespace ReKey.PasswordProvider;

/// <summary>
/// P/Invoke declarations for Win32 authentication calls.
/// See http://support.microsoft.com/kb/155012
/// </summary>
public class NativeMethods
{
    // Error codes returned by LogonUser / GetLastWin32Error
    internal const int ErrorPasswordMustChange = 1907;
    internal const int ErrorPasswordExpired = 1330;

    internal enum LogonTypes : uint
    {
        Interactive = 2,
        Network = 3,
        Service = 5,
    }

    internal enum LogonProviders : uint
    {
        Default = 0,
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool LogonUser(
        string principal,
        string authority,
        string password,
        LogonTypes logonType,
        LogonProviders logonProvider,
        out IntPtr token);
}
