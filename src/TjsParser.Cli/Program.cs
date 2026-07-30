using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using TjsParser;
using TjsParser.Kbad;
using TjsParser.Parsing;
using TjsParser.Serialization;

return Cli.Run(args);

internal static class Cli
{
    public static int Run(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp) { PrintUsage(); return 0; }
            if (options.Input == null) { PrintUsage(); return 2; }
            if (Directory.Exists(options.Input)) return ParseDirectory(options);
            if (File.Exists(options.Input)) return ParseFile(options);
            Console.Error.WriteLine("Input does not exist: " + options.Input);
            return 2;
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine(ex.Message); PrintUsage(); return 2;
        }
        catch (KbadFormatException ex)
        {
            Console.Error.WriteLine(ex.Message); return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString()); return 1;
        }
    }

    private static int ParseFile(CliOptions cli)
    {
        var input = Path.GetFullPath(cli.Input!);
        var fileKind = Parser.DetectFileKind(input);
        if (fileKind == TjsFileKind.Tjs2100Bytecode)
        {
            Console.Error.WriteLine(cli.Input + ": " + UnsupportedDescription(fileKind) + " is not supported.");
            return 1;
        }

        if (fileKind == TjsFileKind.Kbad100BinaryData)
        {
            var document = KbadReader.ReadFile(input);
            var kbadOptions = new KbadJsonOptions { Indented = !cli.Compact, Shape = cli.KbadJsonShape };
            if (cli.Output == null) Console.Out.WriteLine(KbadJson.Serialize(document, kbadOptions));
            else
            {
                var output = ResolveSingleFileOutput(cli.Input!, cli.Output);
                var parent = Path.GetDirectoryName(output); if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                using var stream = File.Create(output); KbadJson.Write(stream, document, kbadOptions);
                Console.Error.WriteLine("Output: " + output);
            }
            return 0;
        }

        var result = Parser.ParseFile(input, cli.ParseOptions);
        var jsonOptions = new AstJsonOptions { Indented = !cli.Compact, IncludeComments = !cli.NoComments };
        if (cli.Output == null) Console.Out.WriteLine(AstJson.Serialize(result, jsonOptions));
        else
        {
            var output = ResolveSingleFileOutput(cli.Input!, cli.Output);
            var parent = Path.GetDirectoryName(output); if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            using var stream = File.Create(output); AstJson.Write(stream, result, jsonOptions);
            Console.Error.WriteLine("Output: " + output);
        }
        PrintDiagnostics(result, cli.Input!);
        return result.Success ? 0 : 1;
    }

    private static string ResolveSingleFileOutput(string input, string output)
    {
        var fullOutput = Path.GetFullPath(output);
        if (Directory.Exists(fullOutput))
            return Path.Combine(fullOutput, Path.GetFileName(input) + ".json");

        if (output.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            output.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            Directory.CreateDirectory(fullOutput);
            return Path.Combine(fullOutput, Path.GetFileName(input) + ".json");
        }

        return fullOutput;
    }

    private static int ParseDirectory(CliOptions cli)
    {
        if (cli.Output == null) throw new CliException("Directory input requires --output.");
        var inputRoot = Path.GetFullPath(cli.Input!);
        var outputRoot = Path.GetFullPath(cli.Output);
        Directory.CreateDirectory(outputRoot);
        var files = Directory.GetFiles(inputRoot, "*.tjs", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        var manifest = new List<ManifestItem>();
        var parsedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;
        var jsonOptions = new AstJsonOptions { Indented = !cli.Compact, IncludeComments = !cli.NoComments };
        var kbadJsonOptions = new KbadJsonOptions { Indented = !cli.Compact, Shape = cli.KbadJsonShape };
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(inputRoot, file);
            var itemPath = relative.Replace('\\', '/');
            var fileKind = TjsFileKind.SourceText;
            try
            {
                fileKind = Parser.DetectFileKind(file);
                if (fileKind == TjsFileKind.Tjs2100Bytecode)
                {
                    skippedCount++;
                    manifest.Add(ManifestItem.SkippedBytecode(itemPath));
                    continue;
                }
                var output = Path.Combine(outputRoot, relative + ".json");
                var parent = Path.GetDirectoryName(output); if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                var outputPath = output.Substring(outputRoot.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
                if (fileKind == TjsFileKind.Kbad100BinaryData)
                {
                    var document = KbadReader.ReadFile(file);
                    using (var stream = File.Create(output)) KbadJson.Write(stream, document, kbadJsonOptions);
                    parsedCount++;
                    manifest.Add(ManifestItem.ParsedKbad(itemPath, outputPath));
                    continue;
                }

                var result = Parser.ParseFile(file, cli.ParseOptions);
                using (var stream = File.Create(output)) AstJson.Write(stream, result, jsonOptions);
                var errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                if (errors == 0)
                {
                    parsedCount++;
                    manifest.Add(ManifestItem.ParsedSource(itemPath, result.Source.Encoding, result.Source.RootMode.ToString(), outputPath));
                }
                else
                {
                    failedCount++;
                    manifest.Add(ManifestItem.Failed(itemPath, "source-text", result.Source.Encoding, result.Source.RootMode.ToString(), errors, outputPath, "Parser diagnostics contain errors."));
                }
                PrintDiagnostics(result, relative);
            }
            catch (Exception ex)
            {
                failedCount++;
                manifest.Add(ManifestItem.Failed(itemPath, ManifestKind(fileKind), null, null, 1, null, ex.Message));
                Console.Error.WriteLine(relative + ": " + ex.Message);
            }
        }
        var manifestPath = Path.Combine(outputRoot, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            inputRoot,
            fileCount = files.Length,
            parsedCount,
            skippedCount,
            failedCount,
            success = failedCount == 0,
            files = manifest
        }, new JsonSerializerOptions
        {
            WriteIndented = !cli.Compact,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = TjsJsonEncoder.Instance
        }));
        Console.Error.WriteLine($"Processed {files.Length} files: parsed {parsedCount}, skipped {skippedCount}, failed {failedCount}; output: {outputRoot}");
        return failedCount == 0 ? 0 : 1;
    }

    private static void PrintDiagnostics(ParseResult result, string path)
    {
        foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            Console.Error.WriteLine($"{path}({diagnostic.Span.Start.Line},{diagnostic.Span.Start.Column}): {diagnostic.Code}: {diagnostic.Message}");
    }

    private static string UnsupportedDescription(TjsFileKind fileKind)
    {
        switch (fileKind)
        {
            case TjsFileKind.Tjs2100Bytecode: return "compiled TJS2 bytecode (TJS2100)";
            case TjsFileKind.Kbad100BinaryData: return "binary TJS dictionary/array data (KBAD100)";
            default: return "TJS file format '" + fileKind + "'";
        }
    }

    private static string ManifestKind(TjsFileKind fileKind)
    {
        switch (fileKind)
        {
            case TjsFileKind.Tjs2100Bytecode: return "tjs2100-bytecode";
            case TjsFileKind.Kbad100BinaryData: return "kbad100-binary-data";
            default: return "source-text";
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: tjsparse parse <file|directory> [-o path] [--mode auto|script|expression]");
        Console.Error.WriteLine("                [--preprocess preserve|active] [-D NAME=VALUE] [--encoding name]");
        Console.Error.WriteLine("                [--kbad-json plain|typed] [--compact] [--no-comments]");
    }
}

internal sealed class CliOptions
{
    public string? Input { get; private set; }
    public string? Output { get; private set; }
    public bool Compact { get; private set; }
    public bool NoComments { get; private set; }
    public bool ShowHelp { get; private set; }
    public KbadJsonShape KbadJsonShape { get; private set; } = KbadJsonShape.Plain;
    public ParseOptions ParseOptions { get; } = new ParseOptions();

    public static CliOptions Parse(string[] args)
    {
        var result = new CliOptions();
        var i = 0;
        if (i < args.Length && args[i] == "parse") i++;
        while (i < args.Length)
        {
            var arg = args[i++];
            switch (arg)
            {
                case "-h": case "--help": result.ShowHelp = true; break;
                case "-o": case "--output": result.Output = RequireValue(args, ref i, arg); break;
                case "--mode": result.ParseOptions.RootMode = ParseMode(RequireValue(args, ref i, arg)); break;
                case "--preprocess": result.ParseOptions.PreprocessorMode = ParsePreprocessor(RequireValue(args, ref i, arg)); break;
                case "--encoding": result.ParseOptions.EncodingHint = RequireValue(args, ref i, arg); break;
                case "--kbad-json": result.KbadJsonShape = ParseKbadJsonShape(RequireValue(args, ref i, arg)); break;
                case "--compact": result.Compact = true; break;
                case "--no-comments": result.NoComments = true; break;
                case "-D": AddDefine(result.ParseOptions, RequireValue(args, ref i, arg)); break;
                default:
                    if (arg.StartsWith("-D", StringComparison.Ordinal) && arg.Length > 2) AddDefine(result.ParseOptions, arg.Substring(2));
                    else if (arg.StartsWith("-", StringComparison.Ordinal)) throw new CliException("Unknown option: " + arg);
                    else if (result.Input == null) result.Input = arg;
                    else throw new CliException("Only one input may be specified.");
                    break;
            }
        }
        return result;
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index >= args.Length) throw new CliException(option + " requires a value."); return args[index++];
    }

    private static RootMode ParseMode(string value) => value.ToLowerInvariant() switch
    { "auto" => RootMode.Auto, "script" => RootMode.Script, "expression" => RootMode.Expression, _ => throw new CliException("Invalid root mode: " + value) };

    private static PreprocessorMode ParsePreprocessor(string value) => value.ToLowerInvariant() switch
    { "preserve" => PreprocessorMode.PreserveAll, "active" => PreprocessorMode.ActiveOnly, _ => throw new CliException("Invalid preprocessor mode: " + value) };

    private static KbadJsonShape ParseKbadJsonShape(string value) => value.ToLowerInvariant() switch
    { "plain" => KbadJsonShape.Plain, "typed" => KbadJsonShape.Typed, _ => throw new CliException("Invalid KBAD JSON shape: " + value) };

    private static void AddDefine(ParseOptions options, string definition)
    {
        var split = definition.IndexOf('=');
        var name = split < 0 ? definition : definition.Substring(0, split);
        var text = split < 0 ? "1" : definition.Substring(split + 1);
        if (name.Length == 0) throw new CliException("Macro name may not be empty.");
        int value;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = Convert.ToInt32(text.Substring(2), 16);
        else if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) throw new CliException("Invalid macro value: " + definition);
        options.Defines[name] = value;
    }
}

internal sealed class CliException : Exception { public CliException(string message) : base(message) { } }

internal sealed class ManifestItem
{
    private ManifestItem(string path, string kind, string status, string? encoding, string? rootMode, int errorCount,
        string? output, string? failure, string? skipReason)
    {
        Path = path; Kind = kind; Status = status; Encoding = encoding; RootMode = rootMode;
        ErrorCount = errorCount; Output = output; Failure = failure; SkipReason = skipReason;
    }

    public static ManifestItem ParsedSource(string path, string encoding, string rootMode, string output)
        => new ManifestItem(path, "source-text", "parsed", encoding, rootMode, 0, output, null, null);

    public static ManifestItem ParsedKbad(string path, string output)
        => new ManifestItem(path, "kbad100-binary-data", "parsed", null, null, 0, output, null, null);

    public static ManifestItem Failed(string path, string kind, string? encoding, string? rootMode, int errorCount, string? output, string failure)
        => new ManifestItem(path, kind, "failed", encoding, rootMode, errorCount, output, failure, null);

    public static ManifestItem SkippedBytecode(string path)
        => new ManifestItem(path, "tjs2100-bytecode", "skipped", null, null, 0, null, null,
            "Compiled TJS2 bytecode is outside the supported parser scope.");

    public string Path { get; }
    public string Kind { get; }
    public string Status { get; }
    public string? Encoding { get; }
    public string? RootMode { get; }
    public int ErrorCount { get; }
    public string? Output { get; }
    public string? Failure { get; }
    public string? SkipReason { get; }
}
