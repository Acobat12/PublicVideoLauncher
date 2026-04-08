using System;
using System.Net;
using System.Net.Sockets;

namespace PublicVideoLauncher.Services;

public static class PortHelper
{
    public static bool TestPortBindable(int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { listener?.Stop(); } catch { }
        }
    }

    public static int GetFreeTcpPort(int preferredPort = 8000, int maxAttempts = 50)
    {
        if (TestPortBindable(preferredPort))
            return preferredPort;

        for (var port = preferredPort + 1; port < preferredPort + maxAttempts; port++)
        {
            if (TestPortBindable(port))
                return port;
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public static bool TestTcpPortOpen(string hostName, int portNumber, int timeoutMs = 1500)
    {
        using var client = new TcpClient();
        try
        {
            var asyncResult = client.BeginConnect(hostName, portNumber, null, null);
            var ok = asyncResult.AsyncWaitHandle.WaitOne(timeoutMs, false);
            if (!ok)
                return false;

            client.EndConnect(asyncResult);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
