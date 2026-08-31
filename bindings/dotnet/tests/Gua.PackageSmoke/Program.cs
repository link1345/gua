using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Gua.Core;
using Gua.Runtime;

using (var context = new GuaContext())
{
    context.BeginFrame("package-smoke");
    context.RegisterNode("ready", "status", "Ready", new GuaBounds(0, 0, 1, 1));
    context.EndFrame();
    if (!context.GetUiTreeJson().Contains("package-smoke", StringComparison.Ordinal))
        throw new InvalidOperationException("Gua.Core native asset did not return the expected tree.");
}

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();

using var runtime = new GuaRuntime();
runtime.BeginFrame("package-smoke");
runtime.RegisterNode(new GuaNodeDescriptor("ready", "status", "Ready", new GuaBounds(0, 0, 1, 1)));
runtime.EndFrame();
if (!runtime.StartInspectorBridge(port))
    throw new InvalidOperationException("Gua.Runtime native asset could not start the Inspector bridge.");

try
{
    using var socket = new ClientWebSocket();
    await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), CancellationToken.None);
    _ = await ReceiveTextAsync(socket); // Initial snapshot.

    var command = Encoding.UTF8.GetBytes("{\"id\":1,\"type\":\"get_version\"}");
    await socket.SendAsync(command, WebSocketMessageType.Text, true, CancellationToken.None);
    using var response = JsonDocument.Parse(await ReceiveTextAsync(socket));
    if (!response.RootElement.GetProperty("ok").GetBoolean() || response.RootElement.GetProperty("id").GetInt32() != 1)
        throw new InvalidOperationException("Packaged native bridge returned an invalid response.");
}
finally
{
    runtime.StopInspectorBridge();
}

Console.WriteLine($"Gua package smoke passed on {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}.");

static async Task<string> ReceiveTextAsync(ClientWebSocket socket)
{
    var buffer = new byte[64 * 1024];
    using var stream = new MemoryStream();
    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close)
            throw new InvalidOperationException("Bridge closed before returning a response.");
        stream.Write(buffer, 0, result.Count);
        if (result.EndOfMessage) return Encoding.UTF8.GetString(stream.ToArray());
    }
}
