using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

internal sealed class ElementHandle : JsHandle, IElementHandle
{
    internal ElementHandle(IMotusSession session, string objectId)
        : base(session, objectId)
    {
    }

    public async Task<string?> GetAttributeAsync(string name, CancellationToken ct = default)
    {
        var result = await SessionInternal.SendAsync(
            "Runtime.callFunctionOn",
            new RuntimeCallFunctionOnParams(
                FunctionDeclaration: "function(name) { return this.getAttribute(name); }",
                ObjectId: ObjectId,
                Arguments: [new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(name))],
                ReturnByValue: true,
                AwaitPromise: false),
            CdpJsonContext.Default.RuntimeCallFunctionOnParams,
            CdpJsonContext.Default.RuntimeCallFunctionOnResult,
            ct).ConfigureAwait(false);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"GetAttribute failed: {result.ExceptionDetails.Text}");

        if (result.Result.Type == "object" && result.Result.Subtype == "null")
            return null;

        return DeserializeValue<string?>(result.Result);
    }

    public async Task<string?> TextContentAsync(CancellationToken ct = default)
    {
        var result = await SessionInternal.SendAsync(
            "Runtime.callFunctionOn",
            new RuntimeCallFunctionOnParams(
                FunctionDeclaration: "function() { return this.textContent; }",
                ObjectId: ObjectId,
                ReturnByValue: true,
                AwaitPromise: false),
            CdpJsonContext.Default.RuntimeCallFunctionOnParams,
            CdpJsonContext.Default.RuntimeCallFunctionOnResult,
            ct).ConfigureAwait(false);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"TextContent failed: {result.ExceptionDetails.Text}");

        if (result.Result.Type == "object" && result.Result.Subtype == "null")
            return null;

        return DeserializeValue<string?>(result.Result);
    }

    public async Task<BoundingBox?> BoundingBoxAsync(CancellationToken ct = default)
    {
        var result = await SessionInternal.SendAsync(
            "Runtime.callFunctionOn",
            new RuntimeCallFunctionOnParams(
                FunctionDeclaration: """
                    function() {
                        var r = this.getBoundingClientRect();
                        if (r.width === 0 && r.height === 0) return null;
                        return { x: r.x, y: r.y, width: r.width, height: r.height };
                    }
                    """,
                ObjectId: ObjectId,
                ReturnByValue: true,
                AwaitPromise: false),
            CdpJsonContext.Default.RuntimeCallFunctionOnParams,
            CdpJsonContext.Default.RuntimeCallFunctionOnResult,
            ct).ConfigureAwait(false);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"BoundingBox failed: {result.ExceptionDetails.Text}");

        if (result.Result.Type == "object" && result.Result.Subtype == "null")
            return null;

        if (result.Result.Value is JsonElement element)
            return element.Deserialize(CdpJsonContext.Default.BoundingBox);

        return null;
    }
}
