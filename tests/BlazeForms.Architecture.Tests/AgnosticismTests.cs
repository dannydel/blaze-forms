using System.Reflection;

namespace BlazeForms.Architecture.Tests;

/// <summary>
/// Enforces AGENTS.md invariant #1 ("Agnosticism"): the public contracts of
/// <c>BlazeForms.Core</c> and <c>BlazeForms.Renderer</c> may reference only BCL types
/// (and, for the renderer, <c>Microsoft.AspNetCore.Components*</c> types) plus the
/// library's own types. No third-party dependency — including Markdig, which Core
/// takes on for its Markdown pipeline — may appear in a public signature.
/// </summary>
public sealed class AgnosticismTests
{
    private const string CoreAssemblyName = "BlazeForms.Core";
    private const string RendererAssemblyName = "BlazeForms.Renderer";
    private const string DesignerAssemblyName = "BlazeForms.Designer";

    /// <summary>
    /// Assembly name prefixes allowed anywhere in the Designer's transitive dependency closure:
    /// the BCL, first-party BlazeForms assemblies, and Markdig — the one sanctioned third-party
    /// dependency, pulled in transitively through Core's safe-Markdown pipeline (PRD §9). No
    /// other third-party (and in particular no UI-framework) assembly may leak in; the Designer
    /// must stay UI-framework-agnostic (PRD §10 / D3) even though it depends on Core and
    /// Renderer, neither of which may themselves reference a concrete UI framework.
    /// </summary>
    private static readonly string[] AllowedClosurePrefixes =
    [
        "System.",
        "Microsoft.",
        "netstandard",
        "mscorlib",
        "BlazeForms.",
        "Markdig",
    ];

    [Fact]
    public void CorePublicApiReferencesOnlyBclAndOwnTypes()
    {
        AssertPublicApiIsAgnostic(CoreAssemblyName, allowAspNetCoreComponents: false);
    }

    [Fact]
    public void RendererPublicApiReferencesOnlyBclAspNetCoreComponentsAndOwnTypes()
    {
        AssertPublicApiIsAgnostic(RendererAssemblyName, allowAspNetCoreComponents: true);
    }

    /// <summary>
    /// Enforces AGENTS.md invariant #1 at the assembly-reference level rather than the public-API
    /// level: even a third-party UI package used only internally by the Designer — never
    /// appearing in a public signature — would defeat the point of PRD §10/D3's commitment to
    /// keep the Designer swappable across host UI frameworks. This walks
    /// <c>BlazeForms.Designer</c>'s full transitive closure of referenced assemblies (not just
    /// its direct references) and fails on the first assembly outside the allow-list.
    /// </summary>
    [Fact]
    public void DesignerDependencyClosureContainsNoThirdPartyUiPackage()
    {
        var assembly = Assembly.Load(DesignerAssemblyName);
        var closure = CollectTransitiveReferencedAssemblies(assembly);

        var violations = closure
            .Where(name => !IsAllowedInClosure(name.Name))
            .Select(name => name.FullName ?? name.Name ?? "<unknown>")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"""
            {DesignerAssemblyName} pulls in third-party dependenc{(violations.Count == 1 ? "y" : "ies")} outside the
            allow-list (BCL, BlazeForms.*, and Markdig): {string.Join(", ", violations)}.
            The Designer must stay UI-framework-agnostic (PRD §10 / D3): it may not depend,
            even transitively, on a concrete UI/component package.
            """);
    }

    /// <summary>
    /// Breadth-first walk of <see cref="Assembly.GetReferencedAssemblies"/> starting from
    /// <paramref name="root"/>, resolving each referenced assembly (so its own references are
    /// followed in turn) and de-duplicating by assembly name.
    /// </summary>
    private static Dictionary<string, AssemblyName>.ValueCollection CollectTransitiveReferencedAssemblies(Assembly root)
    {
        var visited = new Dictionary<string, AssemblyName>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<Assembly>();
        queue.Enqueue(root);

        var processedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.GetName().Name! };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var referenced in current.GetReferencedAssemblies())
            {
                var key = referenced.Name ?? referenced.FullName;
                visited[key] = referenced;

                if (!processedAssemblies.Add(key))
                {
                    continue;
                }

                try
                {
                    queue.Enqueue(Assembly.Load(referenced));
                }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    // Reference metadata without a resolvable assembly on disk (e.g. a
                    // facade/reference assembly with no runtime counterpart) can't contribute
                    // further transitive references; it's still recorded as visited above so
                    // it's checked against the allow-list itself.
                }
            }
        }

        return visited.Values;
    }

    private static bool IsAllowedInClosure(string? assemblyName) =>
        assemblyName is not null
        && AllowedClosurePrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.Ordinal));

    private static void AssertPublicApiIsAgnostic(string assemblyName, bool allowAspNetCoreComponents)
    {
        var assembly = Assembly.Load(assemblyName);
        var violations = new List<string>();

        foreach (var type in assembly.GetExportedTypes())
        {
            CollectViolations(type, allowAspNetCoreComponents, violations);
        }

        Assert.True(
            violations.Count == 0,
            $"""
            {assemblyName} leaks non-agnostic type(s) into its public API:
            {string.Join(Environment.NewLine, violations)}
            """);
    }

    /// <summary>
    /// Walks every type reachable from a public type's signature — base type, interfaces,
    /// generic constraints, and the members it exposes to callers or derivers — flagging
    /// any referenced type whose namespace falls outside the allowed set.
    /// </summary>
    private static void CollectViolations(Type type, bool allowAspNetCoreComponents, List<string> violations)
    {
        CheckType(type.BaseType, $"{type.FullName} : base type", allowAspNetCoreComponents, violations);

        foreach (var @interface in type.GetInterfaces())
        {
            CheckType(@interface, $"{type.FullName} : implements", allowAspNetCoreComponents, violations);
        }

        foreach (var genericParameter in type.GetGenericArguments().Where(argument => argument.IsGenericParameter))
        {
            foreach (var constraint in genericParameter.GetGenericParameterConstraints())
            {
                CheckType(constraint, $"{type.FullName} : generic constraint on {genericParameter.Name}", allowAspNetCoreComponents, violations);
            }
        }

        const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var constructor in type.GetConstructors(MemberFlags).Where(IsPartOfPublicContract))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                CheckType(parameter.ParameterType, $"{type.FullName}.{constructor.Name}({parameter.Name})", allowAspNetCoreComponents, violations);
            }
        }

        foreach (var method in type.GetMethods(MemberFlags).Where(m => IsPartOfPublicContract(m) && !m.IsSpecialName))
        {
            CheckType(method.ReturnType, $"{type.FullName}.{method.Name}() return type", allowAspNetCoreComponents, violations);

            foreach (var parameter in method.GetParameters())
            {
                CheckType(parameter.ParameterType, $"{type.FullName}.{method.Name}({parameter.Name})", allowAspNetCoreComponents, violations);
            }

            foreach (var genericParameter in method.GetGenericArguments())
            {
                foreach (var constraint in genericParameter.GetGenericParameterConstraints())
                {
                    CheckType(constraint, $"{type.FullName}.{method.Name} : generic constraint on {genericParameter.Name}", allowAspNetCoreComponents, violations);
                }
            }
        }

        foreach (var property in type.GetProperties(MemberFlags).Where(IsPartOfPublicContract))
        {
            CheckType(property.PropertyType, $"{type.FullName}.{property.Name}", allowAspNetCoreComponents, violations);
        }

        foreach (var field in type.GetFields(MemberFlags).Where(IsPartOfPublicContract))
        {
            CheckType(field.FieldType, $"{type.FullName}.{field.Name}", allowAspNetCoreComponents, violations);
        }

        foreach (var @event in type.GetEvents(MemberFlags).Where(IsPartOfPublicContract))
        {
            CheckType(@event.EventHandlerType, $"{type.FullName}.{@event.Name}", allowAspNetCoreComponents, violations);
        }

        foreach (var nestedType in type.GetNestedTypes(MemberFlags).Where(IsPartOfPublicContract))
        {
            CollectViolations(nestedType, allowAspNetCoreComponents, violations);
        }
    }

    private static bool IsPartOfPublicContract(MemberInfo? member) => member switch
    {
        ConstructorInfo constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly,
        MethodInfo method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
        PropertyInfo property => IsPartOfPublicContract(property.GetMethod) || IsPartOfPublicContract(property.SetMethod),
        FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
        EventInfo @event => IsPartOfPublicContract(@event.AddMethod),
        Type nestedType => nestedType.IsPublic || nestedType.IsNestedPublic || nestedType.IsNestedFamily || nestedType.IsNestedFamORAssem,
        null => false,
        _ => false,
    };

    /// <summary>
    /// Unwraps a referenced type down to its constituent named types — following arrays,
    /// by-ref parameters, <c>Nullable&lt;T&gt;</c>, and closed generic type arguments — and
    /// records a violation for each constituent whose namespace is not allowed.
    /// </summary>
    private static void CheckType(Type? type, string context, bool allowAspNetCoreComponents, List<string> violations)
    {
        if (type is null || type.IsGenericParameter)
        {
            return;
        }

        if (type.IsByRef || type.IsPointer || type.HasElementType)
        {
            CheckType(type.GetElementType(), context, allowAspNetCoreComponents, violations);
            return;
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                CheckType(argument, context, allowAspNetCoreComponents, violations);
            }

            CheckType(type.GetGenericTypeDefinition(), context, allowAspNetCoreComponents, violations);
            return;
        }

        if (!IsAllowedNamespace(type.Namespace, allowAspNetCoreComponents))
        {
            violations.Add($"  {context} -> {type.FullName}");
        }
    }

    private static bool IsAllowedNamespace(string? typeNamespace, bool allowAspNetCoreComponents)
    {
        if (typeNamespace is null)
        {
            return true;
        }

        if (typeNamespace.StartsWith("BlazeForms", StringComparison.Ordinal))
        {
            return true;
        }

        if (typeNamespace.StartsWith("System", StringComparison.Ordinal))
        {
            return true;
        }

        return allowAspNetCoreComponents && typeNamespace.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal);
    }
}
