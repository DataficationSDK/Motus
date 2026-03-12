namespace Motus.Abstractions;

/// <summary>
/// Base exception for all Motus engine errors. Provides an optional screenshot
/// captured at the time of failure for diagnostics.
/// </summary>
public class MotusException : Exception
{
    public byte[]? Screenshot { get; set; }

    public MotusException(string message) : base(message) { }
    public MotusException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when an operation exceeds its configured timeout.
/// </summary>
public class MotusTimeoutException : MotusException
{
    public TimeSpan TimeoutDuration { get; }

    public MotusTimeoutException(TimeSpan timeoutDuration, string message)
        : base(message)
    {
        TimeoutDuration = timeoutDuration;
    }

    public MotusTimeoutException(TimeSpan timeoutDuration, string message, Exception? innerException)
        : base(message, innerException)
    {
        TimeoutDuration = timeoutDuration;
    }
}

/// <summary>
/// Thrown when a page navigation exceeds its timeout.
/// </summary>
public class NavigationTimeoutException : MotusTimeoutException
{
    public string Url { get; }
    public IReadOnlyList<string>? LastNetworkEvents { get; }

    public NavigationTimeoutException(string url, TimeSpan timeoutDuration,
        IReadOnlyList<string>? lastNetworkEvents, string message)
        : base(timeoutDuration, message)
    {
        Url = url;
        LastNetworkEvents = lastNetworkEvents;
    }

    public NavigationTimeoutException(string url, TimeSpan timeoutDuration,
        IReadOnlyList<string>? lastNetworkEvents, string message, Exception? innerException)
        : base(timeoutDuration, message, innerException)
    {
        Url = url;
        LastNetworkEvents = lastNetworkEvents;
    }
}

/// <summary>
/// Thrown when a wait condition (WaitForAsync, WaitForCondition, WaitForURL) exceeds its timeout.
/// </summary>
public class WaitTimeoutException : MotusTimeoutException
{
    public string Condition { get; }
    public string? LastEvaluatedValue { get; }

    public WaitTimeoutException(string condition, TimeSpan timeoutDuration,
        string? lastEvaluatedValue, string message)
        : base(timeoutDuration, message)
    {
        Condition = condition;
        LastEvaluatedValue = lastEvaluatedValue;
    }

    public WaitTimeoutException(string condition, TimeSpan timeoutDuration,
        string? lastEvaluatedValue, string message, Exception? innerException)
        : base(timeoutDuration, message, innerException)
    {
        Condition = condition;
        LastEvaluatedValue = lastEvaluatedValue;
    }
}

/// <summary>
/// Thrown when an actionability check (visible, enabled, stable, etc.) times out
/// before the element reaches the required state.
/// </summary>
public class ActionTimeoutException : MotusTimeoutException
{
    public string Selector { get; }
    public string FailedCheckName { get; }
    public string? ElementState { get; }
    public string PageUrl { get; }

    public ActionTimeoutException(string selector, string failedCheckName,
        string? elementState, string pageUrl, TimeSpan timeoutDuration, string message)
        : base(timeoutDuration, message)
    {
        Selector = selector;
        FailedCheckName = failedCheckName;
        ElementState = elementState;
        PageUrl = pageUrl;
    }

    public ActionTimeoutException(string selector, string failedCheckName,
        string? elementState, string pageUrl, TimeSpan timeoutDuration,
        string message, Exception? innerException)
        : base(timeoutDuration, message, innerException)
    {
        Selector = selector;
        FailedCheckName = failedCheckName;
        ElementState = elementState;
        PageUrl = pageUrl;
    }
}

/// <summary>
/// Base exception for selector-related errors.
/// </summary>
public class MotusSelectorException : MotusException
{
    public string Selector { get; }
    public string PageUrl { get; }

    public MotusSelectorException(string selector, string pageUrl, string message)
        : base(message)
    {
        Selector = selector;
        PageUrl = pageUrl;
    }

    public MotusSelectorException(string selector, string pageUrl, string message, Exception? innerException)
        : base(message, innerException)
    {
        Selector = selector;
        PageUrl = pageUrl;
    }
}

/// <summary>
/// Thrown when no element matches the given selector.
/// </summary>
public class ElementNotFoundException : MotusSelectorException
{
    public string? DomSnapshot { get; }

    public ElementNotFoundException(string selector, string pageUrl, string? domSnapshot = null)
        : base(selector, pageUrl, $"No element found for selector: {selector}")
    {
        DomSnapshot = domSnapshot;
    }

    public ElementNotFoundException(string selector, string pageUrl, string message, Exception? innerException)
        : base(selector, pageUrl, message, innerException)
    {
    }
}

/// <summary>
/// Thrown when a selector matches more elements than expected for strict mode.
/// </summary>
public class AmbiguousSelectorException : MotusSelectorException
{
    public int MatchedCount { get; }

    public AmbiguousSelectorException(string selector, string pageUrl, int matchedCount)
        : base(selector, pageUrl, $"Selector '{selector}' matched {matchedCount} elements, expected one.")
    {
        MatchedCount = matchedCount;
    }

    public AmbiguousSelectorException(string selector, string pageUrl, int matchedCount,
        string message, Exception? innerException)
        : base(selector, pageUrl, message, innerException)
    {
        MatchedCount = matchedCount;
    }
}

/// <summary>
/// Thrown when a page navigation fails with an error (e.g. DNS resolution failure).
/// </summary>
public class MotusNavigationException : MotusException
{
    public string Url { get; }
    public string ErrorCode { get; }
    public string PageUrl { get; }

    public MotusNavigationException(string url, string errorCode, string pageUrl)
        : base($"Navigation failed: {errorCode}")
    {
        Url = url;
        ErrorCode = errorCode;
        PageUrl = pageUrl;
    }

    public MotusNavigationException(string url, string errorCode, string pageUrl,
        string message, Exception? innerException)
        : base(message, innerException)
    {
        Url = url;
        ErrorCode = errorCode;
        PageUrl = pageUrl;
    }
}

/// <summary>
/// Thrown when the browser target (page, frame, worker) has been closed or destroyed.
/// </summary>
public class MotusTargetClosedException : MotusException
{
    public string TargetType { get; }
    public string? TargetId { get; }

    public MotusTargetClosedException(string targetType, string? targetId, string message)
        : base(message)
    {
        TargetType = targetType;
        TargetId = targetId;
    }

    public MotusTargetClosedException(string targetType, string? targetId,
        string message, Exception? innerException)
        : base(message, innerException)
    {
        TargetType = targetType;
        TargetId = targetId;
    }
}

/// <summary>
/// Thrown when a test assertion made through the Motus assertion API fails.
/// </summary>
public class MotusAssertionException : MotusException
{
    public string? Expected { get; }
    public string? Actual { get; }
    public string? Selector { get; }
    public string? PageUrl { get; }
    public TimeSpan AssertionTimeout { get; }

    public MotusAssertionException(string? expected, string? actual, string? selector,
        string? pageUrl, TimeSpan assertionTimeout, string message)
        : base(message)
    {
        Expected = expected;
        Actual = actual;
        Selector = selector;
        PageUrl = pageUrl;
        AssertionTimeout = assertionTimeout;
    }

    public MotusAssertionException(string? expected, string? actual, string? selector,
        string? pageUrl, TimeSpan assertionTimeout, string message, Exception? innerException)
        : base(message, innerException)
    {
        Expected = expected;
        Actual = actual;
        Selector = selector;
        PageUrl = pageUrl;
        AssertionTimeout = assertionTimeout;
    }
}

/// <summary>
/// Thrown when the CDP protocol layer returns an error or fails to communicate.
/// </summary>
public class MotusProtocolException : MotusException
{
    public int? CdpErrorCode { get; }
    public string? CommandSent { get; }

    public MotusProtocolException(int? cdpErrorCode, string? commandSent, string message)
        : base(message)
    {
        CdpErrorCode = cdpErrorCode;
        CommandSent = commandSent;
    }

    public MotusProtocolException(int? cdpErrorCode, string? commandSent,
        string message, Exception? innerException)
        : base(message, innerException)
    {
        CdpErrorCode = cdpErrorCode;
        CommandSent = commandSent;
    }
}
