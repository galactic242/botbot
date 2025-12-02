using Discord;
using Discord.WebSocket;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;
using System.Diagnostics;

namespace MoonsecDeobfuscator
{
    public static class Program
    {
        private static DiscordSocketClient _client;
        private static ulong TargetChannelId = 1444258745336070164;

        private static DateTime _lastSend = DateTime.MinValue;
        private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(5);

        private static readonly Random _rnd = new();

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
                await _client.SetActivityAsync(new Game("Galactic Deobfuscation Service free"));
            };

            _client.MessageReceived += HandleMessage;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private static string RandStr(int len)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz";
            return new string(Enumerable.Repeat(chars, len)
                .Select(s => s[_rnd.Next(s.Length)]).ToArray());
        }

        private static async Task HandleMessage(SocketMessage message)
        {
            if (message.Author.IsBot)
                return;

            bool inChannel = message.Channel.Id == TargetChannelId;
            bool inDM = message.Channel is SocketDMChannel;

            if (!inChannel && !inDM)
                return;

            if (message.Attachments.Count == 0)
            {
                if (inChannel)
                    await message.DeleteAsync();
                return;
            }

            if (DateTime.UtcNow - _lastSend < Cooldown)
                return;

            _lastSend = DateTime.UtcNow;

            var att = message.Attachments.First();

            var tmpIn = Path.GetTempFileName() + ".lua";

            string luaName = RandStr(8) + ".lua";
            string byteName = RandStr(9) + ".luac";

            var finalLuaPath = Path.Combine(Path.GetTempPath(), luaName);
            var finalBytePath = Path.Combine(Path.GetTempPath(), byteName);

            using (var hc = new HttpClient())
            {
                var data = await hc.GetByteArrayAsync(att.Url);
                await File.WriteAllBytesAsync(tmpIn, data);
            }

            var sw = Stopwatch.StartNew();
            var result = new Deobfuscator().Deobfuscate(File.ReadAllText(tmpIn));
            sw.Stop();

            long nanos = (long)(sw.Elapsed.TotalMilliseconds * 1_000_000);

            var cleaned =
                "-- deobfuscated by galactic services join now https://discord.gg/angmZQJC8a\n\n"
                + result.Source;

            await File.WriteAllTextAsync(finalLuaPath, cleaned);

            using (var fs = new FileStream(finalBytePath, FileMode.Create, FileAccess.Write))
            using (var ser = new Serializer(fs))
                ser.Serialize(result);

            string msgText =
                $"yo deobfuscated in {nanos} nanoseconds\n" +
                $"deobfuscated file name: {luaName}\n" +
                $"bytecode file name: {byteName}\n" +
                $"to view reconstructed source paste bytecode at https://luadec.metaworm.site";

            await message.Channel.SendMessageAsync(msgText);

            using (var fs1 = new FileStream(finalLuaPath, FileMode.Open, FileAccess.Read))
                await message.Channel.SendFileAsync(fs1, luaName);

            using (var fs2 = new FileStream(finalBytePath, FileMode.Open, FileAccess.Read))
                await message.Channel.SendFileAsync(fs2, byteName);
        }
    }
}