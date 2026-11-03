using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace PlaymoxAPI.Services.Platform;

public class SshWebSocketBridgeService
{
    private readonly ILogger<SshWebSocketBridgeService> logger;

    public SshWebSocketBridgeService(ILogger<SshWebSocketBridgeService> logger)
    {
        this.logger = logger;
    }

    public async Task ProxyAsync(WebSocket socket, SshSessionAccess access, CancellationToken cancellationToken)
    {
        SshAuthenticationException? authFailure = null;
        SshConnectionException? connectionFailure = null;
        var hosts = access.CandidateHosts
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (hosts.Count == 0 && !string.IsNullOrWhiteSpace(access.Host))
        {
            hosts.Add(access.Host.Trim());
        }

        try
        {
            foreach (var host in hosts)
            {
                try
                {
                    using var ssh = CreateSshClient(host, access.Username, access.Password);
                    ssh.Connect();
                    using var shell = ssh.CreateShellStream("xterm", 120, 40, 1024, 768, 4096);
                    await SendTextAsync(socket, $"\r\n[Playmox] Connected to VM {access.VmId}\r\n\r\n", cancellationToken);

                    var shellToWsTask = PumpShellToWebSocketAsync(shell, socket, cancellationToken);
                    var wsToShellTask = PumpWebSocketToShellAsync(shell, socket, cancellationToken);
                    await Task.WhenAny(shellToWsTask, wsToShellTask);
                    return;
                }
                catch (SshAuthenticationException ex)
                {
                    authFailure = ex;
                    logger.LogWarning(ex, "SSH auth failed vmId={VmId} host={Host}", access.VmId, host);
                }
                catch (SshConnectionException ex)
                {
                    connectionFailure = ex;
                    logger.LogWarning(ex, "SSH connection failed vmId={VmId} host={Host}", access.VmId, host);
                }
            }

            if (authFailure != null)
            {
                await SendTextAsync(
                    socket,
                    $"[Playmox] SSH authentication failed.\r\n{authFailure.Message} {authFailure.InnerException?.Message}",
                    cancellationToken);
                return;
            }

            if (connectionFailure != null)
            {
                await SendTextAsync(socket, "[Playmox] SSH connection failed.\r\n", cancellationToken);
                return;
            }

            await SendTextAsync(socket, "[Playmox] SSH connection failed.\r\n", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WebSocket SSH proxy failed vmId={VmId}", access.VmId);
            await SendTextAsync(socket, $"[Playmox] SSH proxy error: {ex.Message}\r\n", cancellationToken);
        }
        finally
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session closed", CancellationToken.None);
            }
        }
    }

    private static async Task PumpWebSocketToShellAsync(ShellStream shell, WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (!cancellationToken.IsCancellationRequested &&
               (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived))
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (!string.IsNullOrEmpty(text))
            {
                shell.Write(text);
                shell.Flush();
            }
        }
    }

    private static async Task PumpShellToWebSocketAsync(ShellStream shell, WebSocket socket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested &&
               (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived))
        {
            if (shell.DataAvailable)
            {
                var data = shell.Read();
                if (!string.IsNullOrEmpty(data))
                {
                    await SendTextAsync(socket, data, cancellationToken);
                }
            }
            else
            {
                await Task.Delay(25, cancellationToken);
            }
        }
    }

    private static async Task SendTextAsync(WebSocket socket, string text, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open && socket.State != WebSocketState.CloseReceived)
        {
            return;
        }

        var payload = Encoding.UTF8.GetBytes(text);
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static SshClient CreateSshClient(string host, string username, string password)
    {
        var client = new SshClient(BuildConnectionInfo(host, username, password));
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
        client.KeepAliveInterval = TimeSpan.FromSeconds(15);
        return client;
    }

    private static Renci.SshNet.ConnectionInfo BuildConnectionInfo(string host, string username, string password)
    {
        var passwordMethod = new PasswordAuthenticationMethod(username, password);
        var keyboardMethod = new KeyboardInteractiveAuthenticationMethod(username);
        keyboardMethod.AuthenticationPrompt += (_, eventArgs) =>
        {
            foreach (var prompt in eventArgs.Prompts)
            {
                prompt.Response = password;
            }
        };

        return new Renci.SshNet.ConnectionInfo(host, 22, username, passwordMethod, keyboardMethod)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
}
