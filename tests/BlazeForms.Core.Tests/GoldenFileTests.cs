using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BlazeForms.Definitions;
using BlazeForms.Serialization;

namespace BlazeForms.Core.Tests;

/// <summary>
/// AGENTS.md testing rule: golden-file tests pin the JSON of a representative definition per
/// <c>schemaVersion</c>. <c>form-definition-v2.json</c> is the current representative; the frozen
/// <c>form-definition-v1.json</c> stays as a real version-1 document this build must still read,
/// proving the schema-2 change (the optional <c>calculation</c> property) is backward compatible.
/// Set <c>BLAZEFORMS_UPDATE_GOLDEN=1</c> to rewrite the current file after an intentional — and
/// version-bumped — schema change; the frozen v1 file is never regenerated.
/// </summary>
public sealed class GoldenFileTests
{
    private const string UpdateEnvironmentVariable = "BLAZEFORMS_UPDATE_GOLDEN";

    [Fact]
    public void TheRepresentativeDefinitionMatchesItsGoldenFile()
    {
        var actual = Normalize(FormJson.SerializeDefinition(TestDefinitions.RepresentativeDefinition, indented: true));
        var path = GoldenFilePath("form-definition-v2.json");

        if (string.Equals(Environment.GetEnvironmentVariable(UpdateEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }

        Assert.True(File.Exists(path), $"Golden file '{path}' is missing. Re-run with {UpdateEnvironmentVariable}=1.");
        Assert.Equal(Normalize(File.ReadAllText(path)), actual);
    }

    [Fact]
    public void TheGoldenFileDeserializesBackToTheRepresentativeDefinition()
    {
        var path = GoldenFilePath("form-definition-v2.json");

        Assert.True(File.Exists(path), $"Golden file '{path}' is missing. Re-run with {UpdateEnvironmentVariable}=1.");

        var restored = FormJson.DeserializeDefinition(File.ReadAllText(path));

        Assert.Equal(FormSchema.CurrentVersion, restored.SchemaVersion);
        Assert.Equal(
            FormJson.SerializeDefinition(TestDefinitions.RepresentativeDefinition),
            FormJson.SerializeDefinition(restored));
    }

    [Fact]
    public void TheGoldenFileExercisesEveryNodeTypeInTheSchema()
    {
        var golden = File.ReadAllText(GoldenFilePath("form-definition-v2.json"));

        var pinnedTypeNames = Regex
            .Matches(golden, "\"type\": \"(?<name>[a-z]+)\"", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(Enum.GetValues<NodeType>().Length, pinnedTypeNames.Count);
    }

    [Fact]
    public void TheFrozenVersionOneGoldenStillReadsUnderThisBuild()
    {
        var path = GoldenFilePath("form-definition-v1.json");

        Assert.True(File.Exists(path), $"Frozen golden file '{path}' is missing.");

        var restored = FormJson.DeserializeDefinition(File.ReadAllText(path));

        // A version-1 document predates the calculation property and must keep loading, reporting
        // its own declared version, with every calc node reading as having no calculation.
        Assert.Equal(1, restored.SchemaVersion);
        Assert.All(
            restored.EnumerateNodes().Where(node => node.Type == NodeType.Calc),
            node => Assert.Null(node.Calculation));

        // And it must re-serialize to the exact frozen bytes — a silent re-serialization drift for a
        // real v1 document, not just a self-consistent round-trip, would be a contract break.
        Assert.Equal(
            Normalize(File.ReadAllText(path)),
            Normalize(FormJson.SerializeDefinition(restored, indented: true)));
    }

    private static string Normalize(string json) => json.ReplaceLineEndings("\n").TrimEnd('\n') + "\n";

    private static string GoldenFilePath(string fileName, [CallerFilePath] string testFilePath = "") =>
        Path.Combine(Path.GetDirectoryName(testFilePath)!, "Golden", fileName);
}
