using System;
using System.Buffers;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Neo.BlockchainToolkit;
using Neo.BlockchainToolkit.Models;
using Neo.IO;
using Neo.SmartContract;
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
                    foreach (var token in tokens.OrderBy(t => t.Hash))
                    {
                        var contract = NativeContract.Contracts.SingleOrDefault(d => d.Hash == token.Hash);
                        var contractName = contract?.Name ?? $"{token.Hash}";

                        console.WriteLine($"{contractName} {token.Method}");
                    }
                    return 0;
                }

                var debugInfo = (await DebugInfo.LoadContractDebugInfoAsync(Input, null).ConfigureAwait(false))
                    .Match<DebugInfo?>(di => di, _ => null);

                var documents = debugInfo?.Documents
                    .Select(path => (fileName: System.IO.Path.GetFileName(path), lines: System.IO.File.ReadAllLines(path)))
                    .ToImmutableList() ?? ImmutableList<(string, string[])>.Empty;
                var methodStarts = debugInfo?.Methods.ToImmutableDictionary(m => m.Range.Start)
                    ?? ImmutableDictionary<int, DebugInfo.Method>.Empty;
                var methodEnds = debugInfo?.Methods.ToImmutableDictionary(m => m.Range.End)
                    ?? ImmutableDictionary<int, DebugInfo.Method>.Empty;
                var sequencePoints = debugInfo?.Methods.SelectMany(m => m.SequencePoints).ToImmutableDictionary(s => s.Address)
                    ?? ImmutableDictionary<int, DebugInfo.SequencePoint>.Empty;

                var instructions = script.EnumerateInstructions().ToList();
                var padString = script.GetInstructionAddressPadding();

                var start = true;
                for (int i = 0; i < instructions.Count; i++)
                {
                    if (start) start = false; else Console.WriteLine();

                    if (methodStarts.TryGetValue(instructions[i].address, out var methodStart))
                    {
                        using var color = SetConsoleColor(ConsoleColor.Magenta);
                        Console.WriteLine($"# Method Start {methodStart.Namespace}.{methodStart.Name}");
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
                        Console.Write($"\n# Method End {methodEnd.Namespace}.{methodEnd.Name}");
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
