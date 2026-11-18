using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui;

/// <summary>Creates copyable, privacy scoped support logs for manager and runtime failures.</summary>
internal static class DiagnosticLog
{
    private static readonly string LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JAMM", "logs");

    public static string Create(MainViewModel? model, string eventName, string details)
    {
        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
        var lines = new List<string>
        {
            $"JAMM diagnostic {id}", $"UTC: {DateTimeOffset.UtcNow:O}", $"Event: {Clean(eventName)}",
            $"Platform: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
            $"JAMM: {typeof(DiagnosticLog).Assembly.GetName().Version}",
            $"Profile: {Clean(model?.SelectedProfile ?? "(none)")}",
            "Enabled mods, in load order: " + string.Join(" -> ", model?.Order.Where(x => x.Enabled).Select(x => Clean(x.Name + " [" + x.Id + "]")) ?? [])
        };
        if (!string.IsNullOrWhiteSpace(details)) lines.Add("Details: " + Clean(details));
        if (eventName.StartsWith("game process", StringComparison.Ordinal))
        {
            string proton = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "steam-343780.log");
            try
            {
                if (File.Exists(proton))
                {
                    string excerpt = string.Join(Environment.NewLine, File.ReadLines(proton).TakeLast(200)
                        .Where(line => Regex.IsMatch(line, "(err:|error|fatal|crash|exception)", RegexOptions.IgnoreCase))
                        .TakeLast(12).Select(line => Redact(line[..Math.Min(line.Length, 400)])));
                    if (excerpt.Length > 0) lines.Add("Recent Proton error lines (user paths removed):\n" + excerpt);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        string text = string.Join(Environment.NewLine, lines);
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.WriteAllText(Path.Combine(LogDirectory, $"diagnostic-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{id}.txt"), text, new UTF8Encoding(false));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return text;
    }

    private static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    private static string Redact(string value)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)) value = value.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
        return Regex.Replace(Clean(value), @"(?<![A-Za-z0-9])(?:[A-Za-z]:\\|/)(?:[^\s/:]+[/\\])*[^\s/:]+", "[path]");
    }
}
