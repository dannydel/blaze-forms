using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Hosting;

namespace BlazeForms.Serialization;

/// <summary>
/// Reads and writes the definition schema and the submission envelope. The JSON shape is a
/// public contract carrying <see cref="FormDefinition.SchemaVersion"/>: any change to it bumps
/// the version and lands with updated round-trip and golden-file tests (AGENTS.md invariant #2).
/// </summary>
/// <remarks>
/// Definitions are untrusted input — the library supports importing them — so callers should
/// treat a <see cref="JsonException"/> as ordinary bad input, not a bug.
/// </remarks>
public static class FormJson
{
    /// <summary>
    /// The serializer options BlazeForms itself uses: camelCase names, nulls omitted, and a
    /// source-generated resolver. Handy for asserting host options line up.
    /// </summary>
    public static JsonSerializerOptions Options => FormJsonContext.Default.Options;

    /// <summary>
    /// The source-generated resolver for every schema type. Hosts serving definitions over HTTP
    /// add this to their own <c>TypeInfoResolverChain</c> rather than re-declaring the shape.
    /// </summary>
    public static IJsonTypeInfoResolver TypeInfoResolver => FormJsonContext.Default;

    /// <summary>
    /// Serializes a form definition.
    /// </summary>
    /// <param name="definition">
    /// The definition to write.
    /// </param>
    /// <param name="indented">
    /// Whether to write human-readable JSON. Golden files and host exports use
    /// <see langword="true"/>; the wire uses the default.
    /// </param>
    /// <returns>
    /// The definition as JSON.
    /// </returns>
    public static string SerializeDefinition(FormDefinition definition, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return indented
            ? JsonSerializer.Serialize(definition, IndentedFormJsonContext.Default.FormDefinition)
            : JsonSerializer.Serialize(definition, FormJsonContext.Default.FormDefinition);
    }

    /// <summary>
    /// Deserializes a form definition.
    /// </summary>
    /// <param name="json">
    /// The JSON to read.
    /// </param>
    /// <returns>
    /// The definition.
    /// </returns>
    /// <exception cref="JsonException">
    /// The JSON is malformed, is the literal <c>null</c>, omits a required property, or states a
    /// <see cref="FormDefinition.SchemaVersion"/> this build cannot read.
    /// </exception>
    public static FormDefinition DeserializeDefinition(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        ValidateDeclaredSchemaVersion(json);

        return JsonSerializer.Deserialize(json, FormJsonContext.Default.FormDefinition)
            ?? throw new JsonException("The JSON did not contain a form definition.");
    }

    /// <summary>
    /// Rejects a document that declares a schema version this build cannot read, before any of it
    /// is trusted.
    /// </summary>
    /// <remarks>
    /// The check reads the raw JSON rather than the deserialized value because System.Text.Json
    /// assigns absent properties their default, which makes an omitted <c>schemaVersion</c>
    /// indistinguishable from a declared zero on the object. A document that omits the property is
    /// accepted as version 1; one that declares zero, a negative, or a future version is not.
    /// </remarks>
    private static void ValidateDeclaredSchemaVersion(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("schemaVersion", out var declared))
        {
            return;
        }

        if (declared.ValueKind != JsonValueKind.Number || !declared.TryGetInt32(out var version))
        {
            throw new JsonException("The 'schemaVersion' property must be a whole number.");
        }

        if (version < 1 || version > FormSchema.CurrentVersion)
        {
            throw new JsonException(
                $"Schema version {version} is not readable by this build, which supports versions 1 to {FormSchema.CurrentVersion}.");
        }
    }

    /// <summary>
    /// Serializes a submission envelope.
    /// </summary>
    /// <param name="envelope">
    /// The envelope to write.
    /// </param>
    /// <param name="indented">
    /// Whether to write human-readable JSON, as the submission view's JSON export does
    /// (PRD §4.3).
    /// </param>
    /// <returns>
    /// The envelope as JSON.
    /// </returns>
    public static string SerializeEnvelope(FormSubmissionEnvelope envelope, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return indented
            ? JsonSerializer.Serialize(envelope, IndentedFormJsonContext.Default.FormSubmissionEnvelope)
            : JsonSerializer.Serialize(envelope, FormJsonContext.Default.FormSubmissionEnvelope);
    }

    /// <summary>
    /// Deserializes a submission envelope.
    /// </summary>
    /// <param name="json">
    /// The JSON to read.
    /// </param>
    /// <returns>
    /// The envelope.
    /// </returns>
    /// <exception cref="JsonException">
    /// The JSON is malformed, is the literal <c>null</c>, or omits a required property.
    /// </exception>
    public static FormSubmissionEnvelope DeserializeEnvelope(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return JsonSerializer.Deserialize(json, FormJsonContext.Default.FormSubmissionEnvelope)
            ?? throw new JsonException("The JSON did not contain a submission envelope.");
    }

    /// <summary>
    /// Serializes an expression tree on its own, for logic summaries and diagnostics.
    /// </summary>
    /// <param name="group">
    /// The expression to write.
    /// </param>
    /// <returns>
    /// The expression as JSON.
    /// </returns>
    public static string SerializeConditionGroup(ConditionGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return JsonSerializer.Serialize(group, FormJsonContext.Default.ConditionGroup);
    }

    /// <summary>
    /// Deserializes an expression tree on its own.
    /// </summary>
    /// <param name="json">
    /// The JSON to read.
    /// </param>
    /// <returns>
    /// The expression.
    /// </returns>
    /// <exception cref="JsonException">
    /// The JSON is malformed or is the literal <c>null</c>.
    /// </exception>
    public static ConditionGroup DeserializeConditionGroup(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return JsonSerializer.Deserialize(json, FormJsonContext.Default.ConditionGroup)
            ?? throw new JsonException("The JSON did not contain an expression.");
    }
}
