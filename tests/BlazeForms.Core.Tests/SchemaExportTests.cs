using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Serialization;

namespace BlazeForms.Core.Tests;

/// <summary>
/// AGENTS.md invariant #2 applies to the exported JSON Schema, not only the wire format: a
/// golden file pins <see cref="FormJsonSchema.CreateDefinitionSchema"/>'s output, and further
/// tests catch drift the golden comparison alone would not (an enum member added, renamed, or
/// removed without a schema regen, or the <c>schemaVersion</c> bound falling out of sync with
/// <see cref="FormSchema.CurrentVersion"/>). Set <c>BLAZEFORMS_UPDATE_GOLDEN=1</c> to regenerate
/// both the golden file and its <c>docs/schemas</c> copy after an intentional change; read the
/// diff before committing it, and see <c>docs/schema.md</c> for the publish policy — only an
/// unpublished schema version's file may be regenerated in place.
/// </summary>
public sealed class SchemaExportTests
{
    private const string UpdateEnvironmentVariable = "BLAZEFORMS_UPDATE_GOLDEN";

    [Fact]
    public void TheExportedSchemaMatchesItsGoldenFile()
    {
        var actual = Normalize(FormJsonSchema.CreateDefinitionSchema());
        var goldenPath = GoldenSchemaPath();

        if (string.Equals(Environment.GetEnvironmentVariable(UpdateEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            // Both copies are written together so they can never desync — see
            // ThePublishedDocsSchemaIsByteIdenticalToTheGoldenFile below.
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, actual);

            var publishedPath = PublishedSchemaPath();
            Directory.CreateDirectory(Path.GetDirectoryName(publishedPath)!);
            File.WriteAllText(publishedPath, actual);
        }

        Assert.True(File.Exists(goldenPath), $"Golden file '{goldenPath}' is missing. Re-run with {UpdateEnvironmentVariable}=1.");
        Assert.Equal(Normalize(File.ReadAllText(goldenPath)), actual);
    }

    [Fact]
    public void EveryEnumTypeIsRepresentedExactlyInTheExportedSchema()
    {
        var schema = JsonNode.Parse(FormJsonSchema.CreateDefinitionSchema())!;
        var enumArrays = CollectEnumArrays(schema);

        AssertEnumIsRepresentedExactly(enumArrays, Enum.GetValues<NodeType>());
        AssertEnumIsRepresentedExactly(enumArrays, Enum.GetValues<ConditionOperator>());
        AssertEnumIsRepresentedExactly(enumArrays, Enum.GetValues<CalcOperation>());
        AssertEnumIsRepresentedExactly(enumArrays, Enum.GetValues<CalcFunction>());
        AssertEnumIsRepresentedExactly(enumArrays, Enum.GetValues<CalcFormat>());
        AssertEnumIsRepresentedExactly(enumArrays, Enum.GetValues<ConditionJoin>());
    }

    [Fact]
    public void TheSchemaVersionMaximumMatchesTheCurrentSchemaVersion()
    {
        var schema = JsonNode.Parse(FormJsonSchema.CreateDefinitionSchema())!;

        var schemaVersionNode = FindPropertySchema(schema, "schemaVersion")
            ?? throw new InvalidOperationException("The exported schema has no 'schemaVersion' property.");

        Assert.Equal(1, schemaVersionNode["minimum"]!.GetValue<int>());
        Assert.Equal(FormSchema.CurrentVersion, schemaVersionNode["maximum"]!.GetValue<int>());
    }

    [Fact]
    public void TheRootCarriesTheExpectedDocumentMetadata()
    {
        var schema = JsonNode.Parse(FormJsonSchema.CreateDefinitionSchema())!.AsObject();

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema["$schema"]!.GetValue<string>());
        Assert.EndsWith(
            $"/form-definition-v{FormSchema.CurrentVersion}.schema.json",
            schema["$id"]!.GetValue<string>(),
            StringComparison.Ordinal);
        Assert.Contains(
            $"schemaVersion {FormSchema.CurrentVersion}",
            schema["title"]!.GetValue<string>(),
            StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(schema["description"]?.GetValue<string>()));
    }

    [Fact]
    public void ThePublishedDocsSchemaIsByteIdenticalToTheGoldenFile()
    {
        var goldenPath = GoldenSchemaPath();
        var publishedPath = PublishedSchemaPath();

        Assert.True(
            File.Exists(publishedPath),
            $"Published schema '{publishedPath}' is missing. Copy '{goldenPath}' there — see docs/schema.md.");
        Assert.Equal(
            File.ReadAllBytes(goldenPath),
            File.ReadAllBytes(publishedPath));
    }

    /// <summary>
    /// Asserts that some array in the schema tagged with the JSON Schema <c>enum</c> keyword
    /// contains exactly this enum type's member names — no more, no fewer — so a member added,
    /// renamed, or removed without a schema regen fails loudly instead of slipping past a
    /// substring check.
    /// </summary>
    private static void AssertEnumIsRepresentedExactly<TEnum>(List<HashSet<string>> enumArrays, TEnum[] members)
        where TEnum : struct, Enum
    {
        var expected = members
            .Select(member => Unquote(JsonSerializer.Serialize(member, FormJson.Options)))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            enumArrays.Any(array => array.SetEquals(expected)),
            $"No 'enum' array in the exported schema exactly matches {typeof(TEnum).Name}'s members: [{string.Join(", ", expected)}].");
    }

    private static string Unquote(string jsonLiteral) => jsonLiteral.Trim('"');

    /// <summary>
    /// Collects every array value of a JSON Schema <c>enum</c> keyword anywhere in the document,
    /// as sets of member names (nullable-enum <c>null</c> entries excluded, since
    /// <see cref="Enum.GetValues{TEnum}"/> never includes one).
    /// </summary>
    private static List<HashSet<string>> CollectEnumArrays(JsonNode node)
    {
        var results = new List<HashSet<string>>();
        CollectEnumArrays(node, results);
        return results;
    }

    private static void CollectEnumArrays(JsonNode? node, List<HashSet<string>> results)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue("enum", out var enumNode) && enumNode is JsonArray enumArray)
                {
                    results.Add(enumArray
                        .Where(item => item is not null)
                        .Select(item => item!.GetValue<string>())
                        .ToHashSet(StringComparer.Ordinal));
                }

                foreach (var (_, value) in obj)
                {
                    CollectEnumArrays(value, results);
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    CollectEnumArrays(item, results);
                }

                break;
        }
    }

    private static JsonNode? FindPropertySchema(JsonNode node, string propertyName)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("properties", out var properties)
                && properties is JsonObject propertiesObject
                && propertiesObject.TryGetPropertyValue(propertyName, out var found)
                && found is not null)
            {
                return found;
            }

            foreach (var (_, value) in obj)
            {
                if (value is not null && FindPropertySchema(value, propertyName) is { } nested)
                {
                    return nested;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null && FindPropertySchema(item, propertyName) is { } nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string Normalize(string json) => json.ReplaceLineEndings("\n").TrimEnd('\n') + "\n";

    private static string SchemaFileName() => $"form-definition-v{FormSchema.CurrentVersion}.schema.json";

    private static string GoldenSchemaPath([CallerFilePath] string testFilePath = "") =>
        Path.Combine(Path.GetDirectoryName(testFilePath)!, "Golden", SchemaFileName());

    /// <summary>
    /// Resolves <c>docs/schemas/&lt;file&gt;</c> from this test file's own path rather than
    /// walking the filesystem for a <c>.git</c> marker: the repo root is always exactly two
    /// directories above <c>tests/BlazeForms.Core.Tests</c>, so this is deterministic under any
    /// test runner and fails loudly — never silently skips — if the file is missing.
    /// </summary>
    private static string PublishedSchemaPath([CallerFilePath] string testFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", ".."));

        return Path.Combine(repoRoot, "docs", "schemas", SchemaFileName());
    }
}
