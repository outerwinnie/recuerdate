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
using CsvHelper.Configuration;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace DiscordBotExample
{
    class Program
    {
        private static List<string> _imageUrls;
        private static Random _random = new Random();
        private static DiscordSocketClient _client;
        private static ulong _channelId;
        private static string _fileId;
        private static string _credentialsPath;
        private static string _rewardsCsvPath; // For rewards.csv path from Docker environment
        private static TimeSpan _postTimeSpain;
        private static TimeZoneInfo _spainTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        private static bool _isImageUrlsLoaded = false; // Flag to track if image URLs are loaded

        static async Task Main(string[] args)
        {
            // Read environment variables
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
            var channelIdStr = Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");
            _fileId = Environment.GetEnvironmentVariable("GOOGLE_DRIVE_FILE_ID");
            _credentialsPath = Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_PATH");
            _rewardsCsvPath = Environment.GetEnvironmentVariable("REWARDS_CSV_PATH"); // Rewards CSV path from environment
            var postTimeStr = Environment.GetEnvironmentVariable("POST_TIME");

            // Check if token, channelId, fileId, credentialsPath, postTime, or rewardsCsvPath is null or empty
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(channelIdStr) || string.IsNullOrEmpty(_fileId) || string.IsNullOrEmpty(_credentialsPath) || string.IsNullOrEmpty(postTimeStr) || string.IsNullOrEmpty(_rewardsCsvPath))
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

            // Download and process the CSV file from Google Drive
            var csvData = await DownloadCsvFromGoogleDrive();

            if (csvData != null)
            {
                using (var reader = new StringReader(csvData))
                using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)))
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
                Console.WriteLine("Failed to download or read the CSV file. Exiting...");
                return;
            }

            // Register commands
            await RegisterCommandsAsync();

            // Schedule the first post
            await ScheduleNextPost();

            // Start checking rewards.csv after URLs have been loaded
            if (_isImageUrlsLoaded)
            {
                _ = Task.Run(CheckRewardsCsvAsync); // Runs in parallel after URLs are loaded
            }
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
            
            // Specify that nextPostTimeSpain is unspecified in terms of kind because we will convert it to a specific time zone
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

        private static async Task<string> DownloadCsvFromGoogleDrive()
        {
            try
            {
                // Set up Google Drive API service
                var credential = GoogleCredential.FromFile(_credentialsPath)
                    .CreateScoped(DriveService.Scope.DriveReadonly);

                var service = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "DiscordBotExample",
                });

                // Download the file
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
                Console.WriteLine("No URLs available.");
            }
        }

        // New method to check rewards every 5 minutes
        private static async Task CheckRewardsCsvAsync()
        {
            while (true) // Infinite loop to run continuously
            {
                try
                {
                    if (File.Exists(_rewardsCsvPath)) // Check if the CSV file exists
                    {
                        using (var reader = new StreamReader(_rewardsCsvPath))
                        using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)))
                        {
                            var rewards = csv.GetRecords<RewardRecord>().ToList();

                            // Find the reward named "recuerdate"
                            var recuerdateReward = rewards.FirstOrDefault(r => r.RewardName == "recuerdate");

                            if (recuerdateReward != null)
                            {
                                if (int.TryParse(recuerdateReward.Quantity, out int quantity))
                                {
                                    Console.WriteLine($"'recuerdate' reward found with quantity {quantity}. Posting random images...");
                                    
                                    for (int i = 0; i < quantity; i++)
                                    {
                                        await PostRandomImageUrl(); // Post a random image
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine("No 'recuerdate' reward found.");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Rewards CSV file not found at {_rewardsCsvPath}.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading rewards CSV: {ex.Message}");
                }

                // Wait for 5 minutes before checking again
                await Task.Delay(TimeSpan.FromMinutes(5));
            }
        }

        // RewardRecord class to map CSV columns
        public class RewardRecord
        {
            public string RewardName { get; set; }
            public string Quantity { get; set; }
        }

        // Your CSV record class (make sure the fields match your actual CSV file)
        public class YourRecordClass
        {
            public string image_url { get; set; }
            public string has_spoilers { get; set; }
        }
    }
}
