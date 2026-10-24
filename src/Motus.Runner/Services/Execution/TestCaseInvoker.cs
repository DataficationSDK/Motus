using System.Diagnostics;
using System.Reflection;

namespace Motus.Runner.Services.Execution;

/// <summary>
/// The outcome of one test case run by <see cref="TestCaseInvoker"/>.
/// </summary>
/// <param name="Failure">The exception that failed the test, or null when it passed.</param>
/// <param name="Duration">The time from creating the test instance to the end of the test method,
/// not counting cleanup.</param>
internal sealed record TestCaseRunResult(Exception? Failure, TimeSpan Duration);

/// <summary>
/// Runs one test case by reflection the way its framework would: creates the instance, gives an
/// MSTest test its <c>TestContext</c>, runs setup base class first, runs the test with its data
/// row, then runs cleanup derived class first. Shared by <c>motus run</c> and the visual runner.
/// </summary>
internal static class TestCaseInvoker
{
    /// <summary>
    /// Runs <paramref name="testMethod"/> on a new instance of <paramref name="testClass"/>.
    /// </summary>
    /// <param name="testClass">The class to create the test instance from.</param>
    /// <param name="testMethod">The test method.</param>
    /// <param name="arguments">The data row to call it with, or null for a test without one.</param>
    /// <param name="displayName">The test case's name, reported as MSTest's <c>TestDisplayName</c>.</param>
    /// <param name="beforeInstance">Runs first, inside the test, so a failure fails the test. Used for
    /// <c>[ClassInitialize]</c>.</param>
    /// <param name="onCleanupFailure">Told about each cleanup method or <c>Dispose</c> that throws.
    /// Cleanup failures do not fail the test.</param>
    /// <param name="cancellationToken">Cancels the token MSTest tests read from their <c>TestContext</c>.</param>
    public static async Task<TestCaseRunResult> RunAsync(
        Type testClass,
        MethodInfo testMethod,
        object?[]? arguments,
        string? displayName,
        Func<Task>? beforeInstance = null,
        Action<Exception>? onCleanupFailure = null,
        CancellationToken cancellationToken = default)
    {
        var testCase = new TestCaseContext(testClass, testMethod, displayName, arguments);
        using var cancellation = cancellationToken.Register(() => testCase.CancellationTokenSource.Cancel());

        var sw = Stopwatch.StartNew();
        object? instance = null;
        object? context = null;
        Type? contextType = null;
        Exception? failure = null;

        // Publish the test method name so MotusTestBase can resolve per-method attributes
        // (e.g. [PerformanceBudget]) and name failure artifacts without a TestContext
        MotusInterop.SetTestMethodName(testMethod.Name);
        try
        {
            if (beforeInstance is not null)
                await beforeInstance();

            contextType = FindTestContextType(testClass);
            if (contextType is not null)
                context = MSTestContextFactory.TryCreate(contextType, testCase);

            instance = CreateInstance(testClass, context);
            if (context is not null)
            {
                AssignTestContext(instance, context);
                MSTestContextFactory.SetCurrent(contextType!, context);
            }

            // [TestInitialize] / NUnit [SetUp], base class first
            await RunInstanceHooksAsync(instance, testClass, "TestInitializeAttribute", baseFirst: true);
            await RunInstanceHooksAsync(instance, testClass, "SetUpAttribute", baseFirst: true);

            await InvokeAsync(testMethod, instance, BindArguments(testMethod, arguments));

            testCase.Outcome = TestCaseOutcome.Passed;
        }
        catch (Exception ex)
        {
            failure = Unwrap(ex);
            testCase.Outcome = failure.GetType().Name == "AssertInconclusiveException"
                ? TestCaseOutcome.Inconclusive
                : TestCaseOutcome.Failed;
            if (context is not null)
                MSTestContextFactory.SetException(context, failure);
        }
        finally
        {
            sw.Stop();

            if (instance is not null)
            {
                // [TestCleanup] / NUnit [TearDown], derived class first so that a derived cleanup
                // still has what the base class set up. Each one runs even if an earlier one threw,
                // so a base class that releases a browser context always gets to.
                await RunCleanupHooksAsync(instance, testClass, "TestCleanupAttribute", onCleanupFailure);
                await RunCleanupHooksAsync(instance, testClass, "TearDownAttribute", onCleanupFailure);

                try
                {
                    if (instance is IAsyncDisposable asyncDisposable)
                        await asyncDisposable.DisposeAsync();
                    else if (instance is IDisposable disposable)
                        disposable.Dispose();
                }
                catch (Exception ex)
                {
                    onCleanupFailure?.Invoke(Unwrap(ex));
                }
            }

            if (context is not null)
                MSTestContextFactory.SetCurrent(contextType!, null);
            MotusInterop.ClearTestMethodName();
        }

        return new TestCaseRunResult(failure, sw.Elapsed);
    }

    /// <summary>
    /// Runs a static lifecycle method such as <c>[AssemblyInitialize]</c> or <c>[ClassInitialize]</c>,
    /// handing an MSTest one the <c>TestContext</c> it takes. <paramref name="testClass"/> is the
    /// class the context describes.
    /// </summary>
    public static Task InvokeStaticAsync(MethodInfo method, Type testClass)
    {
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        if (parameters.Length > 0)
        {
            var contextType = parameters[0].ParameterType;
            if (MSTestContextFactory.IsTestContextType(contextType))
                args[0] = MSTestContextFactory.TryCreate(contextType, new TestCaseContext(testClass, testMethod: null));
        }

        return InvokeAsync(method, null, args);
    }

    /// <summary>
    /// The methods carrying <paramref name="attributeName"/> on <paramref name="testClass"/> and its
    /// base classes: base class first when <paramref name="baseFirst"/>, derived class first otherwise.
    /// </summary>
    internal static List<MethodInfo> GetInstanceHooks(Type testClass, string attributeName, bool baseFirst)
    {
        // GetMethods with FlattenHierarchy does not guarantee ordering, so walk the hierarchy.
        var levels = new List<IEnumerable<MethodInfo>>();
        for (var type = testClass; type is not null && type != typeof(object); type = type.BaseType)
        {
            levels.Add(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes(true).Any(a => a.GetType().Name == attributeName)));
        }

        if (baseFirst)
            levels.Reverse();

        return levels.SelectMany(level => level).ToList();
    }

    /// <summary>
    /// Fits a data row to <paramref name="method"/>'s parameters: fills omitted optional parameters
    /// with their defaults and gathers trailing values into a <c>params</c> array.
    /// </summary>
    internal static object?[]? BindArguments(MethodInfo method, object?[]? arguments)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
            return arguments is { Length: > 0 } ? arguments : null;

        arguments ??= [];

        var last = parameters[^1];
        if (last.ParameterType.IsArray && last.IsDefined(typeof(ParamArrayAttribute)) && arguments.Length >= parameters.Length - 1)
        {
            var alreadyAnArray = arguments.Length == parameters.Length
                && (arguments[^1] is null || last.ParameterType.IsInstanceOfType(arguments[^1]));
            if (!alreadyAnArray)
            {
                var fixedCount = parameters.Length - 1;
                var rest = Array.CreateInstance(last.ParameterType.GetElementType()!, arguments.Length - fixedCount);
                for (var i = 0; i < rest.Length; i++)
                    rest.SetValue(arguments[fixedCount + i], i);
                arguments = [.. arguments.Take(fixedCount), rest];
            }
        }

        if (arguments.Length < parameters.Length && parameters.Skip(arguments.Length).All(p => p.IsOptional))
            arguments = [.. arguments, .. Enumerable.Repeat(Type.Missing, parameters.Length - arguments.Length)];

        if (arguments.Length != parameters.Length)
        {
            throw new InvalidOperationException(
                $"Test method {method.DeclaringType?.Name}.{method.Name} takes {parameters.Length} parameter(s) but was given "
                + $"{arguments.Length}. Supply them with [DataRow] or [DynamicData].");
        }

        return arguments;
    }

    private static Type? FindTestContextType(Type testClass)
    {
        foreach (var property in testClass.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name == "TestContext" && MSTestContextFactory.IsTestContextType(property.PropertyType))
                return property.PropertyType;
        }

        foreach (var constructor in testClass.GetConstructors())
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length == 1 && MSTestContextFactory.IsTestContextType(parameters[0].ParameterType))
                return parameters[0].ParameterType;
        }

        return null;
    }

    private static object CreateInstance(Type testClass, object? context)
    {
        // MSTest 3.6 and later can take the TestContext as a constructor parameter instead.
        if (context is not null && testClass.GetConstructor(Type.EmptyTypes) is null)
        {
            var constructor = testClass.GetConstructors().FirstOrDefault(c =>
                c.GetParameters() is [var only] && only.ParameterType.IsInstanceOfType(context));
            if (constructor is not null)
                return constructor.Invoke([context]);
        }

        return Activator.CreateInstance(testClass)!;
    }

    private static void AssignTestContext(object instance, object context)
    {
        foreach (var property in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name == "TestContext"
                && property.PropertyType.IsInstanceOfType(context)
                && property.GetSetMethod() is { } setter)
            {
                setter.Invoke(instance, [context]);
            }
        }
    }

    private static async Task RunInstanceHooksAsync(object instance, Type testClass, string attributeName, bool baseFirst)
    {
        foreach (var method in GetInstanceHooks(testClass, attributeName, baseFirst))
            await InvokeAsync(method, instance, null);
    }

    private static async Task RunCleanupHooksAsync(object instance, Type testClass, string attributeName, Action<Exception>? onFailure)
    {
        foreach (var method in GetInstanceHooks(testClass, attributeName, baseFirst: false))
        {
            try
            {
                await InvokeAsync(method, instance, null);
            }
            catch (Exception ex)
            {
                onFailure?.Invoke(Unwrap(ex));
            }
        }
    }

    private static async Task InvokeAsync(MethodInfo method, object? instance, object?[]? args)
    {
        var result = method.Invoke(instance, args);
        if (result is Task task)
            await task;
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException { InnerException: { } inner })
            ex = inner;
        return ex;
    }
}
