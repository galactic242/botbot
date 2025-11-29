using Discord;
using Discord.WebSocket;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;

namespace MoonsecDeobfuscator
{
    public static class Program
    {
        private static DiscordSocketClient _client;
        private static ulong TargetChannelId = 1444258745336070164;

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

            _client.Log += msg =>
            {
                Console.WriteLine(msg.ToString());
                return Task.CompletedTask;
            };

            _client.Ready += async () =>
            {
                await _client.SetStatusAsync(UserStatus.DoNotDisturb);
                await _client.SetActivityAsync(
                    new Game("Galactic Deobfuscation Service • Free", ActivityType.Playing)
                );
            };

            _client.MessageReceived += HandleMessage;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private static async Task HandleMessage(SocketMessage message)
        {
            if (message.Author.IsBot)
                return;

            if (message.Attachments.Count == 0)
                return;

            bool inChannel = message.Channel.Id == TargetChannelId;
            bool inDM = message.Channel is SocketDMChannel;

            if (!inChannel && !inDM)
                return;

            var file = message.Attachments.First();
            var tempInput = Path.GetTempFileName() + ".lua";
            var tempOutput = Path.GetTempFileName() + ".lua";

            using (var client = new HttpClient())
            {
                var data = await client.GetByteArrayAsync(file.Url);
                await File.WriteAllBytesAsync(tempInput, data);
            }

            var result = new Deobfuscator().Deobfuscate(File.ReadAllText(tempInput));
            var disassembled = new Disassembler(result).Disassemble();

            var finalOutput =
                "-- file deobfuscated by galactic services\n\n" +
                disassembled;

            File.WriteAllText(tempOutput, finalOutput);

            await message.Channel.SendMessageAsync("yo file deobfuscated here it is");

            using var fs = new FileStream(tempOutput, FileMode.Open, FileAccess.Read);
            await message.Channel.SendFileAsync(fs, "deobf.lua");
        }
    }
}