using System;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Neo.BlockchainToolkit;
using Neo.BlockchainToolkit.Models;
using Neo.VM;

namespace DevHawk.DumpNef
{
    class Program
    {
        private static void Main(string[] args) => CommandLineApplication.Execute<Program>(args);

        [Argument(0)]
        [Required]
        internal string NefFile { get; init; } = string.Empty;

        [Option]
        internal bool DisableColors { get; }

        internal async Task<int> OnExecuteAsync(CommandLineApplication app, IConsole console)
        {
            if (!System.IO.File.Exists(NefFile))
            {
                await console.Error.WriteLineAsync($"{NefFile} cannot be found");
                return 1;
            }

            try
            {
                var nef = LoadNefFile(NefFile);
                var script = (Neo.VM.Script)nef.Script;
                var debugInfo = (await DebugInfo.LoadAsync(NefFile, null))
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
                        var doc = documents[sp.Document];
                        var line = doc.lines[sp.Start.line - 1].Substring(sp.Start.column - 1);
                        if (sp.Start.line == sp.End.line)
                        {
                            line = line.Substring(0, sp.End.column - sp.Start.column);
                        }

                        using var color = SetConsoleColor(ConsoleColor.Cyan);
                        Console.WriteLine($"# Code {doc.fileName} line {sp.Start.line}: \"{line}\"");
                    }

                    WriteInstruction(instructions[i].address, instructions[i].instruction, padString);

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
                console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        static Neo.SmartContract.NefFile LoadNefFile(string path)
        {
            using var stream = System.IO.File.OpenRead(path);
            using var reader = new System.IO.BinaryReader(stream, System.Text.Encoding.UTF8, false);
            return Neo.IO.Helper.ReadSerializable<Neo.SmartContract.NefFile>(reader);
        }

        void WriteInstruction(int address, Instruction instruction, string padString)
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

                var comment = instruction.GetComment(address);
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
