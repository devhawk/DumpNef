using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
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

                var instructions = script.EnumerateInstructions().ToList();
                var padString = script.GetInstructionAddressPadding();

                var start = true;
                if (debugInfo == null)
                {
                    for (int i = 0; i < instructions.Count; i++)
                    {
                        WriteNewLine(ref start);
                        WriteInstruction(instructions[i].address, instructions[i].instruction, padString);
                    }
                }
                else
                {
                    var documents = debugInfo.Documents
                        .Select(path => (fileName: Path.GetFileName(path), lines: File.ReadAllLines(path)))
                        .ToArray();

                    foreach (var method in debugInfo.Methods.OrderBy(m => m.Range.Start))
                    {
                        var originalForegroundColor = Console.ForegroundColor;
                        try
                        {
                            Console.ForegroundColor = ConsoleColor.Magenta;
                            WriteNewLine(ref start);
                            Console.Write($"# Method Start {method.Namespace}.{method.Name}");

                            var methodInstructions = instructions
                                .SkipWhile(t => t.address < method.Range.Start)
                                .TakeWhile(t => t.address <= method.Range.End);

                            var sequencePoints = method.SequencePoints.ToDictionary(sp => sp.Address);
                            foreach (var t in methodInstructions)
                            {
                                if (sequencePoints.TryGetValue(t.address, out var sp)
                                    && sp.Document < documents.Length)
                                {
                                    var doc = documents[sp.Document];
                                    var line = doc.lines[sp.Start.line - 1].Substring(sp.Start.column - 1);
                                    if (sp.Start.line == sp.End.line)
                                    {
                                        line = line.Substring(0, sp.End.column - sp.Start.column);
                                    }

                                    Console.ForegroundColor = ConsoleColor.Cyan;
                                    WriteNewLine(ref start);
                                    Console.Write($"# Code {doc.fileName} line {sp.Start.line}: \"{line}\"");
                                }

                                Console.ForegroundColor = originalForegroundColor;
                                WriteNewLine(ref start);
                                WriteInstruction(t.address, t.instruction, padString);
                            }

                            Console.ForegroundColor = ConsoleColor.Magenta;
                            WriteNewLine(ref start);
                            Console.Write($"# Method End {method.Namespace}.{method.Name}");
                        }
                        finally
                        {
                            console.ForegroundColor = originalForegroundColor;
                        }
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                await console.Error.WriteLineAsync(ex.Message);
                return 1;
            }

            static void WriteNewLine(ref bool start)
            {
                if (start) start = false; else Console.WriteLine();
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
    }
}
