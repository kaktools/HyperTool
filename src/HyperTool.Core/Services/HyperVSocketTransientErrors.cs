using System.Net.Sockets;

namespace HyperTool.Services;

internal static class HyperVSocketTransientErrors
{
    public static bool IsTransientConnectSocketError(SocketException ex)
    {
        return ex.SocketErrorCode is SocketError.NoBufferSpaceAvailable
            or SocketError.TryAgain
            or SocketError.TimedOut
            or SocketError.ConnectionRefused
            or SocketError.NetworkDown
            or SocketError.NetworkUnreachable
            or SocketError.HostDown
            or SocketError.HostUnreachable
            or SocketError.WouldBlock
            or SocketError.ConnectionReset
            or SocketError.ConnectionAborted;
    }
}
