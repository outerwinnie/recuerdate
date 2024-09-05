using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using Discord.Rest;
using CsvHelper;
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
        private static TimeSpan _postTimeSpain;
        private static TimeZoneInfo _spainTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        private static bool _isImageUrlsLoaded = false; // Flag to track if image URLs are loaded

        // Path to the local rewards CSV file
        private static string _rewardsCsvPath;

        // Timer for periodic rewards processing
        private static Timer _rewardsTimer;
        private static TimeSpan _rewardsInterval;

        static async Task Main(string[] args)
        {
            // Read environment variables
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
            var channelIdStr = Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");
            _fileId = Environment.GetEnvironmentVariable("GOOGLE_DRIVE_FILE_ID");
            _credentialsPath = Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_PATH");
            var postTimeStr = Environment.GetEnvironmentVariable("POST_TIME");
            _rewardsCsvPath = Environment.GetEnvironmentVariable("REWARDS_CSV_PATH");
            var rewardsIntervalStr = Environment.GetEnvironmentVariable("REWARDS_INTERVAL_MINUTES");

            // Check if token, channelId, fileId, credentialsPath, postTime, rewardsCsvPath, or rewardsInterval is null or empty
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(channelIdStr) || string.IsNullOrEmpty(_fileId) || string.IsNullOrEmpty(_credentialsPath) || string.IsNullOrEmpty(postTimeStr) || string.IsNullOrEmpty(_rewardsCsvPath) || string.IsNullOrEmpty(rewardsIntervalStr))
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

            // Parse rewards interval
            if (!int.TryParse(rewardsIntervalStr, out int rewardsIntervalMinutes))
            {
                Console.WriteLine("Invalid REWARDS_INTERVAL_MINUTES format. It must be an integer.");
                return;
            }

            _rewardsInterval = TimeSpan.FromMinutes(rewardsIntervalMinutes);

            // Initialize the Discord client
            _client = new DiscordSocketClient();
            _client.Log += Log;
            _client.Ready += OnReady;
            _client.InteractionCreated += HandleInteractionAsync;

            // Start the bot
            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            // Set up the timer for rewards processing
            _rewardsTimer = new Timer(async _ =>
            {
                await ProcessRewards();
            }, null, _rewardsInterval, _rewardsInterval);

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
                using (var csvReader = new CsvReader(reader, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)))
                {
                    _imageUrls = csvReader.GetRecords<YourRecordClass>()
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

            // Process rewards initially
            await ProcessRewards();

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

        private static async Task ProcessRewards()
        {
            try
            {
                // Read the rewards CSV from local storage
                var rewardsCsvData = File.ReadAllText(_rewardsCsvPath);

                // Parse the CSV data
                using (var reader = new StringReader(rewardsCsvData))
                using (var csvReader = new CsvReader(reader, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)))
                {
                    var records = csvReader.GetRecords<RewardRecordClass>().ToList();

                    foreach (var record in records)
                    {
                        if (record.RewardName.Equals("recuerdate", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(record.Quantity, out int quantity) && quantity > 0)
                            {
                                Console.WriteLine($"Processing reward '{record.RewardName}' with quantity {quantity}.");
                                for (int i = 0; i < quantity; i++)
                                {
                                    await PostRandomImageUrl();
                                }

                                // Decrease the quantity after sending the images
                                record.Quantity = "0"; // Set quantity to 0 after processing
                            }
                            else
                            {
                                Console.WriteLine($"Invalid Quantity value for record with RewardName '{record.RewardName}'.");
                            }
                        }
                    }

                    // Write the updated rewards data back to the CSV file
                    using (var writer = new StreamWriter(_rewardsCsvPath))
                    using (var csvWriter = new CsvWriter(writer, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)))
                    {
                        csvWriter.WriteRecords(records);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while processing the rewards file: {ex.Message}");
            }
        }

        public class YourRecordClass
        {
            public string image_url { get; set; }
            public string has_spoilers { get; set; }
        }

        public class RewardRecordClass
        {
            public string RewardName { get; set; }
            public string Quantity { get; set; }
        }
    }
}
