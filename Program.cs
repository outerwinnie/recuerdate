using System.Globalization;
using CsvHelper;
using Discord;
using Discord.WebSocket;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Recuerdense_Bot
{
    public class Program
    {
        private static List<string>? _imageUrls;
        private static List<string>? _memeUrls;
        private static readonly Random Random = new Random();
        private static DiscordSocketClient? _client;
        private static ulong _channelId;
        private static string? _fileId;
        private static string? _credentialsPath;
        private static TimeSpan _postTimeSpain;
        private static readonly TimeZoneInfo SpainTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        private static bool _isImageUrlsLoaded; // Flag to track if image URLs are loaded
        private static bool _isMemeUrlsLoaded; // Flag to track if image URLs are loaded
        
        static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Configure services
            builder.Services.AddSingleton<DiscordSocketClient>();
            builder.Services.AddSingleton<Program>(); // Register the bot
            builder.Services.AddControllers(); // Register controllers for API

            var app = builder.Build();
            app.MapControllers(); // Map API endpoints

            // Start the API
            Console.WriteLine("Starting API...");
            _ = app.RunAsync();
            
            // Start the Discord bot
            var bot = app.Services.GetRequiredService<Program>();
            Console.WriteLine("Starting BOT...");
            await bot.StartBotAsync();
        }
        
        public async Task StartBotAsync()
        {
            // Read environment variables
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
            var channelIdStr = Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");
            _fileId = Environment.GetEnvironmentVariable("GOOGLE_DRIVE_FILE_ID");
            _credentialsPath = Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_PATH");
            var postTimeStr = Environment.GetEnvironmentVariable("POST_TIME");

            // Check if token, channelId, fileId, credentialsPath, or postTime is null or empty
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

        private async Task OnReady()
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
                    
                    _memeUrls = csvReader.GetRecords<YourRecordClass>()
                                    .Where(record => !string.IsNullOrWhiteSpace(record.image_url) && record.channel_name == "memitos-y-animalitos\ud83e\udd21")
                                    .Select(record => record.image_url.Trim())
                                    .ToList();

                    _isMemeUrlsLoaded = true;
                }

                Console.WriteLine("Filtered URLs read from CSV:");
                foreach (var url in _memeUrls)
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
        }

        private static async Task RegisterCommandsAsync()
        {
            var sendCommand = new SlashCommandBuilder()
                .WithName("imagen")
                .WithDescription("Envia una imagen ramdom");

            var sendMemeCommand = new SlashCommandBuilder()
                .WithName("meme")
                .WithDescription("Envia un meme ramdom");

            // Replace 'your_guild_id_here' with your actual guild ID
            var guildId = ulong.Parse(Environment.GetEnvironmentVariable("GUILD_ID") ?? throw new InvalidOperationException()); // Example: 123456789012345678
            if (_client != null)
            {
                var guild = _client.GetGuild(guildId);

                await guild.DeleteApplicationCommandsAsync(); // Clear existing commands in the guild
                await _client.Rest.DeleteAllGlobalCommandsAsync(); // Optionally clear global commands
                await guild.CreateApplicationCommandAsync(sendCommand.Build());
                await guild.CreateApplicationCommandAsync(sendMemeCommand.Build());
            }

            Console.WriteLine("Slash command /imagen and /meme registered for guild");
        }

        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            if (interaction is SocketSlashCommand command)
            {
                if (command.Data.Name == "imagen")
                {
                    Console.WriteLine("Trying to send image");
                    await SendCommand();
                }
                
                else if (command.Data.Name == "meme")
                {
                    Console.WriteLine("Trying to send meme");
                    await SendMeme();
                }
            }
        }

        private async Task SendCommand()
        {
            if (_isImageUrlsLoaded)
            {
                if (_imageUrls != null && _imageUrls.Count > 0)
                {
                    Random.Next(_imageUrls.Count);
                    Console.WriteLine("Sending image urls");
                    await PostRandomImageUrl();
                }
            }
        }

        private async Task SendMeme()
        {
            if (_isMemeUrlsLoaded)
            {
                if (_memeUrls != null && _memeUrls.Count > 0)
                {
                    Random.Next(_memeUrls.Count);
                    Console.WriteLine("Sending meme urls");
                    await PostRandomMemeUrl();
                }
            }
        }

        private async Task ScheduleNextPost()
        {
            var nowUtc = DateTime.UtcNow;
            var spainTime = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, SpainTimeZone);
            
            // Specify that nextPostTimeSpain is unspecified in terms of kind because we will convert it to a specific time zone
            var nextPostTimeSpain = DateTime.SpecifyKind(DateTime.Today.Add(_postTimeSpain), DateTimeKind.Unspecified);

            if (nextPostTimeSpain <= spainTime)
            {
                // If the time has already passed for today, schedule for tomorrow
                nextPostTimeSpain = nextPostTimeSpain.AddDays(1);
            }

            // Convert the unspecified time to Spain time zone and then to UTC
            nextPostTimeSpain = TimeZoneInfo.ConvertTimeToUtc(nextPostTimeSpain, SpainTimeZone);

            // Calculate the delay
            var delay = nextPostTimeSpain - nowUtc;

            Console.WriteLine($"Scheduling next post in {delay.TotalMinutes} minutes.");

            await Task.Delay(delay);

            await PostRandomImageUrl();

            // Schedule the next post
            await ScheduleNextPost();
        }

        private static async Task<string?> DownloadCsvFromGoogleDrive()
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

        public async Task PostRandomImageUrl()
        {
            if (_client != null)
            {
                var channel = _client.GetChannel(_channelId) as IMessageChannel;

                if (channel != null && _imageUrls != null && _imageUrls.Count > 0)
                {
                    int index = Random.Next(_imageUrls.Count);
                    string randomUrl = _imageUrls[index];
                    await channel.SendMessageAsync(randomUrl);
                }
                else
                {
                    Console.WriteLine("No URLs available.");
                }
            }
        }
        
        public async Task PostRandomMemeUrl()
        {
            if (_client != null)
            {
                var channel = _client.GetChannel(_channelId) as IMessageChannel;

                if (channel != null && _memeUrls != null && _memeUrls.Count > 0)
                {
                    int index = Random.Next(_memeUrls.Count);
                    string randomUrl = _memeUrls[index];
                    await channel.SendMessageAsync(randomUrl);
                }
                else
                {
                    Console.WriteLine("No URLs available.");
                }
            }
        }

        public class YourRecordClass
        {
            public string image_url { get; set; }
            public string has_spoilers { get; set; }
            public string channel_name { get; set; }
        }
    }
}

