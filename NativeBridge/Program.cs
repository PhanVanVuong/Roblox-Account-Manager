using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;
using System.Collections.Generic;

internal static class NativeBridge
{
    const int Limit = 262144;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = Limit };
    static byte[] ReadExactly(Stream stream, int count)
    {
        var data = new byte[count];
        int offset = 0;
        while (offset < count) { int read = stream.Read(data, offset, count - offset); if (read == 0) return null; offset += read; }
        return data;
    }
    static void Main(string[] args)
    {
        if (args.Length == 0 || args[0] != "chrome-extension://mjapekjkfljcmpclkafajgmenailmkcn/") return;
        using (var input = Console.OpenStandardInput())
        using (var output = Console.OpenStandardOutput())
        {
            for (;;)
            {
                var header = ReadExactly(input, 4);
                if (header == null) return;
                int size = BitConverter.ToInt32(header, 0);
                if (size <= 0 || size > Limit) return;
                var payload = ReadExactly(input, size);
                if (payload == null) return;
                object requestId = null;
                string response;
                try
                {
                    string text = Encoding.UTF8.GetString(payload);
                    var request = Json.Deserialize<Dictionary<string, object>>(text);
                    request.TryGetValue("requestId", out requestId);
                    using (var pipe = new NamedPipeClientStream(".", "RAM.Chrome." + WindowsIdentity.GetCurrent().User.Value, PipeDirection.InOut))
                    {
                        pipe.Connect(2000);
                        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                        using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true))
                        {
                            writer.WriteLine(text);
                            var pending = reader.ReadLineAsync();
                            if (!pending.Wait(45000)) throw new IOException();
                            response = pending.Result;
                            if (response == null || Encoding.UTF8.GetByteCount(response) > Limit) throw new IOException();
                        }
                    }
                }
                catch (Exception) { response = Json.Serialize(new { requestId, ok = false, error = "Mở Roblox Account Manager và chọn Chrome Extension hoặc thêm tài khoản trước." }); }
                var bytes = Encoding.UTF8.GetBytes(response);
                output.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                output.Write(bytes, 0, bytes.Length);
                output.Flush();
            }
        }
    }
}
