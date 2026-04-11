using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Represents a handle to a JavaScript object in the browser.
/// </summary>
internal class JsHandle : IJSHandle
{
    private readonly IMotusSession _session;
    private readonly string _objectId;
    private bool _disposed;

    internal JsHandle(IMotusSession session, string objectId)
    {
        _session = session;
        _objectId = objectId;
    }

    internal string ObjectId => _objectId;

    internal IMotusSession SessionInternal => _session;

    public async Task<T> EvaluateAsync<T>(string expression, object? arg = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var args = arg is not null
            ? new[] { new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(arg)) }
            : (RuntimeCallArgument[]?)null;

        var result = await _session.SendAsync(
            "Runtime.callFunctionOn",
            new RuntimeCallFunctionOnParams(
                FunctionDeclaration: expression,
                ObjectId: _objectId,
                Arguments: args,
                ReturnByValue: true,
                AwaitPromise: true),
            CdpJsonContext.Default.RuntimeCallFunctionOnParams,
            CdpJsonContext.Default.RuntimeCallFunctionOnResult,
            CancellationToken.None).ConfigureAwait(false);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"Evaluation failed: {result.ExceptionDetails.Text}");

        return DeserializeValue<T>(result.Result);
    }

    public async Task<IJSHandle> GetPropertyAsync(string propertyName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var result = await _session.SendAsync(
            "Runtime.callFunctionOn",
            new RuntimeCallFunctionOnParams(
                FunctionDeclaration: "function(name) { return this[name]; }",
                ObjectId: _objectId,
                Arguments: [new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(propertyName))],
                ReturnByValue: false,
                AwaitPromise: false),
            CdpJsonContext.Default.RuntimeCallFunctionOnParams,
            CdpJsonContext.Default.RuntimeCallFunctionOnResult,
            CancellationToken.None).ConfigureAwait(false);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"GetProperty failed: {result.ExceptionDetails.Text}");

        if (result.Result.ObjectId is null)
            throw new InvalidOperationException("Property is not an object and cannot be wrapped in a handle.");

        return new JsHandle(_session, result.Result.ObjectId);
    }

    public async Task<T> JsonValueAsync<T>()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var result = await _session.SendAsync(
            "Runtime.callFunctionOn",
            new RuntimeCallFunctionOnParams(
                FunctionDeclaration: "function() { return this; }",
                ObjectId: _objectId,
                ReturnByValue: true,
                AwaitPromise: false),
            CdpJsonContext.Default.RuntimeCallFunctionOnParams,
            CdpJsonContext.Default.RuntimeCallFunctionOnResult,
            CancellationToken.None).ConfigureAwait(false);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"JsonValue failed: {result.ExceptionDetails.Text}");

        return DeserializeValue<T>(result.Result);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            await _session.SendAsync(
                "Runtime.releaseObject",
                new RuntimeReleaseObjectParams(_objectId),
                CdpJsonContext.Default.RuntimeReleaseObjectParams,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is CdpDisconnectedException or MotusTargetClosedException)
        {
            // Object already gone if browser disconnected
        }
    }

    protected static T DeserializeValue<T>(RuntimeRemoteObject remoteObject)
    {
        if (remoteObject.Value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null)
                return default!;
            return element.Deserialize<T>()
                   ?? throw new InvalidOperationException("Deserialization returned null.");
        }

        if (remoteObject.Type == "undefined" || remoteObject.Subtype == "null")
            return default!;

        throw new InvalidOperationException(
            $"Cannot deserialize remote object of type '{remoteObject.Type}' to {typeof(T).Name}.");
    }
}
