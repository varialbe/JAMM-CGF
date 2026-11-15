using System.Text.Json;
using System.Text.Json.Serialization;
using Jamm.Cli;
using Jamm.Core;

namespace Jamm.Core.Tests;

/// <summary>CLI exit semantics, path-free reports and output safeguards.</summary>
public class CliTests
{
    [Fact]
    public void OnlyInspectReturnsTwoForYycAndErrorsReturnOne()
    {
        var file = YycFile();
        Assert.Equal(2, CommandExitCodes.ForFiles([file], inspecting: true));
        Assert.Equal(0, CommandExitCodes.ForFiles([file]));
        Assert.Equal(1, CommandExitCodes.ForFiles([file, file with { Error = "could not read missing.win" }]));
    }

    [Fact]
    public void YycReportsAndJsonHaveNoPercentageVerdictOrSourcePaths()
    {
        var file = YycFile();
        var comparison = AssetComparer.Compare(file, file);
        var result = new Phase0Result([file], [comparison]);
        string report = TextReport.Markdown(result);
        string table = TextReport.Comparison(comparison);
        Assert.Contains("NOT COMPARABLE: data.win is YYC (no bytecode)", table);
        Assert.DoesNotContain("%", table);
        Assert.DoesNotContain("unchanged", table);
        Assert.DoesNotContain("| code |", report[report.IndexOf("## Comparison with original", StringComparison.Ordinal)..]);
        Assert.DoesNotContain("/private/", report + TextReport.Summary(result, "/private/reports/phase0.md"));
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        string json = JsonSerializer.Serialize(result, options);
        Assert.DoesNotContain("/private/", json);
        Assert.Contains("\"OriginalCodeUnchangedPercent\":null", json);
        Assert.DoesNotContain("Diagnostic", json);
    }

    [Fact]
    public void NewWinOutputRequiresOptInAndCannotReplaceInputsOrExistingFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"jamm-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.win");
            string output = Path.Combine(directory, "merged.win");
            File.WriteAllText(input, "synthetic input");
            Assert.Throws<ArgumentException>(() => OutputFile.Validate(output, [input]));
            OutputFile.Validate(output, [input], allowNewWin: true);
            Assert.Throws<ArgumentException>(() => OutputFile.Validate(input, [input], allowNewWin: true));
            File.WriteAllText(output, "existing");
            Assert.Throws<ArgumentException>(() => OutputFile.Validate(output, [input], allowNewWin: true));
            Assert.Equal("synthetic input", File.ReadAllText(input));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void OutputValidationResolvesFileAndDirectorySymlinks()
    {
        if (OperatingSystem.IsWindows()) return; // Symlink creation may require privileges on Windows.
        string directory = Path.Combine(Path.GetTempPath(), $"jamm-links-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.dat");
            File.WriteAllText(input, "synthetic input");
            string link = Path.Combine(directory, "report.md");
            File.CreateSymbolicLink(link, input);
            Assert.Throws<ArgumentException>(() => OutputFile.Validate(link, [input], allowNewWin: true));
            Assert.Throws<ArgumentException>(() => OutputFile.Validate(link, [input]));
            string alias = Path.Combine(directory, "alias");
            Directory.CreateSymbolicLink(alias, directory);
            Assert.Throws<ArgumentException>(() => OutputFile.Validate(Path.Combine(alias, "input.dat"), [input], allowNewWin: true));
            File.Delete(alias);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static FileAnalysis YycFile() => new("data.win",
        new("data.win", 0, "hash", "game", "game", "2.0.0.0", "2023.6.0.0", "Post2022_0", 17, 0, true, true,
            new Dictionary<AssetKind, int> { [AssetKind.Code] = 0 }), [], null,
        new Dictionary<AssetKind, IReadOnlyList<AssetFingerprint>>(), "private diagnostic at /private/input/data.win");
}
