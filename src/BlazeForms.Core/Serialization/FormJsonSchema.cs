using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using BlazeForms.Definitions;

namespace BlazeForms.Serialization;

/// <summary>
/// Exports the definition schema as JSON Schema (draft 2020-12), for hosts and editors that want
/// machine-readable validation or IDE authoring support rather than a hand-authored document.
/// </summary>
/// <remarks>
/// The schema is generated from <see cref="FormJson.Options"/> via
/// <see cref="JsonSchemaExporter"/>, so it can never drift from what
/// <see cref="FormJson.SerializeDefinition"/> actually writes. It is pinned by a golden file
/// (<c>tests/BlazeForms.Core.Tests/Golden/form-definition-v{schemaVersion}.schema.json</c>) the
/// same way the wire format is (AGENTS.md invariant #2). See <c>docs/schema.md</c> for the
/// publish policy: a change to an already-published version's file is never republished — only
/// an unpublished (not yet tagged/live) version's file may be regenerated in place.
/// </remarks>
public static class FormJsonSchema
{
    private const string SchemaDialect = "https://json-schema.org/draft/2020-12/schema";
    private const string SchemaDescription =
        "JSON Schema for a BlazeForms form definition document, as read and written by BlazeForms.Serialization.FormJson.";

    private static readonly string SchemaId =
        $"https://dannydel.github.io/blaze-forms/schemas/form-definition-v{FormSchema.CurrentVersion}.schema.json";

    private static readonly string SchemaTitle =
        $"BlazeForms form definition (schemaVersion {FormSchema.CurrentVersion})";

    private static readonly Lazy<string> CachedSchema = new(BuildDefinitionSchema);

    /// <summary>
    /// Builds the JSON Schema document for <see cref="FormDefinition"/>.
    /// </summary>
    /// <returns>
    /// The schema as an indented JSON string, draft 2020-12, with the root stamped with
    /// <c>$schema</c>, <c>$id</c>, <c>title</c>, and <c>description</c>, and
    /// <c>schemaVersion</c> constrained to the range this build accepts.
    /// </returns>
    /// <remarks>
    /// The exporter is deterministic for a fixed set of serializer options, so the result is
    /// computed once and cached — repeated calls (e.g. across the tests that each call this)
    /// never re-run the exporter.
    /// </remarks>
    public static string CreateDefinitionSchema() => CachedSchema.Value;

    private static string BuildDefinitionSchema()
    {
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = TransformSchemaNode,
        };

        var schema = FormJson.Options.GetJsonSchemaAsNode(typeof(FormDefinition), exporterOptions);

        if (schema is not JsonObject root)
        {
            throw new InvalidOperationException(
                $"The JSON Schema exporter returned a '{schema.GetValueKind()}' root for {nameof(FormDefinition)}; a JSON object was expected.");
        }

        var stamped = StampRoot(root);

        return stamped.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Narrows <c>schemaVersion</c> to a bounded integer everywhere it appears in the exported
    /// tree, since the exporter otherwise models it as the unconstrained <see langword="int"/>
    /// it is in code.
    /// </summary>
    private static JsonNode TransformSchemaNode(JsonSchemaExporterContext context, JsonNode schema)
    {
        if (context.PropertyInfo is { Name: "schemaVersion" } && schema is JsonObject schemaObject)
        {
            schemaObject["type"] = "integer";
            schemaObject["minimum"] = 1;
            schemaObject["maximum"] = FormSchema.CurrentVersion;
        }

        return schema;
    }

    /// <summary>
    /// Stamps the document-level keywords that only make sense once, on the root node, in a
    /// fixed order so the golden file stays stable.
    /// </summary>
    /// <remarks>
    /// The stamped keywords are added first so they win over anything the exporter might ever
    /// emit at the root under the same name (it emits none of these today, but this keeps the
    /// intent explicit rather than accidental).
    /// </remarks>
    private static JsonObject StampRoot(JsonObject root)
    {
        var stamped = new JsonObject();

        stamped.TryAdd("$schema", SchemaDialect);
        stamped.TryAdd("$id", SchemaId);
        stamped.TryAdd("title", SchemaTitle);
        stamped.TryAdd("description", SchemaDescription);

        foreach (var (key, value) in root.ToList())
        {
            root.Remove(key);
            stamped.TryAdd(key, value);
        }

        return stamped;
    }
}
