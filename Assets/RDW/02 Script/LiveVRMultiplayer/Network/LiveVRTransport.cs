using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

public static class LiveVRTransport
{
    public static bool TryCreateHostSocket(int port, out UdpClient socket, out string error)
    {
        socket = null;
        error = string.Empty;
        try
        {
            socket = new UdpClient(port);
            socket.EnableBroadcast = true;
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    public static void Send(UdpClient socket, string message, IPEndPoint endpoint)
    {
        if (socket == null || endpoint == null || string.IsNullOrEmpty(message))
            return;

        byte[] data = Encoding.UTF8.GetBytes(message);
        socket.Send(data, data.Length, endpoint);
    }
}
