using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using CsvHelper;

namespace DiscordBotExample
{
    class Program
    {
        private static List<string> _imageUrls;
        private static Random _random = new Random();
        private static DiscordSocketClient _client;
        private static ulong _channelId;
        private static string _csvFilePath;
        private static TimeSpan _postTimeSpain;
        private static TimeZoneInfo _spainTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        private static bool _isImageUrlsLoaded = false; // Flag to track if image URLs are loaded

        static async Task Main(string[] args)
        {
            // Read environment variables
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
            var channelIdStr = Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");
            _csvFilePath = Environment.GetEnvironmentVariable("CSV_FILE_PATH");
            var postTimeStr = Environment.GetEnvironmentVariable("POST_TIME");

            // Check if token, channelId, csvFilePath, or postTime is null or empty
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(channelIdStr) || string.IsNullOrEmpty(_csvFilePath) || string.IsNullOrEmpty(postTimeStr))
            {
                Console.WriteLine("Environment variables are not set correctly.");
                return;
            }

            // Parse channel ID
            if (!ulong.TryParse(channelIdStr, out _channelId))
            {
                Console.WriteLine("Invalid DISCORD_CHANNEL_ID format.");
                return;
            }

            // Parse post time
            if (!TimeSpan.TryParse(postTimeStr, out _postTimeSpain))
            {
                Console.WriteLine("Invalid POST_TIME format. It must be in the format HH:mm:ss.");
                return;
            }

            // Initialize the Discord client
            _client = new DiscordSocketClient();
            _client.Log += Log;
            _client.Ready += OnReady;
            _client.InteractionCreated += HandleInteractionAsync;

            // Start the bot
            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            // Block the application until it is closed
            await Task.Delay(-1);
        }

        private static Task Log(LogMessage log)
        {
            Console.WriteLine(log);
            return Task.CompletedTask;
        }

        private static async Task OnReady()
        {
            Console.WriteLine("Bot is connected.");

            // Read and process the CSV file from the local filesystem
            var csvData = await ReadLocalCsvFile();

            if (csvData != null)
            {
                using (var reader = new StringReader(csvData))
                using (var csv = new CsvReader(reader, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)))
                {
                    _imageUrls = csv.GetRecords<YourRecordClass>()
                                    .Where(record => !string.IsNullOrWhiteSpace(record.image_url) && record.has_spoilers != "yes")
                                    .Select(record => record.image_url.Trim())
                                    .ToList();

                    _isImageUrlsLoaded = true; // Set flag to true when URLs are loaded
                }

                Console.WriteLine("Filtered URLs read from CSV:");
                foreach (var url in _imageUrls)
                {
                    Console.WriteLine(url);
                }
            }
            else
            {
                Console.WriteLine("Failed to read the CSV file. Exiting...");
                return;
            }

            // Register commands
            await RegisterCommandsAsync();

            // Schedule the first post
            await ScheduleNextPost();
        }

        private static async Task RegisterCommandsAsync()
        {
            var sendCommand = new SlashCommandBuilder()
                .WithName("send")
                .WithDescription("Send a random image from the list");

            // Replace 'your_guild_id_here' with your actual guild ID
            var guildId = ulong.Parse(Environment.GetEnvironmentVariable("GUILD_ID")); // Example: 123456789012345678
            var guild = _client.GetGuild(guildId);

            await guild.DeleteApplicationCommandsAsync(); // Clear existing commands in the guild
            await _client.Rest.DeleteAllGlobalCommandsAsync(); // Optionally clear global commands
            await guild.CreateApplicationCommandAsync(sendCommand.Build());

            Console.WriteLine("Slash command /send registered for guild");
        }

        private static async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            if (interaction is SocketSlashCommand command)
            {
                if (command.Data.Name == "send")
                {
                    await HandleSendCommandAsync(command);
                }
            }
        }

        private static async Task HandleSendCommandAsync(SocketSlashCommand command)
        {
            if (_isImageUrlsLoaded)
            {
                if (_imageUrls.Count > 0)
                {
                    int index = _random.Next(_imageUrls.Count);
                    string randomUrl = _imageUrls[index];
                    await command.RespondAsync(randomUrl);
                }
                else
                {
                    await command.RespondAsync("No URLs available.");
                }
            }
            else
            {
                await command.RespondAsync("The bot is still loading data. Please try again later.");
            }
        }

        private static async Task ScheduleNextPost()
        {
            var nowUtc = DateTime.UtcNow;
            var spainTime = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _spainTimeZone);
            
            var nextPostTimeSpain = DateTime.SpecifyKind(DateTime.Today.Add(_postTimeSpain), DateTimeKind.Unspecified);

            if (nextPostTimeSpain <= spainTime)
            {
                // If the time has already passed for today, schedule for tomorrow
                nextPostTimeSpain = nextPostTimeSpain.AddDays(1);
            }

            // Convert the unspecified time to Spain time zone and then to UTC
            nextPostTimeSpain = TimeZoneInfo.ConvertTimeToUtc(nextPostTimeSpain, _spainTimeZone);

            // Calculate the delay
            var delay = nextPostTimeSpain - nowUtc;

            Console.WriteLine($"Scheduling next post in {delay.TotalMinutes} minutes.");

            await Task.Delay(delay);

            await PostRandomImageUrl();

            // Schedule the next post
            await ScheduleNextPost();
        }

        private static async Task<string> ReadLocalCsvFile()
        {
            try
            {
                // Check if the file exists
                if (!File.Exists(_csvFilePath))
                {
                    Console.WriteLine("CSV file not found.");
                    return null;
                }

                // Read the CSV file content
                return await File.ReadAllTextAsync(_csvFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while reading the CSV file: {ex.Message}");
                return null;
            }
        }

        private static async Task PostRandomImageUrl()
        {
            var channel = _client.GetChannel(_channelId) as IMessageChannel;

            if (channel != null && _imageUrls.Count > 0)
            {
                int index = _random.Next(_imageUrls.Count);
                string randomUrl = _imageUrls[index];
                await channel.SendMessageAsync(randomUrl);
            }
            else
            {
                Console.WriteLine("Channel not found or no URLs available.");
            }
        }

        public class YourRecordClass
        {
            public string image_url { get; set; }
            public string has_spoilers { get; set; }
        }
    }
}
