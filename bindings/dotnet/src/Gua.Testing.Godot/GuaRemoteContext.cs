using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Gua.Core;

namespace Gua.Testing.Godot;

public sealed class GuaRemoteContext : IGuaContext, IDisposable
{
    private readonly Uri _bridgeUri;
    private readonly TimeSpan _requestTimeout;
    private ClientWebSocket? _socket;
    private int _nextId = 1;
    private bool _disposed;

    public GuaRemoteContext(string bridgeUrl, TimeSpan requestTimeout)
    {
        _bridgeUri = new Uri(bridgeUrl);
        _requestTimeout = requestTimeout;
    }

    public GuaNodeState GetNodeState(string id)
    {
        var node = GetUiTree().FindNodeById(id)
            ?? throw new InvalidOperationException($"Gua node not found: {id}");
        return new GuaNodeState(node.Visible, node.Enabled);
    }

    public string GetUiTreeJson()
    {
        return RequestRawResult(new { type = "get_ui_tree" });
    }

    public string FindNodeById(string id)
    {
        return GetUiTree().FindNodeById(id)?.Id
            ?? throw new InvalidOperationException($"Gua node not found by id: {id}");
    }

    public string FindNodeByRole(string role, string? name = null)
    {
        var node = GetUiTree().Nodes.FirstOrDefault(node =>
            string.Equals(node.Role, role, StringComparison.Ordinal) &&
            (name is null || string.Equals(node.Label, name, StringComparison.Ordinal)));
        if (node is null)
        {
            throw new InvalidOperationException(name is null
                ? $"Gua node not found by role: {role}"
                : $"Gua node not found by role and name: {role}, {name}");
        }

        return node.Id;
    }

    public string FindNodeByText(string text)
    {
        var node = GetUiTree().Nodes.FirstOrDefault(node => string.Equals(node.Label, text, StringComparison.Ordinal));
        if (node is null)
        {
            throw new InvalidOperationException($"Gua node not found by text: {text}");
        }

        return node.Id;
    }

    public bool EnqueueClick(string id)
    {
        Request<object?>(new { type = "click_node", nodeId = id }, allowNullResult: true);
        return true;
    }

    public bool TryPollEvent(out GuaEvent e)
    {
        e = default;
        return false;
    }

    public void WaitUntilAvailable(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;
        do
        {
            try
            {
                GetUiTree();
                return;
            }
            catch (Exception error)
            {
                lastError = error;
                Thread.Sleep(100);
            }
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Timed out connecting to Gua bridge at {_bridgeUri}.", lastError);
    }

    public void WaitForScreen(string screen, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            var tree = GetUiTree();
            if (string.Equals(tree.Screen, screen, StringComparison.Ordinal) ||
                string.Equals(tree.Screen, screen.Replace("res://", "", StringComparison.Ordinal), StringComparison.Ordinal))
            {
                return;
            }

            Thread.Sleep(50);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Timed out waiting for Godot scene: {screen}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _socket?.Dispose();
    }

    private GuaRemoteUiTree GetUiTree()
    {
        return Request<GuaRemoteUiTree>(new { type = "get_ui_tree" });
    }

    private T Request<T>(object command, bool allowNullResult = false)
    {
        EnsureConnected();
        var id = _nextId++;
        var payload = JsonSerializer.Serialize(command, command.GetType());
        using var document = JsonDocument.Parse(payload);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        Send(output.ToArray());
        while (true)
        {
            var responseJson = ReceiveString();
            using var response = JsonDocument.Parse(responseJson);
            if (!response.RootElement.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id)
            {
                continue;
            }

            if (!response.RootElement.GetProperty("ok").GetBoolean())
            {
                throw new InvalidOperationException(response.RootElement.GetProperty("error").GetString());
            }

            var result = response.RootElement.GetProperty("result").Deserialize<T>(JsonOptions);
            if (result is null && !allowNullResult)
            {
                throw new InvalidOperationException("Gua bridge returned an empty result.");
            }

            return result!;
        }
    }

    private string RequestRawResult(object command)
    {
        EnsureConnected();
        var id = _nextId++;
        var payload = JsonSerializer.Serialize(command, command.GetType());
        using var document = JsonDocument.Parse(payload);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        Send(output.ToArray());
        while (true)
        {
            var responseJson = ReceiveString();
            using var response = JsonDocument.Parse(responseJson);
            if (!response.RootElement.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id)
            {
                continue;
            }

            if (!response.RootElement.GetProperty("ok").GetBoolean())
            {
                throw new InvalidOperationException(response.RootElement.GetProperty("error").GetString());
            }

            return response.RootElement.GetProperty("result").GetRawText();
        }
    }

    private void EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_socket?.State == WebSocketState.Open)
        {
            return;
        }

        using var cts = new CancellationTokenSource(_requestTimeout);
        _socket?.Dispose();
        _socket = new ClientWebSocket();
        try
        {
            _socket.ConnectAsync(_bridgeUri, cts.Token).GetAwaiter().GetResult();
        }
        catch
        {
            _socket.Dispose();
            _socket = null;
            throw;
        }
    }

    private void Send(byte[] payload)
    {
        using var cts = new CancellationTokenSource(_requestTimeout);
        var socket = _socket ?? throw new InvalidOperationException("Gua bridge WebSocket is not connected.");
        socket.SendAsync(payload, WebSocketMessageType.Text, true, cts.Token).GetAwaiter().GetResult();
    }

    private string ReceiveString()
    {
        using var cts = new CancellationTokenSource(_requestTimeout);
        var buffer = new byte[65536];
        using var stream = new MemoryStream();
        while (true)
        {
            var socket = _socket ?? throw new InvalidOperationException("Gua bridge WebSocket is not connected.");
            var result = socket.ReceiveAsync(buffer, cts.Token).GetAwaiter().GetResult();
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("Gua bridge WebSocket closed.");
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
