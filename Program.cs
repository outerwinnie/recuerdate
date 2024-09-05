using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using CsvHelper;

namespace DiscordBotExample
{
    class Program
    {
        private static List<string> _imageUrls;
        private static Random _random = new Random();
        private static DiscordSocketClient _client;
        private static ulong _channelId;
        private static string _credentialsPath;
        private static bool _isImageUrlsLoaded = false;

        static async Task Main(string[] args)
        {
            // Read environment variables
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
            var channelIdStr = Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");
            _credentialsPath = Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_PATH");

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(channelIdStr) || string.IsNullOrEmpty(_credentialsPath))
            {
                Console.WriteLine("Environment variables are not set correctly.");
                return;
            }

            if (!ulong.TryParse(channelIdStr, out _channelId))
            {
                Console.WriteLine("Invalid DISCORD_CHANNEL_ID format.");
                return;
            }

            // Initialize the Discord client
            _client = new DiscordSocketClient();
            _client.Log += Log;
            _client.Ready += OnReady;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            // Start the CSV monitoring task in the background
            _ = MonitorCsvFileAsync();

            // Block the application until closed
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

            // Load initial image URLs (from your Google Drive or wherever necessary)
            _isImageUrlsLoaded = true;

            // Example code to load image URLs (replace with actual logic)
            _imageUrls = new List<string> { "http://example.com/image1.jpg", "http://example.com/image2.jpg" };
        }

        private static async Task MonitorCsvFileAsync()
        {
            while (true)
            {
                try
                {
                    // Read CSV file path from Docker environment variable
                    string csvFilePath = Environment.GetEnvironmentVariable("CSV_FILE_PATH");

                    if (string.IsNullOrEmpty(csvFilePath))
                    {
                        Console.WriteLine("CSV_FILE_PATH environment variable is not set.");
                        return;
                    }

                    if (File.Exists(csvFilePath))
                    {
                        using (var reader = new StreamReader(csvFilePath))
                        using (var csv = new CsvReader(reader, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)))
                        {
                            var records = csv.GetRecords<RewardRecord>().ToList();

                            foreach (var record in records)
                            {
                                // Check if reward is "recuerdate" and quantity is greater than 0
                                if (record.reward == "recuerdate" && record.quantity > 0)
                                {
                                    Console.WriteLine($"Running PostRandomImageUrl {record.quantity} times...");

                                    // Run PostRandomImageUrl as many times as specified by quantity
                                    for (int i = 0; i < record.quantity; i++)
                                    {
                                        await PostRandomImageUrl();
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"CSV file not found at path: {csvFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading CSV file: {ex.Message}");
                }

                // Wait for 5 minutes before checking again
                await Task.Delay(TimeSpan.FromMinutes(5));
            }
        }

        private static async Task PostRandomImageUrl()
        {
            if (_isImageUrlsLoaded && _imageUrls.Count > 0)
            {
                var channel = _client.GetChannel(_channelId) as IMessageChannel;

                if (channel != null)
                {
                    int index = _random.Next(_imageUrls.Count);
                    string randomUrl = _imageUrls[index];
                    await channel.SendMessageAsync(randomUrl);
                }
                else
                {
                    Console.WriteLine("Unable to find the channel.");
                }
            }
            else
            {
                Console.WriteLine("No image URLs loaded.");
            }
        }

        public class RewardRecord
        {
            public string reward { get; set; }
            public int quantity { get; set; }
        }
    }
}
