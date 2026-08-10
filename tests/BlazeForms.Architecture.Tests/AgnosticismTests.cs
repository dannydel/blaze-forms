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
