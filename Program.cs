﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
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
                var debugInfo = await DebugInfoParser.LoadAsync(NefFile, null);

                var instructions = EnumerateInstructions(nef.Script).ToList();
                var digitCount = DigitCount(instructions.Last().address);
                var padString = new string('0', digitCount);

                var start = true;
                if (debugInfo == null)
                {
                    foreach (var t in EnumerateInstructions(nef.Script))
                    {
                        WriteNewLine(ref start);
                        WriteInstruction(t.address, t.instruction, padString);
                    }
                }
                else
                {
                    var documents = debugInfo.Documents
                        .Select(path => (fileName: Path.GetFileName(path), lines: File.ReadAllLines(path)))
                        .ToArray();

                    foreach (var method in debugInfo.Methods.OrderBy(m => m.Range.Start))
                    {
                        WriteNewLine(ref start);
                        using (var _ = SetConsoleColor(ConsoleColor.Magenta))
                        {
                            Console.Write($"# Method Start {method.Namespace}.{method.Name}");
                        }

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
                                
                                WriteNewLine(ref start);
                                using (var _ = SetConsoleColor(ConsoleColor.Cyan))
                                {
                                    Console.Write($"# Code {doc.fileName} line {sp.Start.line}: \"{line}\"");
                                }
                            }

                            WriteNewLine(ref start);
                            WriteInstruction(t.address, t.instruction, padString);
                        }

                        WriteNewLine(ref start);
                        using (var _ = SetConsoleColor(ConsoleColor.Magenta))
                        {
                            Console.Write($"# Method End {method.Namespace}.{method.Name}");
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

        static IEnumerable<(int address, Instruction instruction)> EnumerateInstructions(Script script)
        {
            var address = 0;
            var opcode = OpCode.PUSH0;
            while (address < script.Length)
            {
                var instruction = script.GetInstruction(address);
                opcode = instruction.OpCode;
                yield return (address, instruction);
                address += instruction.Size;
            }

            if (opcode != OpCode.RET)
            {
                yield return (address, Instruction.RET);
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
            using (var _ = SetConsoleColor(ConsoleColor.Yellow))
            {
                Console.Write($"{address.ToString(padString)}");
            }

            using (var _ = SetConsoleColor(ConsoleColor.Blue))
            {
                Console.Write($" {instruction.OpCode}");
            }

            if (!instruction.Operand.IsEmpty)
            {
                Console.Write($" {GetOperandString(instruction)}");
            }

            var comment = GetComment(instruction, address);
            if (comment.Length > 0)
            {
                using (var _ = SetConsoleColor(ConsoleColor.Green))
                {
                    Console.Write($" # {comment}");
                }
            }
        }

        static string GetOperandString(Instruction instruction)
        {
            return string.Create<ReadOnlyMemory<byte>>(instruction.Operand.Length * 3 - 1,
                instruction.Operand, (span, memory) =>
                {
                    var first = memory.Span[0];
                    span[0] = GetHexValue(first / 16);
                    span[1] = GetHexValue(first % 16);

                    var index = 1;
                    for (var i = 2; i < span.Length; i += 3)
                    {
                        var b = memory.Span[index++];
                        span[i] = '-';
                        span[i + 1] = GetHexValue(b / 16);
                        span[i + 2] = GetHexValue(b % 16);
                    }
                });

            static char GetHexValue(int i) => (i < 10) ? (char)(i + '0') : (char)(i - 10 + 'A');
        }

        static readonly IReadOnlyDictionary<uint, string> sysCallNames =
            Neo.SmartContract.ApplicationEngine.Services
                .ToImmutableDictionary(kvp => kvp.Value.Hash, kvp => kvp.Value.Name);

        static string GetComment(Instruction instruction, int ip)
        {
            switch (instruction.OpCode)
            {
                case OpCode.PUSHINT8:
                case OpCode.PUSHINT16:
                case OpCode.PUSHINT32:
                case OpCode.PUSHINT64:
                case OpCode.PUSHINT128:
                case OpCode.PUSHINT256:
                    return $"{new System.Numerics.BigInteger(instruction.Operand.Span)}";
                case OpCode.PUSHM1:
                    return $"{(int)instruction.OpCode - (int)OpCode.PUSH0}";
                case OpCode.PUSHDATA1:
                case OpCode.PUSHDATA2:
                case OpCode.PUSHDATA4:
                    {
                        var text = System.Text.Encoding.UTF8.GetString(instruction.Operand.Span)
                            .Replace("\r", "\"\\r\"").Replace("\n", "\"\\n\"");
                        if (instruction.Operand.Length == 20)
                        {
                            return $"as script hash: {new Neo.UInt160(instruction.Operand.Span)}, as text: \"{text}\"";
                        }
                        return $"as text: \"{text}\"";
                    }
                case OpCode.SYSCALL:
                    return sysCallNames.TryGetValue(instruction.TokenU32, out var name)
                        ? name
                        : $"Unknown SysCall {instruction.TokenU32}";
                case OpCode.INITSLOT:
                    return $"{instruction.TokenU8} local variables, {instruction.TokenU8_1} arguments";
                case OpCode.JMP_L:
                case OpCode.JMPEQ_L:
                case OpCode.JMPGE_L:
                case OpCode.JMPGT_L:
                case OpCode.JMPIF_L:
                case OpCode.JMPIFNOT_L:
                case OpCode.JMPLE_L:
                case OpCode.JMPLT_L:
                case OpCode.JMPNE_L:
                case OpCode.CALL_L:
                    return OffsetComment(instruction.TokenI32);
                case OpCode.JMP:
                case OpCode.JMPEQ:
                case OpCode.JMPGE:
                case OpCode.JMPGT:
                case OpCode.JMPIF:
                case OpCode.JMPIFNOT:
                case OpCode.JMPLE:
                case OpCode.JMPLT:
                case OpCode.JMPNE:
                case OpCode.CALL:
                    return OffsetComment(instruction.TokenI8);
                default:
                    return string.Empty;
            }

            string OffsetComment(int offset) => $"pos: {ip + offset}, offset: {offset}";
        }

        static int DigitCount(int n)
        {
            if (n < 10) return 1;
            if (n < 100) return 2;
            if (n < 1000) return 3;
            if (n < 10000) return 4;
            if (n < 100000) return 5;
            if (n < 1000000) return 6;
            if (n < 10000000) return 7;
            if (n < 100000000) return 8;
            if (n < 1000000000) return 9;
            return 10;
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
