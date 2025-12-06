using Discord;
using Discord.WebSocket;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Security.Cryptography;
using Websocket.Client;
using System.Net.WebSockets;       // <<< REQUIRED FIX
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Text.RegularExpressions;

namespace GalacticDecompilerBot
{
    public static class Program
    {
        private static DiscordSocketClient _client;
        private static readonly ulong TargetChannel = 1444258745336070164;

        public static async Task Main()
        {
            var token = Environment.GetEnvironmentVariable("BOT_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("BOT_TOKEN missing");
                return;
            }

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All
            });

            _client.Ready += () =>
            {
                _client.SetActivityAsync(new Game("Galactic Websocket Decompiler"));
                return Task.CompletedTask;
            };

            _client.MessageReceived += HandleMessage;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private static async Task HandleMessage(SocketMessage msg)
        {
            if (msg.Author.IsBot) return;
            if (msg.Channel.Id != TargetChannel && msg.Channel is not SocketDMChannel) return;
            if (msg.Attachments.Count == 0) return;

            var att = msg.Attachments.First();
            string temp = Path.GetTempFileName() + ".lua";

            using (HttpClient hc = new HttpClient())
            {
                var data = await hc.GetByteArrayAsync(att.Url);
                File.WriteAllBytes(temp, data);
            }

            var source = File.ReadAllText(temp);
            var deob = new Deobfuscator().Deobfuscate(source);

            string bcFile = Rand(8) + ".luac";

            using (var fs = new FileStream(bcFile, FileMode.Create))
            using (var ser = new Serializer(fs))
                ser.Serialize(deob);

            string result = await WebsocketDecompile(bcFile);

            result = LuaRenamer.Process(result);

            string outLua = Rand(8) + ".lua";
            File.WriteAllText(outLua, result);

            await msg.Channel.SendMessageAsync("done");

            using (var fs = new FileStream(outLua, FileMode.Open))
                await msg.Channel.SendFileAsync(fs, outLua);
        }

        private static async Task<string> WebsocketDecompile(string bytecodePath)
        {
            string luaResult = "";

            string sid = Guid.NewGuid().ToString("N").Substring(0, 20);
            var (key, iv) = Crypto.WebsocketKeyIV(sid);

            var url = new Uri("wss://luadecs.metaworm.site:3333/");
            using var client = new WebsocketClient(url);

            client.MessageReceived
                .Where(msg => msg.MessageType == WebSocketMessageType.Binary)
                .Subscribe(msg =>
                {
                    try
                    {
                        var dec = Crypto.AESCFBDecrypt(key, iv, msg.Binary);
                        luaResult += Encoding.UTF8.GetString(dec);
                    }
                    catch { }
                });

            await client.Start();

            client.Send($"40/sio,{{\"sid\":\"{sid}\"}}");

            await Task.Delay(200);

            byte[] raw = File.ReadAllBytes(bytecodePath);
            byte[] pkt = Crypto.AESCFBEncrypt(key, iv, raw);

            client.Send(pkt);

            await Task.Delay(1500);

            return luaResult;
        }

        private static string Rand(int len)
        {
            const string c = "abcdefghijklmnopqrstuvwxyz";
            var rng = RandomNumberGenerator.Create();
            char[] b = new char[len];

            for (int i = 0; i < len; i++)
            {
                byte[] x = new byte[1];
                rng.GetBytes(x);
                b[i] = c[x[0] % c.Length];
            }
            return new string(b);
        }
    }

    public static class LuaRenamer
    {
        public static string Process(string src)
        {
            src = RemoveComments(src);
            src = RenameVariables(src);
            src = RenameFunctions(src);

            return "-- this file was deobfuscated by galactic join now\n\n" + src;
        }

        private static string RemoveComments(string src)
        {
            var lines = src.Split('\n');
            var clean = lines.Where(l => !l.TrimStart().StartsWith("--"));
            return string.Join("\n", clean);
        }

        private static string RenameVariables(string src)
        {
            var vars = new Dictionary<string, string>();
            int count = 1;

            return Regex.Replace(
                src,
                @"\b([A-Za-z_][A-Za-z0-9_]*)\b",
                m =>
                {
                    string name = m.Value;

                    if (IsKeyword(name)) return name;

                    if (!vars.ContainsKey(name))
                    {
                        vars[name] = "V" + count++;
                    }

                    return vars[name];
                });
        }

        private static string RenameFunctions(string src)
        {
            var funcs = new Dictionary<string, string>();
            int count = 1;

            return Regex.Replace(
                src,
                @"function\s+([A-Za-z_][A-Za-z0-9_]*)",
                m =>
                {
                    string old = m.Groups[1].Value;

                    if (!funcs.ContainsKey(old))
                        funcs[old] = "OX" + count++;

                    return "function " + funcs[old];
                });
        }

        private static bool IsKeyword(string x)
        {
            string[] keys =
            {
                "local","function","end","if","then","else","elseif",
                "while","do","repeat","until","for","in","return","break",
                "true","false","nil","and","or","not"
            };

            return keys.Contains(x);
        }
    }

    public static class Crypto
    {
        private static readonly byte[] SkinA =
        {
            0x2B,0xC0,0x73,0xA3,0x29,0xC5,0x93,0xD7,0x7E,0x9E,0x4D,0x52,0xFD,0xEB,0xFC,0x7A,
            0xCB,0x9F,0x5A,0x5B,0xDE,0x6C,0x81,0xE8,0x26,0x7C,0xF7,0xFB,0x76,0xD3,0x11,0xAC
        };

        private static readonly byte[] SkinB =
        {
            0x72,0x94,0x84,0xB1,0xD3,0xC3,0xEA,0xDB,0xC4,0xAC,0x9D,0x86,0x77,0x5E,0x59,0xEF,
            0xD6,0xD1,0xD3,0xEF,0xB2,0xD3,0xEF,0xB2,0x83,0xA7,0x7B,0xA7,0xCB,0x9F,0xB6,0xD8
        };

        public static (byte[], byte[]) WebsocketKeyIV(string sid)
        {
            byte[] sidb = SID(sid);
            byte[] x1 = XOR(sidb, SkinB, true);
            byte[] x2 = XOR(SkinA, x1, false);

            string pwd = Encoding.Unicode.GetString(x2);

            using var derive = new Rfc2898DeriveBytes(
                pwd,
                new byte[] {0x4C,0x82,0xA1,0x18,0x24,0x64,0x15,0x96},
                1);

            byte[] full = derive.GetBytes(0x30);

            byte[] key = full.Take(0x20).ToArray();
            byte[] iv = full.Skip(0x20).Take(0x10).ToArray();
            return (key, iv);
        }

        private static byte[] SID(string sid)
        {
            var s = Encoding.UTF8.GetBytes(sid);
            byte[] o = new byte[32];
            for (int i = 0; i < s.Length && i < 32; i++)
            {
                byte v = s[i];
                o[i] = (byte)((v - (v >= 0x3A ? 0x57 : 0x30)) & 0xFF);
            }
            return o;
        }

        private static byte[] XOR(byte[] a, byte[] b, bool inv)
        {
            if (inv)
                b = b.Select(x => (byte)(~x & 0xFF)).ToArray();
            return a.Zip(b, (x, y) => (byte)(x ^ y)).ToArray();
        }

        public static byte[] AESCFBEncrypt(byte[] key, byte[] iv, byte[] pt)
        {
            int pad = 16 - (pt.Length % 16);
            byte[] padded = pt.Concat(Enumerable.Repeat((byte)pad, pad)).ToArray();

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CFB;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            aes.IV = iv;

            using var enc = aes.CreateEncryptor();
            return enc.TransformFinalBlock(padded, 0, padded.Length);
        }

        public static byte[] AESCFBDecrypt(byte[] key, byte[] iv, byte[] ct)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CFB;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            aes.IV = iv;

            using var dec = aes.CreateDecryptor();
            var d = dec.TransformFinalBlock(ct, 0, ct.Length);

            int pad = d[d.Length - 1];
            if (pad > 0 && pad <= 16)
            {
                bool valid = d.Skip(d.Length - pad).All(x => x == pad);
                if (valid) return d.Take(d.Length - pad).ToArray();
            }
            return d;
        }
    }
}