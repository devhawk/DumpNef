using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Neo.BlockchainToolkit;
using Neo.BlockchainToolkit.Models;
using Neo.IO;
using Neo.Json;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native;
using Neo.VM;

namespace DevHawk.DumpNef
{
    class Program
    {
        private static void Main(string[] args) => CommandLineApplication.Execute<Program>(args);

        [Argument(0, "Path to .NEF file or contract hex string")]
        [Required]
        internal string Input { get; init; } = string.Empty;

        [Option(Description = "Disable colors in Neo VM output")]
        internal bool DisableColors { get; }

        [Option]
        internal bool MethodTokens { get; }

        internal async Task<int> OnExecuteAsync(CommandLineApplication app, IConsole console)
        {
            try
            {
                if (!TryLoadScript(Input, out var script, out var tokens))
                {
                    throw new Exception($"{nameof(Input)} must be a path to an .nef file or a Base64 or Hex encoded Neo.VM script");
                }

                if (MethodTokens)
                {
                    if (tokens.Length == 0)
                    {
                        console.WriteLine("No Method Tokens in contract");
                    }
                    else
                    {
                        for (ushort i = 0; i < tokens.Length; i++)
                        {
                            var token = tokens[i];
                            var contract = NativeContract.Contracts.SingleOrDefault(d => d.Hash == token.Hash);
                            var contractName = contract?.Name ?? $"{token.Hash}";

                            console.WriteLine($"{AsByteString(i)}: {contractName} {token.Method}");
                            console.WriteLine($"\t{token.ParametersCount} parameter{(token.ParametersCount == 1 ? "" : "s")}");
                            console.WriteLine($"\t{(token.HasReturnValue ? "has" : "does not have")} return value");
                            console.WriteLine($"\t{token.CallFlags} call flags");
                        }
                    }
                    return 0;

                    // convert index to hex-string similar to Instruction.GetOperandString does
                    static string AsByteString(ushort index)
                    {
                        Span<byte> indexSpan = stackalloc byte[sizeof(ushort)];
                        BinaryPrimitives.WriteUInt16LittleEndian(indexSpan, index);

                        var builder = new System.Text.StringBuilder(indexSpan.Length * 3 - 1);
                        var first = true;
                        for (var i = 0; i < indexSpan.Length; i++)
                        {
                            if (first) { first = false; } else { builder.Append('-'); }
                            var value = indexSpan[i];
                            builder.Append(GetHexValue(value / 16));
                            builder.Append(GetHexValue(value % 16));
                        }
                        return builder.ToString();

                        static char GetHexValue(int i) => (i < 10) ? (char)(i + '0') : (char)(i - 10 + 'A');
                    }
                }

                var debugInfo = (await DebugInfo.LoadContractDebugInfoAsync(Input, null).ConfigureAwait(false))
                    .Match<DebugInfo?>(di => di, _ => null);

                IReadOnlyList<(string, string[])> documents;
                IReadOnlyDictionary<int, string> methodStarts, methodEnds;
                IReadOnlyDictionary<int, DebugInfo.SequencePoint> sequencePoints;
                if (debugInfo is null)
                {
                    documents = Enumerable.Empty<(string, string[])>().ToList();
                    methodStarts = TryLoadManifest(Input, out var manifest)
                        ? manifest.Abi.Methods.ToDictionary(m => m.Offset, m => m.Name)
                        : new Dictionary<int, string>();
                    methodEnds = new Dictionary<int, string>();
                    sequencePoints = new Dictionary<int, DebugInfo.SequencePoint>();
                }
                else
                {
                    documents = debugInfo.Documents.Select(p => SelectDocument(debugInfo, p)).ToList();
                    methodStarts = debugInfo.Methods.ToDictionary(m => m.Range.Start, m => $"{m.Namespace}.{m.Name}");
                    methodEnds = debugInfo.Methods.ToDictionary(m => m.Range.End, m => $"{m.Namespace}.{m.Name}");
                    sequencePoints = debugInfo.Methods.SelectMany(m => m.SequencePoints)
                        .ToDictionary(s => s.Address);
                }

                static (string filename, string[] lines) SelectDocument(DebugInfo debugInfo, string path)
                {
                    if (!string.IsNullOrEmpty(debugInfo.DocumentRoot))
                    {
                        path = Path.Combine(debugInfo.DocumentRoot, path);
                    }

                    return (Path.GetFileName(path), File.ReadAllLines(path));
                }

                var instructions = script.EnumerateInstructions().ToList();
                var padString = script.GetInstructionAddressPadding();

                var start = true;
                for (int i = 0; i < instructions.Count; i++)
                {
                    if (start) start = false; else Console.WriteLine();

                    if (methodStarts.TryGetValue(instructions[i].address, out var methodStart))
                    {
                        using var color = SetConsoleColor(ConsoleColor.Magenta);
                        Console.WriteLine($"# Method Start {methodStart}");
                    }

                    if (sequencePoints.TryGetValue(instructions[i].address, out var sp)
                        && sp.Document < documents.Count)
                    {
                        var (fileName, lines) = documents[sp.Document];
                        var line = lines[sp.Start.Line - 1][(sp.Start.Column - 1)..];
                        if (sp.Start.Line == sp.End.Line)
                        {
                            line = line[..(sp.End.Column - sp.Start.Column)];
                        }

                        using var color = SetConsoleColor(ConsoleColor.Cyan);
                        Console.WriteLine($"# Code {fileName} line {sp.Start.Line}: \"{line}\"");
                    }

                    WriteInstruction(instructions[i].address, instructions[i].instruction, padString, tokens);

                    if (methodEnds.TryGetValue(instructions[i].address, out var methodEnd))
                    {
                        using var color = SetConsoleColor(ConsoleColor.Magenta);
                        Console.Write($"\n# Method End {methodEnd}");
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                await console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
                return 1;
            }
        }

        static bool TryLoadManifest(string input, [MaybeNullWhen(false)] out ContractManifest manifest)
        {
            var path = Path.ChangeExtension(input, ".manifest.json");

            try
            {
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path);
                    var json = JToken.Parse(text) as JObject;
                    manifest = ContractManifest.FromJson(json);
                    return true;
                }
            }
            catch { }

            manifest = default;
            return false;
        }

        static bool TryLoadScript(string input, out Script script, out MethodToken[] tokens)
        {
            if (File.Exists(input))
            {
                var bytes = File.ReadAllBytes(input);
                var reader = new MemoryReader(bytes);
                var nefFile = reader.ReadSerializable<NefFile>();
                script = nefFile.Script;
                tokens = nefFile.Tokens;
                return true;
            }

            tokens = Array.Empty<MethodToken>();

            var pool = ArrayPool<byte>.Shared;
            var buffer = input.Length < 256
                ? null
                : pool.Rent(input.Length);
            try
            {
                Span<byte> span = input.Length < 256
                    ? stackalloc byte[input.Length]
                    : buffer.AsSpan(0, input.Length);
                if (Convert.TryFromBase64String(input, span, out var bytesWritten))
                {
                    script = span[..bytesWritten].ToArray();
                    return true;
                }
            }
            finally
            {
                if (buffer is not null) pool.Return(buffer);
            }

            try
            {
                script = Convert.FromHexString(input);
                return true;
            }
            catch (FormatException)
            {
                script = Array.Empty<byte>();
                return false;
            }
        }

        static void WriteInstruction(int address, Instruction instruction, string padString, MethodToken[] tokens)
        {
            var originalForegroundColor = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"{address.ToString(padString)}");

                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write($" {instruction.OpCode}");

                if (!instruction.Operand.IsEmpty)
                {
                    Console.Write($" {instruction.GetOperandString()}");
                }

                var comment = instruction.GetComment(address, tokens);
                if (comment.Length > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($" # {comment}");
                }
            }
            finally
            {
                Console.ForegroundColor = originalForegroundColor;
            }
        }

        IDisposable SetConsoleColor(ConsoleColor foregroundColor, ConsoleColor? backgroundColor = null)
        {
            if (DisableColors) return Nito.Disposables.NoopDisposable.Instance;
            return new ConsoleColorManager(foregroundColor, backgroundColor);
        }

        class ConsoleColorManager : IDisposable
        {
            readonly ConsoleColor originalForegroundColor;
            readonly ConsoleColor? originalBackgroundColor;

            public ConsoleColorManager(ConsoleColor foregroundColor, ConsoleColor? backgroundColor = null)
            {
                originalForegroundColor = Console.ForegroundColor;
                originalBackgroundColor = backgroundColor.HasValue ? Console.BackgroundColor : null;

                Console.ForegroundColor = foregroundColor;
                if (backgroundColor.HasValue)
                {
                    Console.BackgroundColor = backgroundColor.Value;
                }
            }

            public void Dispose()
            {
                Console.ForegroundColor = originalForegroundColor;
                if (originalBackgroundColor.HasValue)
                {
                    Console.BackgroundColor = originalBackgroundColor.Value;
                }
            }
        }
    }
}
