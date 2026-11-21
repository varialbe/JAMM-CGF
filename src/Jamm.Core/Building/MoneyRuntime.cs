using System.Globalization;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

/// <summary>
/// Lets money grow beyond the legacy 999,999,999 wallet clamp and shows large amounts compactly
/// (<c>$3.4M</c>, <c>$9.5T</c>). Every vanilla price, value and wallet display goes through
/// <c>number_to_dollar_string</c>, so replacing that one script covers shops, sell dialogs,
/// the catalog and quest rewards. Fish sale values are not changed.
/// </summary>
internal static class MoneyRuntime
{
    internal const string Formatter = "gml_Script_number_to_dollar_string";
    internal const string Wallet = "gml_Object_Wallet_Draw_0";
    internal const string Revision = "big-money-1";
    private const string Diagnostic = "[CGF-MONEY-001]";
    private const string WalletClamp = "number_to_dollar_string(min(floor(dollars), 999999999))";

    /// <summary>Largest amount the wallet keeps. Doubles hold integers exactly up to 2^53 (about 9.0e15).</summary>
    internal const double MaxMoney = 1e15;

    /// <summary>Compact formatter body. Amounts below 10,000 keep the vanilla digits and commas.</summary>
    internal const string FormatterGml = """
        var jamm_v, jamm_d, jamm_u, jamm_t, jamm_s, jamm_i, jamm_neg;
        jamm_v = floor(argument0);
        jamm_neg = (jamm_v < 0);
        if (jamm_neg) jamm_v = -jamm_v;
        if (jamm_v < 10000)
        {
            jamm_s = string(floor(argument0));
            jamm_i = string_length(jamm_s) - 2;
            while (jamm_i > 1)
            {
                jamm_s = string_insert(",", jamm_s, jamm_i);
                jamm_i -= 3;
            }
            return "$" + jamm_s;
        }
        jamm_d = 1000;
        jamm_u = "K";
        if (jamm_v >= 1000000) { jamm_d = 1000000; jamm_u = "M"; }
        if (jamm_v >= 1000000000) { jamm_d = 1000000000; jamm_u = "B"; }
        if (jamm_v >= 1000 * 1000000000) { jamm_d = 1000 * 1000000000; jamm_u = "T"; }
        if (jamm_v >= 1000000 * 1000000000) { jamm_d = 1000000 * 1000000000; jamm_u = "Qa"; }
        jamm_t = floor((jamm_v * 10) / jamm_d);
        jamm_s = string(jamm_t div 10) + "." + string(jamm_t mod 10) + jamm_u;
        if (jamm_neg) jamm_s = "-" + jamm_s;
        return "$" + jamm_s;
        """;

    private const string WalletReplacement = """
        if (Girl.dollars > 1000000 * 1000000000) Girl.dollars = 1000000 * 1000000000;
            dollars_string = number_to_dollar_string(floor(dollars))
        """;

    /// <summary>Reference implementation of the GML formatter, used by tests and documentation.</summary>
    public static string Format(double value)
    {
        double v = Math.Floor(value);
        bool negative = v < 0;
        if (negative) v = -v;
        if (v < 10000)
        {
            string digits = Math.Floor(value).ToString("0", CultureInfo.InvariantCulture);
            for (int i = digits.Length - 2; i > 1; i -= 3) digits = digits.Insert(i - 1, ",");
            return "$" + digits;
        }
        (double divisor, string unit) = v switch
        {
            >= 1e15 => (1e15, "Qa"),
            >= 1e12 => (1e12, "T"),
            >= 1e9 => (1e9, "B"),
            >= 1e6 => (1e6, "M"),
            _ => (1e3, "K")
        };
        double tenths = Math.Floor(v * 10 / divisor);
        string text = $"{Math.Floor(tenths / 10).ToString("0", CultureInfo.InvariantCulture)}.{(tenths % 10).ToString("0", CultureInfo.InvariantCulture)}{unit}";
        return "$" + (negative ? "-" + text : text);
    }

    /// <summary>Patches the formatter and the wallet clamp. Mods that already replaced either entry keep their version.</summary>
    internal static void Apply(IReadOnlyDictionary<string, CodeText> baseline, Dictionary<string, string> recompile,
        Dictionary<string, string> generated, List<string> decisions)
    {
        if (!baseline.TryGetValue(Formatter, out var formatter) || !baseline.TryGetValue(Wallet, out var wallet))
            return;
        List<string> patched = [];
        List<string> kept = [];

        if (recompile.TryGetValue(Formatter, out string? changedFormatter) && Normalize(changedFormatter) != Normalize(formatter.Text))
            kept.Add($"{Formatter} (changed by a mod)");
        else
        {
            recompile[Formatter] = generated[Formatter] = FormatterGml;
            patched.Add(Formatter);
        }

        string walletText = recompile.GetValueOrDefault(Wallet) ?? wallet.Text;
        int at = walletText.IndexOf("dollars_string = " + WalletClamp, StringComparison.Ordinal);
        if (at >= 0 && walletText.IndexOf(WalletClamp, at + WalletClamp.Length, StringComparison.Ordinal) < 0)
        {
            string code = walletText[..at] + WalletReplacement.Trim() + walletText[(at + ("dollars_string = " + WalletClamp).Length)..];
            recompile[Wallet] = generated[Wallet] = code;
            patched.Add(Wallet);
        }
        else
            kept.Add($"{Wallet} (no vanilla 999,999,999 clamp found)");

        string text = $"{Diagnostic} Money can exceed $999,999,999 (kept up to $1.0Qa) and large amounts display compactly (K, M, B, T, Qa). Patched: {string.Join(", ", patched)}.";
        if (kept.Count > 0) text += $" Kept: {string.Join(", ", kept)}.";
        decisions.Add(text);
    }

    // The published console archive predates the big-money generator. Normalize only its two canonical
    // command assignments and help text, so a profile's setting also works with either console version.
    internal static void ConfigureConsole(bool enabled, Dictionary<string, string> recompile, Dictionary<string, string> generated)
    {
        const string entry = ConsoleUpgradeGuard.ConsoleStep;
        if (!recompile.TryGetValue(entry, out string? current)) return;
        string[] oldLimits = enabled ? ["999999999"] : ["1000000 * 1000000000", "1000000000000000"];
        string newLimit = enabled ? "1000000 * 1000000000" : "999999999";
        string oldHelp = enabled ? "set dollars (0..999999999)" : "set dollars (up to 1e15)";
        string newHelp = enabled ? "set dollars (up to 1e15)" : "set dollars (0..999999999)";
        string text = current.Replace(oldHelp, newHelp, StringComparison.Ordinal);
        foreach (string expression in new[] { "Girl.dollars + jamm_m_val", "jamm_m_val" })
            foreach (string oldLimit in oldLimits)
                text = text.Replace($"Girl.dollars = clamp({expression}, 0, {oldLimit});",
                    $"Girl.dollars = clamp({expression}, 0, {newLimit});", StringComparison.Ordinal);
        if (text != current) generated[entry] = recompile[entry] = text;
    }

    private static string Normalize(string gml) => string.Join('\n', gml.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
}
