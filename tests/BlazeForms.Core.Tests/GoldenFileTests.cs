using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BlazeForms.Definitions;
using BlazeForms.Serialization;

namespace BlazeForms.Core.Tests;

/// <summary>
/// AGENTS.md testing rule: golden-file tests pin the JSON of a representative definition per
/// <c>schemaVersion</c>. Set <c>BLAZEFORMS_UPDATE_GOLDEN=1</c> to rewrite the file after an
/// intentional — and version-bumped — schema change.
/// </summary>
public sealed class GoldenFileTests
{
    private const string UpdateEnvironmentVariable = "BLAZEFORMS_UPDATE_GOLDEN";

    [Fact]
    public void TheRepresentativeDefinitionMatchesItsGoldenFile()
    {
        var actual = Normalize(FormJson.SerializeDefinition(TestDefinitions.RepresentativeDefinition, indented: true));
        var path = GoldenFilePath();

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
        var path = GoldenFilePath();

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
        var golden = File.ReadAllText(GoldenFilePath());

        var pinnedTypeNames = Regex
            .Matches(golden, "\"type\": \"(?<name>[a-z]+)\"", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(Enum.GetValues<NodeType>().Length, pinnedTypeNames.Count);
    }

    private static string Normalize(string json) => json.ReplaceLineEndings("\n").TrimEnd('\n') + "\n";

    private static string GoldenFilePath([CallerFilePath] string testFilePath = "") =>
        Path.Combine(Path.GetDirectoryName(testFilePath)!, "Golden", "form-definition-v1.json");
}
