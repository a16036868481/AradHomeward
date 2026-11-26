using System;

internal static class LoginManagementPort
{
    internal static readonly int Value = Read();

    private static int Read()
    {
        string value = Environment.GetEnvironmentVariable("ARAD_LOCAL_MANAGEMENT_PORT");
        if (String.IsNullOrEmpty(value)) return 8080;
        int port;
        if (Int32.TryParse(value, out port) && (port == 8080 || (port >= 18080 && port <= 18095))) return port;
        throw new InvalidOperationException("Invalid local management port.");
    }
}
