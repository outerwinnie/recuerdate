﻿using System;
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
        private static List<string> _imageUrls;
        private static Random _random = new Random();
        private static DiscordSocketClient _client;
        private static System.Timers.Timer _timer;
        private static ulong _channelId;
        private static string _fileId;
        private static string _credentialsPath;
        private static int _postIntervalSeconds;

        static async Task Main(string[] args)
        {
            // Read environment variables
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
            var channelIdStr = Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");
            _fileId = Environment.GetEnvironmentVariable("GOOGLE_DRIVE_FILE_ID");
            _credentialsPath = Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_PATH");
            var intervalStr = Environment.GetEnvironmentVariable("POST_INTERVAL_SECONDS");

            // Check if token, channelId, fileId, credentialsPath, or interval is null or empty
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(channelIdStr) || string.IsNullOrEmpty(_fileId) || string.IsNullOrEmpty(_credentialsPath) || string.IsNullOrEmpty(intervalStr))
            {
                Console.WriteLine("Environment variables are not set correctly.");
                Console.WriteLine($"DISCORD_BOT_TOKEN: {(string.IsNullOrEmpty(token) ? "Not set" : "Set")}");
                Console.WriteLine($"DISCORD_CHANNEL_ID: {(string.IsNullOrEmpty(channelIdStr) ? "Not set" : "Set")}");
                Console.WriteLine($"GOOGLE_DRIVE_FILE_ID: {(string.IsNullOrEmpty(_fileId) ? "Not set" : "Set")}");
                Console.WriteLine($"GOOGLE_CREDENTIALS_PATH: {(string.IsNullOrEmpty(_credentialsPath) ? "Not set" : "Set")}");
                Console.WriteLine($"POST_INTERVAL_SECONDS: {(string.IsNullOrEmpty(intervalStr) ? "Not set" : "Set")}");
                return;
            }

            // Parse channel ID
            if (!ulong.TryParse(channelIdStr, out _channelId))
            {
                Console.WriteLine("Invalid DISCORD_CHANNEL_ID format.");
                return;
            }

            // Parse post interval
            if (!int.TryParse(intervalStr, out _postIntervalSeconds) || _postIntervalSeconds <= 0)
            {
                Console.WriteLine("Invalid POST_INTERVAL_SECONDS format. It must be a positive integer.");
                return;
            }

            // Initialize the Discord client
            _client = new DiscordSocketClient();
            _client.Log += Log;
            _client.Ready += OnReady;

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
                using (var csv = new CsvReader(reader, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)))
                {
                    _imageUrls = csv.GetRecords<YourRecordClass>()
                                    .Where(record => !string.IsNullOrWhiteSpace(record.image_url))
                                    .Select(record => record.image_url.Trim())
                                    .ToList();
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

            // Check if imageUrls is empty
            if (_imageUrls.Count == 0)
            {
                Console.WriteLine("No valid URLs available. Exiting...");
                return;
            }

            // Create and configure the timer
            _timer = new System.Timers.Timer
            {
                Interval = _postIntervalSeconds * 1000, // Convert seconds to milliseconds
                AutoReset = true, // Timer should repeat
                Enabled = true
            };

            _timer.Elapsed += async (sender, e) => await PostRandomImageUrl();

            // Post an initial message
            await PostRandomImageUrl();
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
    }

    // Define a class that matches the CSV structure
    public class YourRecordClass
    {
        public string image_url { get; set; }
    }
}