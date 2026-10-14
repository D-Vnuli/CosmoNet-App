using System.Threading;

namespace CosmoNet.App.Models;

public enum VpnConnectionPhase
{
    Disconnected,
    Connecting,
    Verifying,
    Connected,
    Disconnecting,
    Error
}

public sealed class VpnConnectionLifecycle
{
    private int _unexpectedExitHandled;

    public VpnConnectionPhase Phase { get; private set; } = VpnConnectionPhase.Disconnected;

    public void BeginConnection()
    {
        Interlocked.Exchange(ref _unexpectedExitHandled, 0);
        Phase = VpnConnectionPhase.Connecting;
    }

    public void BeginVerification() => Phase = VpnConnectionPhase.Verifying;

    public bool TryMarkConnected(bool processRunning, bool connectivityVerified)
    {
        if (!processRunning || !connectivityVerified || Phase != VpnConnectionPhase.Verifying)
        {
            Phase = VpnConnectionPhase.Error;
            return false;
        }

        Phase = VpnConnectionPhase.Connected;
        return true;
    }

    public void MarkError() => Phase = VpnConnectionPhase.Error;

    public void BeginDisconnect() => Phase = VpnConnectionPhase.Disconnecting;

    public void MarkDisconnected() => Phase = VpnConnectionPhase.Disconnected;

    public bool TryHandleUnexpectedExit()
    {
        if (Phase is not (VpnConnectionPhase.Connecting or VpnConnectionPhase.Verifying or VpnConnectionPhase.Connected)
            || Interlocked.Exchange(ref _unexpectedExitHandled, 1) != 0)
        {
            return false;
        }

        Phase = VpnConnectionPhase.Error;
        return true;
    }
}
