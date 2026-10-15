using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using aSMP;

class CompressionSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(Form1 f, string name) { return typeof(Form1).GetField(name, Private).GetValue(f); }
    static object Call(Form1 f, string name, params object[] args)
    {
        Type[] types = args.Select(a => a.GetType()).ToArray();
        return typeof(Form1).GetMethod(name, Private, null, types, null).Invoke(f, args);
    }
    static void Equal(byte[] a, byte[] b, string message)
    {
        if (!a.SequenceEqual(b)) throw new Exception(message);
    }
    [STAThread]
    static void Main(string[] args)
    {
        string tools = Application.StartupPath;
        string work = Path.Combine(Path.GetTempPath(), "asmp-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            using (Form1 form = new Form1())
            {
                typeof(Form1).GetMethod("Form1_Load", Private).Invoke(form, new object[] { form, EventArgs.Empty });
                ((Timer)Field(form, "timer1")).Enabled = false;
                if (args.Length > 0)
                {
                    Call(form, "loader", args[0]);
                    Call(form, "detectstandards");
                }
                ComboBox compression = (ComboBox)Field(form, "comboBox5");
                if (compression.Items.Count != 2 || compression.SelectedIndex != 1)
                    throw new Exception("Expected only none/zx0, with zx0 default.");
                CheckBox full = (CheckBox)Field(form, "checkBoxCompressCode");
                for (int model = 0; model < 3; model++)
                for (int key = 0; key < 2; key++)
                {
                    ((ComboBox)Field(form, "comboBox2")).SelectedIndex = model;
                    ((CheckBox)Field(form, "checkKEY")).Checked = key != 0;
                    full.Checked = false;
                    compression.SelectedIndex = 0;
                    string plain = (string)Call(form, "generatecode");
                    File.WriteAllText(Path.Combine(work, "plain.asm"), plain);
                    Zx0Package.RunTool(tools, work, "pasmo.exe", "--bin plain.asm plain.bin");
                    byte[] bitmap = (byte[])Call(form, "packbitmap");
                    byte[] bitmapPacked = Zx0Package.Compress(bitmap, tools);
                    File.WriteAllBytes(Path.Combine(work, "bitmap.zx0"), bitmapPacked);
                    Zx0Package.RunTool(tools, work, "dzx0.exe", "-f bitmap.zx0 bitmap.bin");
                    Equal(bitmap, File.ReadAllBytes(Path.Combine(work, "bitmap.bin")), "Bitmap round trip failed.");
                    compression.SelectedIndex = 1;
                    string bitmapOnly = (string)Call(form, "generatecode");
                    File.WriteAllText(Path.Combine(work, "bitmap.asm"), bitmapOnly);
                    Zx0Package.RunTool(tools, work, "pasmo.exe", "--bin bitmap.asm bitmap-package.bin");
                    full.Checked = true;
                    string packed = (string)Call(form, "generatecode");
                    compression.SelectedIndex = 0;
                    if (packed != (string)Call(form, "generatecode"))
                        throw new Exception("Whole-package mode must compress the bitmap even with none selected.");
                    File.WriteAllText(Path.Combine(work, "package.asm"), packed);
                    Zx0Package.RunTool(tools, work, "pasmo.exe", "--bin package.asm package.bin");
                    Zx0Package.RunTool(tools, work, "pasmo.exe", "--tapbas package.asm package.tap");
                    byte[] package = File.ReadAllBytes(Path.Combine(work, "package.bin"));
                    File.WriteAllBytes(Path.Combine(work, "payload.zx0"), package.Skip(512).ToArray());
                    Zx0Package.RunTool(tools, work, "dzx0.exe", "-f payload.zx0 payload.bin");
                    string body = plain.Substring(plain.IndexOf("goraster:", StringComparison.Ordinal));
                    body = body.Substring(0, body.LastIndexOf("end 32768", StringComparison.Ordinal));
                    int stack = body.IndexOf(" stack:", StringComparison.Ordinal);
                    if (stack >= 0) body = body.Substring(0, stack);
                    body = body.Replace("goraster:\r\n", "goraster:\r\nLD HL,bitmap\r\nLD DE,16384\r\nLD BC,6144\r\nLDIR\r\n")
                        .Replace("stackpointer", "loader_stackpointer");
                    File.WriteAllText(Path.Combine(work, "expected.asm"), "org 33280\r\nloader_stackpointer equ 33276\r\n" + body);
                    Zx0Package.RunTool(tools, work, "pasmo.exe", "--bin expected.asm expected.bin");
                    Equal(File.ReadAllBytes(Path.Combine(work, "payload.bin")), File.ReadAllBytes(Path.Combine(work, "expected.bin")), "Expanded code differs.");
                    LoaderMachine.Check(package, File.ReadAllBytes(Path.Combine(work, "expected.bin")), bitmap, key != 0);
                    Console.WriteLine("PASS model={0} BASIC-return={1}: plain={2}, bitmap-only={3}, whole={4} bytes", model, key,
                        new FileInfo(Path.Combine(work, "plain.bin")).Length, new FileInfo(Path.Combine(work, "bitmap-package.bin")).Length, package.Length);
                }
                string decoder = ((TextBox)Field(form, "txtZX0")).Text;
                bool rejected = false;
                try { Zx0Package.Build("org 32768\r\ndi\r\ngoraster:\r\ndefs 32000,0\r\nend 32768", decoder, tools, false); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected) throw new Exception("Oversize program was accepted.");
                rejected = false;
                try { Zx0Package.Compress(new byte[] {1, 2, 3}, work); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected) throw new Exception("Missing tool was accepted.");
                Console.WriteLine("PASS oversized payload and missing-tool errors.");
            }
        }
        finally { Directory.Delete(work, true); }
    }
}
