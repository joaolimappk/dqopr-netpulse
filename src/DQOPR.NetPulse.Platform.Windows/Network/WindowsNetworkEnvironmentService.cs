using System.Net.NetworkInformation;
using System.Net.Sockets;
using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Core.Monitoring;

namespace DQOPR.NetPulse.Platform.Windows.Network;

public sealed class WindowsNetworkEnvironmentService : INetworkEnvironmentService
{
    public Task<NetworkInterfaceSnapshot> GetActiveInterfaceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var active = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .Select(adapter => new { Adapter = adapter, Properties = adapter.GetIPProperties() })
            .Where(item => item.Properties.GatewayAddresses.Any(gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork))
            .OrderByDescending(item => item.Adapter.Speed)
            .FirstOrDefault();

        if (active is null)
        {
            return Task.FromResult(new NetworkInterfaceSnapshot(DateTimeOffset.UtcNow, null, null, null, null, false, null));
        }

        var gatewayAddress = active.Properties.GatewayAddresses
            .FirstOrDefault(gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork)
            ?.Address
            .ToString();
        var localAddress = active.Properties.UnicastAddresses
            .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            ?.Address
            .ToString();

        return Task.FromResult(new NetworkInterfaceSnapshot(
            DateTimeOffset.UtcNow,
            active.Adapter.Name,
            active.Adapter.Description,
            gatewayAddress,
            localAddress,
            true,
            active.Adapter.NetworkInterfaceType.ToString()));
    }
}
