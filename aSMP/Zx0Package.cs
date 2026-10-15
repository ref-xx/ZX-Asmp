using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace aSMP
{
    // Keeps the loader and its stack below the expanded program. The packed stream
    // is moved above the entire destination before decoding, so overlap is impossible.
    internal static class Zx0Package
    {
        internal const int Origin = 32768;
        internal const int PayloadOrigin = 33280;
        internal const int StackPointer = PayloadOrigin - 4;
        internal const int MemoryEnd = 65021; // IM2 handler and table start here.

        internal static string RunTool(string toolsDirectory, string workingDirectory, string tool, string arguments)
        {
            string executable = Path.Combine(toolsDirectory, tool);
            if (!File.Exists(executable))
                throw new InvalidOperationException(tool + " is required. Put it in the aSMP program folder.");
            StringBuilder output = new StringBuilder();
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(executable, arguments);
                process.StartInfo.WorkingDirectory = workingDirectory;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                DataReceivedEventHandler collect = delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) lock (output) output.AppendLine(e.Data);
                };
                process.OutputDataReceived += collect;
                process.ErrorDataReceived += collect;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(tool + " failed:\r\n" + output);
            }
            return output.ToString();
        }

        internal static string Bytes(byte[] data)
        {
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                if (i % 32 == 0) result.Append("defb ");
                else result.Append(',');
                result.Append('#').Append(data[i].ToString("X2"));
                if (i % 32 == 31 || i == data.Length - 1) result.Append("\r\n");
            }
            return result.ToString();
        }

        internal static byte[] Compress(byte[] data, string toolsDirectory)
        {
            string temporary = Path.Combine(Path.GetTempPath(), "asmp-zx0-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                File.WriteAllBytes(Path.Combine(temporary, "input.bin"), data);
                RunTool(toolsDirectory, temporary, "zx0.exe", "-f input.bin packed.bin");
                return File.ReadAllBytes(Path.Combine(temporary, "packed.bin"));
            }
            finally { Directory.Delete(temporary, true); }
        }

        internal static string Build(string source, string decoder, string toolsDirectory, bool restoreBasic)
        {
            int split = source.IndexOf("goraster:", StringComparison.Ordinal);
            if (split < 0) throw new InvalidOperationException("Missing goraster entry point.");
            string prefix = source.Substring(0, split).Replace("stackpointer", "loader_stackpointer");
            string body = source.Substring(split);
            body = body.Substring(0, body.LastIndexOf("end 32768", StringComparison.Ordinal));
            // The saved registers must live outside the region being expanded.
            int stack = body.IndexOf(" stack:", StringComparison.Ordinal);
            if (stack >= 0) body = body.Substring(0, stack);
            body = body.Replace("stackpointer", "loader_stackpointer");
            string payload = "org " + PayloadOrigin + "\r\nloader_stackpointer equ " + StackPointer + "\r\n" + body;
            int decoderStart = decoder.IndexOf("dzx0_standard:", StringComparison.Ordinal);
            if (decoderStart < 0) throw new InvalidOperationException("Missing ZX0 decoder in txtZX0.");
            decoder = decoder.Substring(decoderStart);
            string temporary = Path.Combine(Path.GetTempPath(), "asmp-code-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                File.WriteAllText(Path.Combine(temporary, "payload.asm"), payload, Encoding.ASCII);
                RunTool(toolsDirectory, temporary, "pasmo.exe", "--bin payload.asm payload.bin");
                byte[] raw = File.ReadAllBytes(Path.Combine(temporary, "payload.bin"));
                if (raw.Length == 0 || PayloadOrigin + raw.Length > MemoryEnd)
                    throw new InvalidOperationException("Expanded code does not fit below the IM2 area (65021).");
                byte[] packed = Compress(raw, toolsDirectory);
                int packedAddress = MemoryEnd - packed.Length;
                if (PayloadOrigin + raw.Length > packedAddress)
                    throw new InvalidOperationException("Not enough RAM to safely unpack this program: expanded " + raw.Length +
                        " bytes, packed " + packed.Length + " bytes. Reduce the raster range or disable Compress Code.");

                StringBuilder result = new StringBuilder();
                result.Append("; ZX0 whole package: ").Append(raw.Length).Append(" -> ").Append(packed.Length).Append(" bytes\r\n");
                result.Append("loader_stackpointer equ ").Append(StackPointer).Append("\r\n").Append(prefix);
                if (!restoreBasic) result.Append("ld sp,loader_stackpointer\r\n");
                result.Append("; Relocate compressed input clear of the destination.\r\n")
                    .Append("ld hl,packed_program_end-1\r\nld de,").Append(MemoryEnd - 1)
                    .Append("\r\nld bc,").Append(packed.Length).Append("\r\nlddr\r\n")
                    .Append("ld hl,").Append(packedAddress).Append("\r\nld de,").Append(PayloadOrigin)
                    .Append("\r\ncall dzx0_standard\r\njp ").Append(PayloadOrigin).Append("\r\n")
                    .Append(decoder).Append("\r\n");
                // Reserve 64 bytes for register backup and decoder calls below SP.
                result.Append("defs ").Append(StackPointer - 64).Append("-$,0\r\ndefs 68,0\r\n")
                    .Append("packed_program:\r\n").Append(Bytes(packed))
                    .Append("packed_program_end:\r\nend 32768\r\n");
                string finalSource = result.ToString();
                File.WriteAllText(Path.Combine(temporary, "package.asm"), finalSource, Encoding.ASCII);
                RunTool(toolsDirectory, temporary, "pasmo.exe", "--bin package.asm package.bin");
                return finalSource;
            }
            finally { Directory.Delete(temporary, true); }
        }
    }
}
