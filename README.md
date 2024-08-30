# 🎉 Discord Random Image Bot (Recuerdate)

## 🚀 Overview

The **Discord Random Image Bot** is your go-to tool for spicing up your Discord server! This bot posts random image URLs from a CSV file hosted on Google Drive, directly into your chosen channel at intervals you set. It's fully customizable, Docker-ready, and super easy to use!

### ✨ Features

- 🎲 **Random Image Posting**: Automatically shares a random image URL from a Google Drive CSV file.
- ⏲️ **Customizable Interval**: Set how often the bot should post using an environment variable.
- 🐳 **Dockerized**: Simple to deploy anywhere Docker runs.
- 🔐 **Secure Configuration**: Manage your sensitive data securely with environment variables.

## 📚 Table of Contents

- [✨ Features](#-features)
- [📦 Prerequisites](#-prerequisites)
- [🛠️ Setup](#️-setup)
  - [🔗 Google Drive Setup](#-google-drive-setup)
  - [🤖 Discord Bot Setup](#-discord-bot-setup)
  - [🌍 Environment Variables](#-environment-variables)
- [▶️ Running the Bot](#️-running-the-bot)
  - [🐋 Build the Docker Image](#-build-the-docker-image)
  - [🚀 Run the Docker Container](#-run-the-docker-container)
- [🎨 Customization](#-customization)
- [🐞 Troubleshooting](#-troubleshooting)

## 📦 Prerequisites

Before you get started, make sure you have the following:

- 🐋 [Docker](https://www.docker.com/get-started) installed.
- 🤖 A [Discord bot token](https://discord.com/developers/applications) from the Discord Developer Portal.
- 🌍 A [Google Cloud Project](https://console.cloud.google.com/) with the Google Drive API enabled and your `credentials.json` downloaded.

## 🛠️ Setup

### 🔗 Google Drive Setup

1. **Create a Google Cloud Project**:
   - Head over to the [Google Cloud Console](https://console.cloud.google.com/).
   - Enable the Google Drive API.
   - Generate OAuth 2.0 credentials and download the `credentials.json` file.

2. **Upload Your CSV File**:
   - Upload a CSV file containing image URLs to Google Drive.
   - Share the file with the email linked to your Google Cloud project.

3. **Get the File ID**:
   - Right-click on your CSV file in Google Drive and select "Get link."
   - Extract the file ID from the URL (e.g., `1A2B3C4D5E6F7G8H9I`).

### 🤖 Discord Bot Setup

1. **Create Your Bot**:
   - Go to the [Discord Developer Portal](https://discord.com/developers/applications).
   - Create a new application and add a bot to it.
   - Copy the bot token for later use.

2. **Invite the Bot to Your Server**:
   - Generate an OAuth2 URL with the bot and the necessary permissions.
   - Use this URL to invite the bot to your Discord server.

### 🌍 Environment Variables

You'll need the following environment variables to configure your bot:

- **`DISCORD_BOT_TOKEN`**: Your Discord bot token.
- **`DISCORD_CHANNEL_ID`**: The ID of the channel where the bot will post images.
- **`GOOGLE_DRIVE_FILE_ID`**: The Google Drive file ID for your CSV.
- **`GOOGLE_CREDENTIALS_PATH`**: Path to your `credentials.json` inside the Docker container (usually `/app/credentials.json`).
- **`POST_INTERVAL_SECONDS`**: The interval (in seconds) between each image post.

## ▶️ Running the Bot

### 🐋 Build the Docker Image

To build the Docker image, navigate to the project directory and run:

```bash
docker build -t discord-bot-example .
```

### 🚀 Run the Docker Container

Run the container with the necessary environment variables:

```bash
docker run -e DISCORD_BOT_TOKEN="YOUR_BOT_TOKEN" \
           -e DISCORD_CHANNEL_ID="YOUR_CHANNEL_ID" \
           -e GOOGLE_DRIVE_FILE_ID="YOUR_FILE_ID" \
           -e GOOGLE_CREDENTIALS_PATH="/app/credentials.json" \
           -e POST_INTERVAL_SECONDS="30" \  # Adjust the interval as needed
           discord-bot-example
```

Make sure to replace `"YOUR_BOT_TOKEN"`, `"YOUR_CHANNEL_ID"`, and `"YOUR_FILE_ID"` with your actual values. Adjust the `POST_INTERVAL_SECONDS` variable to control how often the bot posts.

## 🎨 Customization

- **Change the Posting Interval**: Modify the `POST_INTERVAL_SECONDS` environment variable to set how often the bot should post.
- **Update the Image List**: Simply update the CSV file on Google Drive—no need to restart the bot!

## 🐞 Troubleshooting

- **Bot Not Posting?**:
  - Ensure the bot has the correct permissions in the Discord channel.
  - Check the bot’s logs for any error messages.

- **Can't Access Google Drive File?**:
  - Double-check that the file is shared with the correct Google service account.
  - Ensure the file ID is correct.
