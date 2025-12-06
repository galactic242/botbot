using Discord;
using Discord.WebSocket;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.Text.RegularExpressions;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;

namespace GalacticBytecodeBot
{
    public static class Program
    {
        private static DiscordSocketClient _client;
        private static readonly ulong TargetChannel = 1444258745336070164;

        private static readonly Dictionary<ulong, bool> Busy = new Dictionary<ulong, bool>();

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

            _client.Ready += async () =>
            {
                await _client.SetStatusAsync(UserStatus.DoNotDisturb);
                await _client.SetActivityAsync(new Game("Galactic Deobfuscatio"));
            };

            _client.MessageReceived += HandleMessage;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
            await Task.Delay(-1);
        }

        private static async Task HandleMessage(SocketMessage msg)
        {
            if (msg.Author.IsBot) return;

            // -b can be used anywhere
            if (msg.Content.StartsWith("-b") && msg.Attachments.Count > 0)
            {
                await HandleFormat(msg);
                return;
            }

            // auto bytecode only in DM or target channel
            if (msg.Channel.Id != TargetChannel && msg.Channel is not SocketDMChannel)
                return;

            if (Busy.ContainsKey(msg.Author.Id))
            {
                await msg.Channel.SendMessageAsync($"{msg.Author.Mention} please wait, your previous request is still processing.");
                return;
            }

            Busy[msg.Author.Id] = true;

            try
            {
                string sourceCode = null;

                // loadstring("")
                if (msg.Content.Trim().StartsWith("loadstring"))
                {
                    int start = msg.Content.IndexOf("(");
                    int end = msg.Content.LastIndexOf(")");
                    if (start != -1 && end != -1 && end > start)
                    {
                        string inside = msg.Content.Substring(start + 1, end - start - 1).Trim();
                        inside = inside.Trim('"');
                        sourceCode = inside;
                    }
                }

                // github URL
                if (msg.Content.Contains("github.com"))
                {
                    try
                    {
                        string raw = ConvertGithubToRaw(msg.Content.Trim());
                        using HttpClient hc = new HttpClient();
                        sourceCode = await hc.GetStringAsync(raw);
                    }
                    catch { }
                }

                // files
                if (msg.Attachments.Count > 0)
                {
                    var att = msg.Attachments.First();
                    if (!IsAllowed(att.Filename))
                    {
                        await msg.Channel.SendMessageAsync($"{msg.Author.Mention} this file type is not allowed.");
                        return;
                    }

                    using HttpClient hc = new HttpClient();
                    var bytes = await hc.GetByteArrayAsync(att.Url);
                    sourceCode = System.Text.Encoding.UTF8.GetString(bytes);
                }

                if (sourceCode == null)
                    return;

                // turning animation message
                var statusMsg = await msg.Channel.SendMessageAsync("Turning to bytecode.");

                var cts = new CancellationTokenSource();
                var token = cts.Token;

                _ = Task.Run(async () =>
                {
                    string[] frames = { ".", "..", "..." };
                    int i = 0;

                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(1000);
                        if (token.IsCancellationRequested) break;

                        await statusMsg.ModifyAsync(m => m.Content = "Turning to bytecode" + frames[i]);
                        i = (i + 1) % frames.Length;
                    }
                });

                // run MoonSec deobfuscation
                var result = new Deobfuscator().Deobfuscate(sourceCode);

                string bytecodeFile = Rand(10) + ".luac";

                using (var fs = new FileStream(bytecodeFile, FileMode.Create))
                using (var writer = new StreamWriter(fs))
                {
                    writer.WriteLine("-- deobfuscated by galactic deobfuscation join now");
                    writer.Flush();

                    using (var ser = new Serializer(fs))
                        ser.Serialize(result);
                }

                // stop animation
                cts.Cancel();
                await statusMsg.ModifyAsync(m => m.Content = "Bytecode generated.");

                var embed = new EmbedBuilder()
                    .WithTitle("Luau Bytecode Generated")
                    .WithColor(new Color((uint)new Random().Next(0xFFFFFF)))
                    .WithDescription(
                        "Your file has been converted to Luau bytecode.\n\n" +
                        "How to decompile:\n" +
                        "1. Open: https://luadec.metaworm.site\n" +
                        "2. Upload the .luac file\n" +
                        "3. Wait for decompilation\n\n" +
                        "If the site is down, use any offline Luadec build.")
                    .WithFooter("Galactic Services")
                    .Build();

                using (var fs = new FileStream(bytecodeFile, FileMode.Open))
                    await msg.Channel.SendFileAsync(
                        fs,
                        bytecodeFile,
                        $"{msg.Author.Mention} here is your bytecode file",
                        embed: embed
                    );

                await msg.DeleteAsync();
            }
            finally
            {
                if (Busy.ContainsKey(msg.Author.Id))
                    Busy.Remove(msg.Author.Id);
            }
        }

        private static async Task HandleFormat(SocketMessage msg)
        {
            var att = msg.Attachments.First();

            using HttpClient hc = new HttpClient();
            var bytes = await hc.GetByteArrayAsync(att.Url);
            string code = System.Text.Encoding.UTF8.GetString(bytes);

            string formatted = FormatLua(code);

            File.WriteAllText("formatted.lua", formatted);

            var embed = new EmbedBuilder()
                .WithTitle("Lua File Formatted")
                .WithColor(new Color((uint)new Random().Next(0xFFFFFF)))
                .WithDescription("Variables renamed to v1, v2... and functions renamed to mn1, mn2...")
                .Build();

            await msg.Channel.SendFileAsync("formatted.lua", embed: embed);
        }

        private static string FormatLua(string code)
        {
            int v = 1;
            int m = 1;

            code = Regex.Replace(
                code,
                @"local\s+([A-Za-z_][A-Za-z0-9_]*)",
                match => "local v" + (v++)
            );

            code = Regex.Replace(
                code,
                @"function\s+([A-Za-z_][A-Za-z0-9_]*)",
                match => "function mn" + (m++)
            );

            return code;
        }

        private static bool IsAllowed(string file)
        {
            string f = file.ToLower();
            return f.EndsWith(".lua") || f.EndsWith(".luau") || f.EndsWith(".txt");
        }

        private static string ConvertGithubToRaw(string url)
        {
            if (url.Contains("raw.githubusercontent.com"))
                return url;

            url = url.Replace("github.com", "raw.githubusercontent.com");
            url = url.Replace("/blob/", "/");
            return url;
        }

        private static string Rand(int len)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz";
            char[] b = new char[len];
            byte[] d = new byte[1];
            var rng = System.Security.Cryptography.RandomNumberGenerator.Create();

            for (int i = 0; i < len; i++)
            {
                rng.GetBytes(d);
                b[i] = chars[d[0] % chars.Length];
            }

            return new string(b);
        }
    }
}