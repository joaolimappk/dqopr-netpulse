using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Core.Monitoring;

public interface INetworkEnvironmentService
{
    Task<NetworkInterfaceSnapshot> GetActiveInterfaceAsync(CancellationToken cancellationToken);
}
