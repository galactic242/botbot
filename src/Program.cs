using Discord;
using Discord.WebSocket;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;

namespace MoonsecDeobfuscator
{
    public static class Program
    {
        private static DiscordSocketClient _client;
        private static readonly ulong TargetChannelId = 123456789000000000;

        public static async Task Main(string[] args)
        {
            var token = Environment.GetEnvironmentVariable("BOT_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("BOT_TOKEN env missing");
                return;
            }

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All
            });

            _client.Ready += async () =>
            {
                await _client.SetActivityAsync(new Game("Galactic Deobfuscation Service"));
            };

            _client.MessageReceived += HandleMessage;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
            await Task.Delay(-1);
        }

        private static async Task HandleMessage(SocketMessage message)
        {
            if (message.Author.IsBot) return;

            bool inChannel = message.Channel.Id == TargetChannelId;
            bool inDM = message.Channel is SocketDMChannel;
            if (!inChannel && !inDM) return;

            if (message.Attachments.Count == 0) return;

            var att = message.Attachments.First();
            var tempIn = Path.GetTempFileName() + ".lua";

            using (var hc = new HttpClient())
            {
                var bytes = await hc.GetByteArrayAsync(att.Url);
                await File.WriteAllBytesAsync(tempIn, bytes);
            }

            var sw = Stopwatch.StartNew();

            var result = new Deobfuscator().Deobfuscate(File.ReadAllText(tempIn));

            // make random file names
            string luaOut = Rand(8) + ".luau";
            string bcOut = Rand(9) + ".luac";

            // BYTECODE serialize
            using (var fs = new FileStream(bcOut, FileMode.Create))
            using (var ser = new Serializer(fs))
                ser.Serialize(result);

            // upload bytecode to online luadec
            var decompiled = await DecompileOnline(bcOut);

            // strip lua comments
            decompiled = RemoveComments(decompiled);

            // prepend banner
            decompiled =
                "-- deobfuscated by galactic services join now https://discord.gg/angmZQJC8a\n\n" +
                decompiled;

            File.WriteAllText(luaOut, decompiled);

            sw.Stop();
            long nanos = (long)(sw.Elapsed.TotalMilliseconds * 1_000_000);

            await message.Channel.SendMessageAsync(
                $"yo done in {nanos}ns\n" +
                $"deobfuscated file: {luaOut}\n" +
                $"bytecode file: {bcOut}\n" +
                $"paste bytecode at luadec.metaworm.site to view original"
            );

            using (var fs1 = new FileStream(luaOut, FileMode.Open))
                await message.Channel.SendFileAsync(fs1, luaOut);

            using (var fs2 = new FileStream(bcOut, FileMode.Open))
                await message.Channel.SendFileAsync(fs2, bcOut);
        }

        private static async Task<string> DecompileOnline(string path)
        {
            using var hc = new HttpClient();
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(File.ReadAllBytes(path)), "file", Path.GetFileName(path));

            var res = await hc.PostAsync("https://luadec.metaworm.site/decompile", form);
            return await res.Content.ReadAsStringAsync();
        }

        private static string RemoveComments(string src)
        {
            return string.Join("\n",
                src.Split('\n').Where(l => !l.TrimStart().StartsWith("--"))
            );
        }

        private static string Rand(int len)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz";
            return new string(Enumerable.Range(0, len)
                .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
        }
    }
}
