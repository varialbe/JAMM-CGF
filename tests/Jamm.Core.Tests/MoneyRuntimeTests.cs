using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed class MoneyRuntimeTests
{
    [Theory]
    [InlineData(0, "$0")]
    [InlineData(950, "$950")]
    [InlineData(1234, "$1,234")]
    [InlineData(9999, "$9,999")]
    [InlineData(9999.9, "$9,999")]
    [InlineData(10000, "$10.0K")]
    [InlineData(25000, "$25.0K")]
    [InlineData(999950, "$999.9K")]
    [InlineData(999999, "$999.9K")]
    [InlineData(999999.9, "$999.9K")]
    [InlineData(1000000, "$1.0M")]
    [InlineData(3400000, "$3.4M")]
    [InlineData(999999999, "$999.9M")]
    [InlineData(1234567890, "$1.2B")]
    [InlineData(9500000000, "$9.5B")]
    [InlineData(3400000000000, "$3.4T")]
    [InlineData(3400000000123, "$3.4T")]
    [InlineData(9500000000000, "$9.5T")]
    [InlineData(999999999999999, "$999.9T")]
    [InlineData(1e15, "$1.0Qa")]
    [InlineData(-0.1, "$-1")]
    [InlineData(-99, "$-99")]
    [InlineData(-100, "$-100")]
    [InlineData(-500, "$-500")]
    [InlineData(-999, "$-999")]
    [InlineData(-999.9, "$-1,000")]
    [InlineData(-1000, "$-1,000")]
    [InlineData(-5000, "$-5,000")]
    [InlineData(-9999, "$-9,999")]
    [InlineData(-9999.9, "$-10.0K")]
    [InlineData(-10000, "$-10.0K")]
    [InlineData(-25000, "$-25.0K")]
    [InlineData(-999950, "$-999.9K")]
    [InlineData(-1e15, "$-1.0Qa")]
    public void FormatsCompactlyWithTruncatedTenths(double value, string expected) =>
        Assert.Equal(expected, MoneyRuntime.Format(value));

    [Theory]
    [InlineData(true, "999999999", "1000000 * 1000000000")]
    [InlineData(false, "1000000000000000", "999999999")]
    public void ConsoleArchiveUsesTheSelectedMoneyMode(bool enabled, string before, string after)
    {
        const string name = ConsoleUpgradeGuard.ConsoleStep;
        var recompile = new Dictionary<string, string>
        {
            [name] = $"Girl.dollars = clamp(jamm_m_val, 0, {before});\nGirl.dollars = clamp(Girl.dollars + jamm_m_val, 0, {before});"
        };
        var generated = new Dictionary<string, string>();
        MoneyRuntime.ConfigureConsole(enabled, recompile, generated);
        Assert.Contains($"clamp(jamm_m_val, 0, {after})", recompile[name]);
        Assert.Contains($"clamp(Girl.dollars + jamm_m_val, 0, {after})", recompile[name]);
        Assert.Equal(recompile[name], generated[name]);
    }

    [Fact]
    public void GmlFormatterUsesTheSameRules()
    {
        string gml = MoneyRuntime.FormatterGml;
        Assert.Contains("if (jamm_v < 10000)", gml);
        Assert.Contains("floor((jamm_v * 10) / jamm_d)", gml);
        foreach (string unit in new[] { "\"K\"", "\"M\"", "\"B\"", "\"T\"", "\"Qa\"" }) Assert.Contains(unit, gml);
        // The legacy compiler must not see 64-bit literals; large divisors are products of 32-bit values.
        Assert.DoesNotContain("1000000000000", gml);
    }

    [Fact]
    public void CompiledGmlFormatterMatchesReferenceFormatAcrossBoundaries()
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        data.GeneralInfo.Build = 1773;
        var entry = UndertaleCode.CreateEmptyEntry(data, MoneyRuntime.Formatter);
        data.Scripts.Add(new() { Name = data.Strings.MakeString("number_to_dollar_string"), Code = entry });
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(entry, MoneyRuntime.FormatterGml);
        var compiled = compiler.Compile();
        Assert.True(compiled.Successful, compiled.PrintAllErrors(true));

        double[] inputs =
        [
            0, 0.9, 1, 9, 99, 100, 950, 999, 1000, 1234, 9999, 9999.9, 10000, 10099, 10100, 25000,
            99999, 100000, 999949, 999950, 999999, 999999.9, 1000000, 1099999, 3400000, 999999999,
            1000000000, 1234567890, 9500000000, 999999999999, 1000000000000, 3400000000000,
            3400000000123, 9500000000000, 999999999999999, 1e15, 9.5e15,
            -0.1, -1, -99, -100, -500, -999, -999.9, -1000, -5000, -9999, -9999.9, -10000, -25000,
            -999950, -1000000, -1234567890, -3400000000123, -1e15
        ];

        foreach (double input in inputs)
        {
            string actual = RunFormatterBytecode(entry, input);
            Assert.Equal(MoneyRuntime.Format(input), actual);
        }
    }

    private static string RunFormatterBytecode(UndertaleCode code, double argument0)
    {
        var byWord = new Dictionary<int, int>();
        var wordAt = new int[code.Instructions.Count];
        int word = 0;
        for (int i = 0; i < code.Instructions.Count; i++)
        {
            wordAt[i] = word;
            byWord[word] = i;
            word += (int)code.Instructions[i].CalculateInstructionSize();
        }
        var stack = new Stack<object>();
        var vars = new Dictionary<string, object>(StringComparer.Ordinal) { ["argument0"] = argument0 };
        int ip = 0;
        for (int steps = 0; steps < 10_000 && ip < code.Instructions.Count; steps++)
        {
            var instr = code.Instructions[ip];
            switch (instr.Kind)
            {
                case UndertaleInstruction.Opcode.Push:
                case UndertaleInstruction.Opcode.PushLoc:
                case UndertaleInstruction.Opcode.PushGlb:
                case UndertaleInstruction.Opcode.PushBltn:
                case UndertaleInstruction.Opcode.PushI:
                    stack.Push(instr.Type1 switch
                    {
                        UndertaleInstruction.DataType.Int16 => (double)instr.ValueShort,
                        UndertaleInstruction.DataType.Int32 => instr.ValueVariable is { } v
                            ? (v.Name.Content == "argument0" ? argument0 : vars[v.Name.Content])
                            : (double)instr.ValueInt,
                        UndertaleInstruction.DataType.Int64 => (double)instr.ValueLong,
                        UndertaleInstruction.DataType.Double => instr.ValueDouble,
                        UndertaleInstruction.DataType.Boolean => instr.ValueShort != 0 ? 1.0 : 0.0,
                        UndertaleInstruction.DataType.String => instr.ValueString.Resource.Content,
                        UndertaleInstruction.DataType.Variable => instr.ValueVariable.Name.Content == "argument0"
                            ? argument0
                            : vars[instr.ValueVariable.Name.Content],
                        _ => throw new NotSupportedException($"Push {instr.Type1}")
                    });
                    ip++;
                    break;
                case UndertaleInstruction.Opcode.Pop:
                    object val = stack.Pop();
                    vars[instr.ValueVariable.Name.Content] = val;
                    ip++;
                    break;
                case UndertaleInstruction.Opcode.Conv:
                    object conv = stack.Pop();
                    stack.Push(instr.Type2 switch
                    {
                        UndertaleInstruction.DataType.Variable => conv,
                        UndertaleInstruction.DataType.String => Convert.ToString(conv, System.Globalization.CultureInfo.InvariantCulture)!,
                        _ => Convert.ToDouble(conv, System.Globalization.CultureInfo.InvariantCulture)
                    });
                    ip++;
                    break;
                case UndertaleInstruction.Opcode.Add:
                    object addR = stack.Pop(), addL = stack.Pop();
                    stack.Push(addL is string || addR is string
                        ? string.Concat(addL, addR)
                        : AsDouble(addL) + AsDouble(addR));
                    ip++;
                    break;
                case UndertaleInstruction.Opcode.Sub:
                    double subR = AsDouble(stack.Pop()), subL = AsDouble(stack.Pop());
                    stack.Push(subL - subR);
                    ip++;
                    break;
                case UndertaleInstruction.Opcode.Mul:
                    double mulR = AsDouble(stack.Pop()), mulL = AsDouble(stack.Pop());
                    stack.Push(mulL * mulR);
                    ip++;
                    break;
                case UndertaleInstruction.Opcode.Div:
                    double divR = AsDouble(stack.Pop()), divL = AsDouble(stack.Pop());
                    stack.Push(divL / divR);
                    ip++;
                    break;
                case UndertaleInstruction.Opcode.Rem:
                    double remR = AsDouble(stack.Pop()), remL = AsDouble(stack.Pop());
                    stack.Push(Math.Truncate(remL / remR));
                    ip++;
                    break;
                case UndertaleInstruction.Opcode.Mod:
                    double modR = AsDouble(stack.Pop()), modL = AsDouble(stack.Pop());
                    stack.Push(modL % modR);
                    ip++;
                    break;
                case UndertaleInstruction.Opcode.Neg:
                    stack.Push(-AsDouble(stack.Pop()));
                    ip++;
                    break;
                case UndertaleInstruction.Opcode.Cmp:
                    double cmpR = AsDouble(stack.Pop()), cmpL = AsDouble(stack.Pop());
                    bool cmp = instr.ComparisonKind switch
                    {
                        UndertaleInstruction.ComparisonType.LT => cmpL < cmpR,
                        UndertaleInstruction.ComparisonType.LTE => cmpL <= cmpR,
                        UndertaleInstruction.ComparisonType.EQ => cmpL == cmpR,
                        UndertaleInstruction.ComparisonType.NEQ => cmpL != cmpR,
                        UndertaleInstruction.ComparisonType.GTE => cmpL >= cmpR,
                        UndertaleInstruction.ComparisonType.GT => cmpL > cmpR,
                        _ => throw new NotSupportedException($"Cmp {instr.ComparisonKind}")
                    };
                    stack.Push(cmp ? 1.0 : 0.0);
                    ip++;
                    break;
                case UndertaleInstruction.Opcode.B:
                    ip = byWord[wordAt[ip] + instr.JumpOffset];
                    break;
                case UndertaleInstruction.Opcode.Bt:
                    ip = AsDouble(stack.Pop()) != 0 ? byWord[wordAt[ip] + instr.JumpOffset] : ip + 1;
                    break;
                case UndertaleInstruction.Opcode.Bf:
                    ip = AsDouble(stack.Pop()) == 0 ? byWord[wordAt[ip] + instr.JumpOffset] : ip + 1;
                    break;
                case UndertaleInstruction.Opcode.Call:
                    var args = new object[instr.ArgumentsCount];
                    for (int a = 0; a < args.Length; a++) args[a] = stack.Pop();
                    string fn = instr.ValueFunction.Name.Content;
                    stack.Push(fn switch
                    {
                        "floor" => Math.Floor(AsDouble(args[0])),
                        "string" => args[0] is string s ? s : AsDouble(args[0]).ToString("0.###############", System.Globalization.CultureInfo.InvariantCulture),
                        "string_length" => (double)((string)args[0]).Length,
                        "string_insert" => ((string)args[1]).Insert(Math.Clamp((int)AsDouble(args[2]) - 1, 0, ((string)args[1]).Length), (string)args[0]),
                        _ => throw new NotSupportedException($"Call {fn}")
                    });
                    ip++;
                    break;
                case UndertaleInstruction.Opcode.Ret:
                    return (string)stack.Pop();
                default:
                    throw new NotSupportedException($"Opcode {instr.Kind}");
            }
        }
        throw new InvalidOperationException("Formatter bytecode did not return.");

        static double AsDouble(object value) => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed partial class BuildingTests
{
    private const string VanillaFormatter = """
        dstr = string(floor(argument0));
        i = string_length(dstr) - 2;
        while (i > 1)
        {
            dstr = string_insert(",", dstr, i);
            i -= 3;
        }
        dstr = "$" + dstr;
        return dstr;
        """;

    private const string VanillaWallet = """
        if (view_current == 0)
        {
            dollars_string = number_to_dollar_string(min(floor(dollars), 999999999));
            draw_text(x, y, dollars_string);
        }
        """;

    [Fact]
    public void BigMoneyPatchesFormatterAndWalletOnlyInModdedBuildsAndCanBeTurnedOff()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), modPath = temp.PathFor("mod.win");
        WriteModel(basePath, d => Setup(d, false));
        WriteModel(modPath, d => Setup(d, true));
        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "Unrelated mod");

        string output = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(build.Verification);
        Assert.Contains(build.Plan.Decisions, d => d.StartsWith("[CGF-MONEY-001]", StringComparison.Ordinal));
        using (var merged = GameFileLoader.Load(output))
        {
            string formatter = PatchBuilder.ReadCode(merged, MoneyRuntime.Formatter).Text;
            Assert.Contains("\"Qa\"", formatter);
            string wallet = PatchBuilder.ReadCode(merged, MoneyRuntime.Wallet).Text;
            Assert.DoesNotContain("999999999", wallet);
            Assert.Contains("number_to_dollar_string(floor(dollars))", wallet);
            Assert.Contains("Girl.dollars", wallet);
        }

        string off = temp.PathFor("off.win");
        var plain = ArchiveBuilder.Build(basePath, [new(patch, modPath)], off, bigMoney: false);
        Assert.DoesNotContain(plain.Plan.Decisions, d => d.Contains("CGF-MONEY", StringComparison.Ordinal));
        using (var merged = GameFileLoader.Load(off))
            Assert.Contains("999999999", PatchBuilder.ReadCode(merged, MoneyRuntime.Wallet).Text);

        string vanilla = temp.PathFor("vanilla.win");
        ArchiveBuilder.Build(basePath, [], vanilla);
        Assert.Equal(File.ReadAllBytes(basePath), File.ReadAllBytes(vanilla));

        static void Setup(UndertaleData data, bool modded)
        {
            foreach (string name in new[] { "Wallet", "Girl", "Marker" })
                data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
            var compiler = new CompileGroup(data);
            var formatter = UndertaleCode.CreateEmptyEntry(data, MoneyRuntime.Formatter);
            data.Scripts.Add(new() { Name = data.Strings.MakeString("number_to_dollar_string"), Code = formatter });
            compiler.QueueCodeReplace(formatter, VanillaFormatter);
            compiler.QueueCodeReplace(data.GameObjects.ByName("Wallet").EventHandlerFor(EventType.Draw, data), VanillaWallet);
            compiler.QueueCodeReplace(data.GameObjects.ByName("Marker").EventHandlerFor(EventType.Create, data), modded ? "value = 2;" : "value = 1;");
            Assert.True(compiler.Compile().Successful);
        }
    }

    [Fact]
    public void ConsoleArchiveBuildsWithBigMoneyOnAndOffAndPreservesLargeLiterals()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), modPath = temp.PathFor("console.win");
        WriteConsoleBase(basePath);
        WriteConsoleBase(modPath, d => Jamm.ModMaker.ConsoleMod.Apply(d));
        using var original = GameFileLoader.Load(basePath);
        using var console = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, console, "Console");

        string onOut = temp.PathFor("on.win");
        ArchiveBuilder.Build(basePath, [new(patch, modPath)], onOut, bigMoney: true);
        using (var on = GameFileLoader.Load(onOut))
        {
            string step = PatchBuilder.ReadCode(on, ConsoleUpgradeGuard.ConsoleStep).Text;
            Assert.Contains("up to 1e15", step);
            Assert.Contains("1000000000000000", step);
            Assert.DoesNotContain("999999999", step);
        }

        string offOut = temp.PathFor("off.win");
        ArchiveBuilder.Build(basePath, [new(patch, modPath)], offOut, bigMoney: false);
        using (var off = GameFileLoader.Load(offOut))
        {
            string step = PatchBuilder.ReadCode(off, ConsoleUpgradeGuard.ConsoleStep).Text;
            Assert.Contains("0..999999999", step);
            Assert.Contains("clamp(Girl.dollars + jamm_m_val, 0, 999999999)", step);
            Assert.Contains("clamp(jamm_m_val, 0, 999999999)", step);
        }

        static void WriteConsoleBase(string path, Action<UndertaleData>? edit = null)
        {
            using var data = UndertaleData.CreateNew();
            data.GeneralInfo.Major = 1;
            data.GeneralInfo.BytecodeVersion = 16;
            data.GeneralInfo.Build = 1773;
            foreach (string roomName in new[] { "NewWorld", "room11" })
                data.Rooms.Add(new() { Name = data.Strings.MakeString(roomName) });
            foreach (string objName in new[] { "Girl", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons", "Wallet" })
                data.GameObjects.Add(new() { Name = data.Strings.MakeString(objName), Visible = true });
            var compiler = new CompileGroup(data);
            var formatter = UndertaleCode.CreateEmptyEntry(data, MoneyRuntime.Formatter);
            data.Scripts.Add(new() { Name = data.Strings.MakeString("number_to_dollar_string"), Code = formatter });
            compiler.QueueCodeReplace(formatter, VanillaFormatter);
            compiler.QueueCodeReplace(data.GameObjects.ByName("Wallet").EventHandlerFor(EventType.Draw, data), VanillaWallet);
            Assert.True(compiler.Compile().Successful);
            edit?.Invoke(data);
            using var stream = File.Create(path);
            UndertaleIO.Write(stream, data);
        }
    }
}
