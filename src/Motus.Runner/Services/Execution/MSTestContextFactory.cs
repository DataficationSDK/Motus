using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;

namespace Motus.Runner.Services.Execution;

/// <summary>
/// Builds MSTest <c>TestContext</c> objects for tests run by reflection.
/// </summary>
/// <remarks>
/// The runners do not reference MSTest: a test assembly brings its own version, and the
/// <c>TestContext</c> it expects is that version's type, which has moved between assemblies
/// and gained abstract members from one major version to the next. So the subclass is generated
/// at run time, once per <c>TestContext</c> type, by overriding whatever members that version
/// declares abstract, plus <c>CurrentTestOutcome</c>. Every override forwards to
/// <see cref="Dispatch"/>. When a version cannot be subclassed this way, no context is made
/// and the test runs without one, as it did before the runners made contexts at all.
/// </remarks>
internal static class MSTestContextFactory
{
    internal const string TestContextTypeName = "Microsoft.VisualStudio.TestTools.UnitTesting.TestContext";

    private static readonly ConcurrentDictionary<Type, ContextShape?> s_shapes = new();

    /// <summary>True when <paramref name="type"/> is some version of MSTest's <c>TestContext</c>.</summary>
    public static bool IsTestContextType(Type? type)
        => type is { IsClass: true, IsAbstract: true, FullName: TestContextTypeName };

    /// <summary>
    /// Creates a <paramref name="contextType"/> describing <paramref name="testCase"/>, or returns
    /// null when that is not an MSTest <c>TestContext</c> or this version of it can't be built.
    /// </summary>
    public static object? TryCreate(Type contextType, TestCaseContext testCase)
    {
        if (!IsTestContextType(contextType))
            return null;

        var shape = s_shapes.GetOrAdd(contextType, TryBuildShape);
        if (shape is null)
            return null;

        try
        {
            return shape.Create(testCase);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Points MSTest's static <c>TestContext.Current</c> (MSTest 4.1 and later) at
    /// <paramref name="context"/>. It is an async local, so this must be called from the async
    /// method that goes on to run the test. Does nothing for versions without it.
    /// </summary>
    public static void SetCurrent(Type contextType, object? context)
    {
        if (s_shapes.TryGetValue(contextType, out var shape) && shape?.CurrentSetter is { } setter)
        {
            try { setter.Invoke(null, [context]); }
            catch { /* the test can still use the instance it was given */ }
        }
    }

    /// <summary>
    /// Records the exception that failed the test on <c>TestContext.TestException</c>
    /// (MSTest 4 and later), so the test's cleanup methods can read it.
    /// </summary>
    public static void SetException(object context, Exception exception)
    {
        if (context.GetType().BaseType is { } contextType
            && s_shapes.TryGetValue(contextType, out var shape)
            && shape?.ExceptionSetter is { } setter)
        {
            try { setter.Invoke(context, [exception]); }
            catch { /* informational only */ }
        }
    }

    private static object? Dispatch(ContextShape shape, TestCaseContext testCase, Dictionary<string, object?> properties, string member, object?[] args)
    {
        switch (member)
        {
            case "get_Properties":
                return properties;

            case "get_CurrentTestOutcome":
                return shape.ToOutcome(testCase.Outcome);

            case "Write":
                Console.Out.Write(Format(args));
                return null;

            case "WriteLine":
                Console.Out.WriteLine(Format(args));
                return null;

            case "DisplayMessage":
                // (MessageLevel level, string message)
                Console.Out.WriteLine(args.Length > 1 ? args[1] as string : null);
                return null;

            default:
                // AddResultFile, and any member a later MSTest makes abstract: the runners have no
                // result attachments, so there is nothing to do and the default is returned.
                return null;
        }
    }

    private static string? Format(object?[] args)
    {
        if (args.Length == 0)
            return null;

        var format = args[0] as string;
        if (args.Length > 1 && args[1] is object?[] formatArgs && format is not null)
            return string.Format(CultureInfo.CurrentCulture, format, formatArgs);

        return format;
    }

    private static ContextShape? TryBuildShape(Type contextType)
    {
        try
        {
            var baseConstructor = contextType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes);
            if (baseConstructor is null || !(baseConstructor.IsPublic || baseConstructor.IsFamily || baseConstructor.IsFamilyOrAssembly))
                return null;

            var toOverride = new List<MethodInfo>();
            foreach (var method in contextType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!method.IsAbstract)
                    continue;

                // A member this can't override would leave the generated type abstract.
                if (!CanOverride(method))
                    return null;

                toOverride.Add(method);
            }

            var outcomeGetter = contextType.GetProperty("CurrentTestOutcome", BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();
            if (outcomeGetter is null || !outcomeGetter.ReturnType.IsEnum)
                return null;
            if (!outcomeGetter.IsAbstract)
            {
                if (!outcomeGetter.IsVirtual || outcomeGetter.IsFinal)
                    return null;
                toOverride.Add(outcomeGetter);
            }

            var generated = Generate(contextType, baseConstructor, toOverride);
            var constructor = generated.GetConstructor([typeof(Func<string, object?[], object?>)])!;

            return new ContextShape(
                constructor,
                outcomeGetter.ReturnType,
                ReadPropertyLabels(contextType),
                NonPublicSetter(contextType, "CancellationTokenSource", BindingFlags.Instance),
                NonPublicSetter(contextType, "TestDisplayName", BindingFlags.Instance),
                NonPublicSetter(contextType, "TestData", BindingFlags.Instance),
                NonPublicSetter(contextType, "TestException", BindingFlags.Instance),
                NonPublicSetter(contextType, "Current", BindingFlags.Static));
        }
        catch
        {
            return null;
        }
    }

    private static bool CanOverride(MethodInfo method)
        => !method.IsGenericMethodDefinition
           && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly)
           && !method.ReturnType.IsByRef
           && !method.ReturnType.IsPointer
           && method.GetParameters().All(p => !p.ParameterType.IsByRef && !p.ParameterType.IsPointer);

    private static MethodInfo? NonPublicSetter(Type contextType, string propertyName, BindingFlags kind)
        => contextType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | kind)?.GetSetMethod(nonPublic: true);

    /// <summary>
    /// Reads the keys MSTest uses for <c>Properties</c> from its own <c>...Label</c> fields, so the
    /// base class's <c>TestName</c> and friends find what is stored, whatever this version calls them.
    /// </summary>
    private static Dictionary<string, string> ReadPropertyLabels(Type contextType)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in contextType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType != typeof(string) || !field.Name.EndsWith("Label", StringComparison.Ordinal))
                continue;

            var name = field.Name[..^"Label".Length];
            labels[name] = (field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null)) as string ?? name;
        }

        return labels;
    }

    private static Type Generate(Type contextType, ConstructorInfo baseConstructor, List<MethodInfo> toOverride)
    {
        // Collectible, so that it can refer to a test assembly loaded into a collectible context.
        var assemblyName = new AssemblyName($"Motus.Runner.TestContext.{Guid.NewGuid():N}");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType(
            "Motus.Runner.MotusTestContext",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            contextType);

        var dispatchType = typeof(Func<string, object?[], object?>);
        var dispatchField = type.DefineField("_dispatch", dispatchType, FieldAttributes.Private | FieldAttributes.InitOnly);
        var invoke = dispatchType.GetMethod(nameof(Func<object>.Invoke))!;

        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [dispatchType]);
        var ctorIl = constructor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, baseConstructor);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, dispatchField);
        ctorIl.Emit(OpCodes.Ret);

        foreach (var baseMethod in toOverride)
        {
            var parameters = baseMethod.GetParameters();
            var attributes = MethodAttributes.Virtual | MethodAttributes.HideBySig
                | (baseMethod.IsPublic ? MethodAttributes.Public : MethodAttributes.Family);
            if (baseMethod.IsSpecialName)
                attributes |= MethodAttributes.SpecialName;

            var method = type.DefineMethod(
                baseMethod.Name, attributes, baseMethod.ReturnType, parameters.Select(p => p.ParameterType).ToArray());
            var il = method.GetILGenerator();

            // _dispatch(name, new object[] { arg0, arg1, ... })
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, dispatchField);
            il.Emit(OpCodes.Ldstr, baseMethod.Name);
            il.Emit(OpCodes.Ldc_I4, parameters.Length);
            il.Emit(OpCodes.Newarr, typeof(object));
            for (var i = 0; i < parameters.Length; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldarg, i + 1);
                if (parameters[i].ParameterType.IsValueType || parameters[i].ParameterType.IsGenericParameter)
                    il.Emit(OpCodes.Box, parameters[i].ParameterType);
                il.Emit(OpCodes.Stelem_Ref);
            }

            il.Emit(OpCodes.Callvirt, invoke);

            if (baseMethod.ReturnType == typeof(void))
            {
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ret);
            }
            else
            {
                // A null result becomes the return type's default rather than a NullReferenceException.
                var result = il.DeclareLocal(typeof(object));
                var fallback = il.DeclareLocal(baseMethod.ReturnType);
                var useDefault = il.DefineLabel();
                il.Emit(OpCodes.Stloc, result);
                il.Emit(OpCodes.Ldloc, result);
                il.Emit(OpCodes.Brfalse_S, useDefault);
                il.Emit(OpCodes.Ldloc, result);
                il.Emit(OpCodes.Unbox_Any, baseMethod.ReturnType);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(useDefault);
                il.Emit(OpCodes.Ldloca, fallback);
                il.Emit(OpCodes.Initobj, baseMethod.ReturnType);
                il.Emit(OpCodes.Ldloc, fallback);
                il.Emit(OpCodes.Ret);
            }

            type.DefineMethodOverride(method, baseMethod);
        }

        return type.CreateType()!;
    }

    private sealed class ContextShape(
        ConstructorInfo constructor,
        Type outcomeType,
        Dictionary<string, string> labels,
        MethodInfo? cancellationSetter,
        MethodInfo? displayNameSetter,
        MethodInfo? dataSetter,
        MethodInfo? exceptionSetter,
        MethodInfo? currentSetter)
    {
        public MethodInfo? ExceptionSetter => exceptionSetter;

        public MethodInfo? CurrentSetter => currentSetter;

        public object ToOutcome(TestCaseOutcome outcome)
            => Enum.TryParse(outcomeType, outcome.ToString(), out var value)
                ? value!
                : Enum.ToObject(outcomeType, 0);

        public object Create(TestCaseContext testCase)
        {
            var properties = BuildProperties(testCase);
            Func<string, object?[], object?> dispatch = (member, args) => Dispatch(this, testCase, properties, member, args);
            var instance = constructor.Invoke([dispatch]);

            TrySet(cancellationSetter, instance, testCase.CancellationTokenSource);
            if (testCase.TestMethod is not null)
            {
                TrySet(displayNameSetter, instance, testCase.DisplayName);
                if (testCase.Arguments is not null)
                    TrySet(dataSetter, instance, testCase.Arguments);
            }

            return instance;
        }

        private Dictionary<string, object?> BuildProperties(TestCaseContext testCase)
        {
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            void Put(string name, object? value) => properties[labels.GetValueOrDefault(name, name)] = value;

            Put("FullyQualifiedTestClassName", testCase.TestClass.FullName);
            Put("ManagedType", testCase.TestClass.FullName);
            if (testCase.TestMethod is not null)
            {
                Put("TestName", testCase.TestMethod.Name);
                Put("ManagedMethod", testCase.TestMethod.Name);
            }

            // Nothing is deployed and there is no results folder of MSTest's own, so every directory
            // MSTest would name is the one the test assembly was loaded from, which always exists.
            var directory = Path.GetDirectoryName(testCase.TestClass.Assembly.Location);
            if (!string.IsNullOrEmpty(directory))
            {
                foreach (var name in (string[])["TestRunDirectory", "DeploymentDirectory", "ResultsDirectory",
                             "TestRunResultsDirectory", "TestResultsDirectory", "TestDir", "TestDeploymentDir", "TestLogsDir"])
                {
                    Put(name, directory);
                }
            }

            return properties;
        }

        private static void TrySet(MethodInfo? setter, object instance, object? value)
        {
            if (setter is null)
                return;

            try { setter.Invoke(instance, [value]); }
            catch { /* optional detail; the context works without it */ }
        }
    }
}
