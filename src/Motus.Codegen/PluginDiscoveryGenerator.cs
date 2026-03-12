using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Motus.Codegen.Emit;
using Motus.Codegen.Model;

namespace Motus.Codegen;

/// <summary>
/// Roslyn incremental source generator that discovers [MotusPlugin] types
/// and emits a module initializer to register them with PluginDiscovery.
/// </summary>
[Generator]
public sealed class PluginDiscoveryGenerator : IIncrementalGenerator
{
    private const string MotusPluginAttributeFqn = "Motus.Abstractions.MotusPluginAttribute";
    private const string IPluginFqn = "Motus.Abstractions.IPlugin";

    // Diagnostic descriptors
    private static readonly DiagnosticDescriptor AbstractClassDiagnostic = new(
        "MOTUS001",
        "[MotusPlugin] on abstract class",
        "Type '{0}' is marked [MotusPlugin] but is abstract and will be skipped",
        "Motus.Plugins",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingIPluginDiagnostic = new(
        "MOTUS002",
        "[MotusPlugin] without IPlugin",
        "Type '{0}' is marked [MotusPlugin] but does not implement IPlugin and will be skipped",
        "Motus.Plugins",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NoParameterlessCtorDiagnostic = new(
        "MOTUS003",
        "[MotusPlugin] without parameterless constructor",
        "Type '{0}' is marked [MotusPlugin] but has no public parameterless constructor and will be skipped",
        "Motus.Plugins",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenericClassDiagnostic = new(
        "MOTUS004",
        "[MotusPlugin] on generic class",
        "Type '{0}' is marked [MotusPlugin] but is generic and will be skipped",
        "Motus.Plugins",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Stage A: Find [MotusPlugin] types in the current compilation via syntax
        var localPlugins = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                MotusPluginAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => ctx.TargetSymbol as INamedTypeSymbol)
            .Where(static s => s is not null)
            .Collect();

        // Stage B: Scan referenced assemblies for [MotusPlugin] types
        var referencedPlugins = context.CompilationProvider
            .Select(static (compilation, ct) =>
            {
                var results = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
                var attributeSymbol = compilation.GetTypeByMetadataName(MotusPluginAttributeFqn);
                if (attributeSymbol is null)
                    return results.ToImmutable();

                foreach (var reference in compilation.References)
                {
                    ct.ThrowIfCancellationRequested();
                    if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                        continue;

                    ScanNamespace(assembly.GlobalNamespace, attributeSymbol, results, ct);
                }

                return results.ToImmutable();
            });

        // Combine local + referenced
        var allSymbols = localPlugins.Combine(referencedPlugins);

        // Stage C-E: Validate, deduplicate, emit
        context.RegisterSourceOutput(allSymbols, static (spc, pair) =>
        {
            var (local, referenced) = pair;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var validPlugins = new List<PluginTypeInfo>();

            // Process all symbols (local first, then referenced)
            var allSymbols = local.Where(s => s is not null).Cast<INamedTypeSymbol>()
                .Concat(referenced);

            foreach (var symbol in allSymbols)
            {
                var fqn = GetFullyQualifiedName(symbol);
                if (!seen.Add(fqn))
                    continue;

                // Validate
                if (!ValidatePlugin(spc, symbol, fqn))
                    continue;

                validPlugins.Add(new PluginTypeInfo(fqn, symbol.ContainingAssembly.Name));
            }

            var source = PluginRegistryEmitter.Emit(validPlugins);
            spc.AddSource("MotusPluginRegistry.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static bool ValidatePlugin(SourceProductionContext spc, INamedTypeSymbol symbol, string fqn)
    {
        if (symbol.IsAbstract)
        {
            spc.ReportDiagnostic(Diagnostic.Create(AbstractClassDiagnostic, symbol.Locations.FirstOrDefault(), fqn));
            return false;
        }

        if (symbol.IsGenericType)
        {
            spc.ReportDiagnostic(Diagnostic.Create(GenericClassDiagnostic, symbol.Locations.FirstOrDefault(), fqn));
            return false;
        }

        if (!ImplementsIPlugin(symbol))
        {
            spc.ReportDiagnostic(Diagnostic.Create(MissingIPluginDiagnostic, symbol.Locations.FirstOrDefault(), fqn));
            return false;
        }

        if (!HasPublicParameterlessCtor(symbol))
        {
            spc.ReportDiagnostic(Diagnostic.Create(NoParameterlessCtorDiagnostic, symbol.Locations.FirstOrDefault(), fqn));
            return false;
        }

        return true;
    }

    private static bool ImplementsIPlugin(INamedTypeSymbol symbol)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            var fullName = iface.ContainingNamespace + "." + iface.Name;
            if (fullName == IPluginFqn)
                return true;
        }
        return false;
    }

    private static bool HasPublicParameterlessCtor(INamedTypeSymbol symbol)
    {
        // If no constructors are declared, the compiler provides a default public parameterless one
        var ctors = symbol.InstanceConstructors;
        if (ctors.Length == 0)
            return true;

        foreach (var ctor in ctors)
        {
            if (ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length == 0)
                return true;
        }

        return false;
    }

    private static string GetFullyQualifiedName(INamedTypeSymbol symbol)
    {
        // Build fully qualified name without "global::" prefix
        var parts = new List<string> { symbol.Name };
        var ns = symbol.ContainingNamespace;
        while (ns is not null && !ns.IsGlobalNamespace)
        {
            parts.Add(ns.Name);
            ns = ns.ContainingNamespace;
        }
        parts.Reverse();
        return string.Join(".", parts);
    }

    private static void ScanNamespace(
        INamespaceSymbol ns,
        INamedTypeSymbol attributeSymbol,
        ImmutableArray<INamedTypeSymbol>.Builder results,
        System.Threading.CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var type in ns.GetTypeMembers())
        {
            foreach (var attr in type.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeSymbol))
                {
                    results.Add(type);
                    break;
                }
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            ScanNamespace(childNs, attributeSymbol, results, ct);
        }
    }
}
