using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    public async Task<T> EvaluateAsync<T>(string expression, object? arg = null)
    {
        if (_mainFrameId is null)
            throw new InvalidOperationException("Page is not initialized.");

        return await EvaluateInFrameAsync<T>(_mainFrameId, expression, arg).ConfigureAwait(false);
    }

    public async Task<IJSHandle> EvaluateHandleAsync(string expression, object? arg = null)
    {
        var contextId = _mainFrameId is not null
            ? GetExecutionContextId(_mainFrameId)
            : null;

        var result = await _session.SendAsync(
            "Runtime.evaluate",
            new RuntimeEvaluateParams(
                Expression: WrapExpression(expression, arg),
                ReturnByValue: false,
                AwaitPromise: true,
                ContextId: contextId),
            CdpJsonContext.Default.RuntimeEvaluateParams,
            CdpJsonContext.Default.RuntimeEvaluateResult,
            CancellationToken.None).ConfigureAwait(false);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"Evaluation failed: {result.ExceptionDetails.Text}");

        if (result.Result.ObjectId is null)
            throw new InvalidOperationException(
                "Expression did not return an object. Use EvaluateAsync<T> for primitive values.");

        return new JsHandle(_session, result.Result.ObjectId);
    }

    public async Task<T> WaitForFunctionAsync<T>(string expression, object? arg = null, double? timeout = null)
    {
        if (_mainFrameId is null)
            throw new InvalidOperationException("Page is not initialized.");

        return await WaitForFunctionInFrameAsync<T>(_mainFrameId, expression, arg, timeout).ConfigureAwait(false);
    }
}
