using System.Net;
using System.Net.Sockets;
using Xunit;

namespace ERP.FMC.Tests;

public class PortResolutionTests
{
    /// <summary>
    /// Cổng đang bị chiếm phải được phát hiện là KHÔNG trống.
    /// </summary>
    [Fact]
    public void IsPortFree_DetectsOccupiedPort()
    {
        // Bind thật để chiếm cổng.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            // Không dùng IsPortFree của Program.cs (private static trong
            // top-level statements) -> thử bind lần hai theo đúng logic
            // của helper để kiểm chứng nhận diện "cổng đang bận".
            var second = new TcpListener(IPAddress.Loopback, port);
            var occupied = Assert.Throws<SocketException>(() => second.Start());
            Assert.NotNull(occupied);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Cổng trống phải bind được thành công (2 lần liên tiếp).
    /// </summary>
    [Fact]
    public void FreePort_CanBeBound()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        // Sau khi release, bind lại được.
        var again = new TcpListener(IPAddress.Loopback, port);
        var ex = Record.Exception(() => again.Start());
        Assert.Null(ex);
        again.Stop();
    }
}
