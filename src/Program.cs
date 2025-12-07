using Discord;
using Discord.WebSocket;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

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

                if (msg.Attachments.Count > 0)
                {
                    var att = msg.Attachments.First();
                    if (!(att.Filename.ToLower().EndsWith(".lua") || att.Filename.ToLower().EndsWith(".luau") || att.Filename.ToLower().EndsWith(".txt")))
                    {
                        await msg.Channel.SendMessageAsync($"{msg.Author.Mention} this file type is not allowed.");
                        Busy.Remove(msg.Author.Id);
                        return;
                    }

                    using HttpClient hc = new HttpClient();
                    var bytes = await hc.GetByteArrayAsync(att.Url);
                    sourceCode = System.Text.Encoding.UTF8.GetString(bytes);
                }

                if (sourceCode == null)
                {
                    Busy.Remove(msg.Author.Id);
                    return;
                }

                var statusMsg = await msg.Channel.SendMessageAsync("Turning to bytecode...");

                string bytecodeFile = Guid.NewGuid().ToString().Substring(0, 8) + ".luac";
                string tempFilePath = Path.Combine(Path.GetTempPath(), bytecodeFile);

                try
                {
                    await statusMsg.ModifyAsync(m => m.Content = "Processing...");

                    using (var deob = new Deobfuscator())
                    {
                        var result = deob.Deobfuscate(sourceCode);

                        if (result != null)
                        {
                            if (result is byte[] byteArray)
                            {
                                File.WriteAllBytes(tempFilePath, byteArray);
                            }
                            else
                            {
                                string resultString = result.ToString();
                                File.WriteAllText(tempFilePath, resultString);
                            }
                        }
                        else
                        {
                            File.WriteAllText(tempFilePath, sourceCode);
                        }
                    }

                    await statusMsg.ModifyAsync(m => m.Content = "Bytecode generated successfully");

                    if (File.Exists(tempFilePath) && new FileInfo(tempFilePath).Length > 0)
                    {
                        var embed = new EmbedBuilder()
                            .WithTitle("Luau Bytecode Generated")
                            .WithColor(new Color((uint)new Random().Next(0xFFFFFF)))
                            .WithDescription("Your file has been converted to Luau bytecode.")
                            .WithFooter("Galactic Services")
                            .Build();

                        await using (var fs = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
                        {
                            await msg.Channel.SendFileAsync(fs, bytecodeFile, $"{msg.Author.Mention} here is your bytecode file", embed: embed);
                        }
                    }
                    else
                    {
                        await msg.Channel.SendMessageAsync($"{msg.Author.Mention} failed to generate bytecode file.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex}");
                    await msg.Channel.SendMessageAsync($"{msg.Author.Mention} an error occurred during processing: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempFilePath))
                            File.Delete(tempFilePath);
                    }
                    catch { }
                }

                try
                {
                    await msg.DeleteAsync();
                }
                catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex}");
                await msg.Channel.SendMessageAsync($"{msg.Author.Mention} an error occurred.");
            }
            finally
            {
                if (Busy.ContainsKey(msg.Author.Id))
                    Busy.Remove(msg.Author.Id);
            }
        }
    }
}