using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using CsvHelper;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace DiscordBotExample
{
    class Program
    {
        private static List<string> _imageUrls = new List<string>();
        private static Random _random = new Random();
        private static DiscordSocketClient _client;
        private static ulong _channelId;
        private static string _fileId;
        private static string _credentialsPath;
        private static TimeSpan _postTimeSpain;
        private static TimeZoneInfo _spainTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        private static bool _isImageUrlsLoaded = false;

        static async Task Main(string[] args)
        {
            // Read environment variables
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
            var channelIdStr = Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");
            _fileId = Environment.GetEnvironmentVariable("GOOGLE_DRIVE_FILE_ID");
            _credentialsPath = Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_PATH");
            var postTimeStr = Environment.GetEnvironmentVariable("POST_TIME");

            // Validate environment variables
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(channelIdStr) || string.IsNullOrEmpty(_fileId) || string.IsNullOrEmpty(_credentialsPath) || string.IsNullOrEmpty(postTimeStr))
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

            var guildId = ulong.Parse(Environment.GetEnvironmentVariable("GUILD_ID")); // Example: 123456789012345678
            var guild = _client.GetGuild(guildId);

            await guild.DeleteApplicationCommandsAsync(); // Clear existing commands in the guild
            await _client.Rest.DeleteAllGlobalCommandsAsync(); // Optionally clear global commands
            await guild.CreateApplicationCommandAsync(sendCommand.Build());

            Console.WriteLine("Slash command /send registered for guild");
        }

        private static async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            if (interaction is SocketSlashCommand command && command.Data.Name == "send")
            {
                await HandleSendCommandAsync(command);
            }
        }

        private static async Task HandleSendCommandAsync(SocketSlashCommand command)
        {
            if (!_isImageUrlsLoaded)
            {
                await command.RespondAsync("The bot is still loading data. Please try again later.");
                return;
            }

            if (_imageUrls.Count > 0)
            {
                string randomUrl = GetRandomImageUrl();
                await command.RespondAsync(randomUrl);
            }
            else
            {
                await command.RespondAsync("No URLs available.");
            }
        }

        private static string GetRandomImageUrl()
        {
            lock (_random) // Ensure thread safety if accessing from multiple threads
            {
                return _imageUrls[_random.Next(_imageUrls.Count)];
            }
        }

        private static async Task ScheduleNextPost()
        {
            var nowUtc = DateTime.UtcNow;
            var spainTime = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _spainTimeZone);
            
            var nextPostTimeSpain = DateTime.SpecifyKind(DateTime.Today.Add(_postTimeSpain), DateTimeKind.Unspecified);

            if (nextPostTimeSpain <= spainTime)
            {
                nextPostTimeSpain = nextPostTimeSpain.AddDays(1);
            }

            nextPostTimeSpain = TimeZoneInfo.ConvertTimeToUtc(nextPostTimeSpain, _spainTimeZone);
            var delay = nextPostTimeSpain - nowUtc;

            Console.WriteLine($"Scheduling next post in {delay.TotalMinutes} minutes.");

            await Task.Delay(delay);

            await PostRandomImageUrl();
            await ScheduleNextPost();
        }

        private static async Task PostRandomImageUrl()
        {
            var channel = _client.GetChannel(_channelId) as IMessageChannel;

            if (channel != null)
            {
                if (!_isImageUrlsLoaded)
                {
                    await channel.SendMessageAsync("The bot is still loading data. Please try again later.");
                    return;
                }

                if (_imageUrls.Count > 0)
                {
                    string randomUrl = GetRandomImageUrl();
                    await channel.SendMessageAsync(randomUrl);
                }
                else
                {
                    await channel.SendMessageAsync("No URLs available.");
                }
            }
        }

        private static async Task LoadImageUrls()
        {
            var csvData = await DownloadCsvFromGoogleDrive();

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
                Console.WriteLine("Failed to download or read the CSV file.");
            }
        }

        private static async Task<string> DownloadCsvFromGoogleDrive()
        {
            try
            {
                var credential = GoogleCredential.FromFile(_credentialsPath)
                    .CreateScoped(DriveService.Scope.DriveReadonly);

                var service = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "DiscordBotExample",
                });

                var request = service.Files.Get(_fileId);
                var stream = new MemoryStream();
                request.MediaDownloader.ProgressChanged += progress =>
                {
                    if (progress.Status == Google.Apis.Download.DownloadStatus.Completed)
                    {
                        Console.WriteLine("Download complete.");
                    }
                };

                await request.DownloadAsync(stream);

                stream.Position = 0;
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                return null;
            }
        }

        public class YourRecordClass
        {
            public string image_url { get; set; }
            public string has_spoilers { get; set; }
        }
    }
}
