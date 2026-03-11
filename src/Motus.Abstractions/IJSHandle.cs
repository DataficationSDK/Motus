namespace Motus.Abstractions;

/// <summary>
/// Represents a handle to a JavaScript object in the page.
/// </summary>
public interface IJSHandle : IAsyncDisposable
{
    /// <summary>
    /// Evaluates a JavaScript expression in the context of this handle.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="expression">The JavaScript expression to evaluate.</param>
    /// <param name="arg">Optional argument to pass to the expression.</param>
    /// <returns>The result of the evaluation.</returns>
    Task<T> EvaluateAsync<T>(string expression, object? arg = null);

    /// <summary>
    /// Gets a property of the JavaScript object by name.
    /// </summary>
    /// <param name="propertyName">The property name.</param>
    /// <returns>A handle to the property value.</returns>
    Task<IJSHandle> GetPropertyAsync(string propertyName);

    /// <summary>
    /// Returns the JSON representation of the object.
    /// </summary>
    /// <typeparam name="T">The expected deserialized type.</typeparam>
    /// <returns>The deserialized JSON value.</returns>
    Task<T> JsonValueAsync<T>();
}
