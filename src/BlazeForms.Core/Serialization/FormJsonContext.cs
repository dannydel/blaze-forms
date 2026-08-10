using System.Text.Json.Serialization;
using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Hosting;
using BlazeForms.Versioning;

namespace BlazeForms.Serialization;

/// <summary>
/// The source-generated serializer contract for every schema type. Core is trim-compatible, so
/// reflection-based serialization is off-limits (AGENTS.md C# standards) — everything goes
/// through this context.
/// </summary>
/// <remarks>
/// Only nulls are omitted. A schema-version-1 document therefore always spells out
/// <c>"required": false</c>, <c>"half": false</c>, <c>"options": []</c> and the like, rather than
/// leaning on defaults. That verbosity is a deliberate v1 decision: it makes the wire format
/// self-describing for the hosts and tools that read it, and it means a reader never has to know
/// what a missing property defaults to. Switching to
/// <see cref="JsonIgnoreCondition.WhenWritingDefault"/> would change the bytes for every existing
/// definition, so it is a schema change — it needs a <c>schemaVersion</c> bump and a new golden
/// file, not a quiet tidy-up (AGENTS.md invariant #2).
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(FormDefinition))]
[JsonSerializable(typeof(FormSubmissionEnvelope))]
[JsonSerializable(typeof(FormDraft))]
[JsonSerializable(typeof(FormVersion))]
[JsonSerializable(typeof(FormVersionSummary))]
[JsonSerializable(typeof(ConditionGroup))]
internal sealed partial class FormJsonContext : JsonSerializerContext;

/// <summary>
/// The same contract, emitting indented JSON for golden files and host-facing exports.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(FormDefinition))]
[JsonSerializable(typeof(FormSubmissionEnvelope))]
internal sealed partial class IndentedFormJsonContext : JsonSerializerContext;
