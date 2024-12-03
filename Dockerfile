# Use the .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy the project files and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy the rest of the files and build the project
COPY . ./
RUN dotnet publish -c Release -o out

# Use the ASP.NET Core Runtime image to run the application
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Copy built files from the build stage
COPY --from=build /app/out ./

# Ensure that necessary files (e.g., credentials, data) can be mounted or included
# Add placeholders for credentials and data files in case they're required during runtime
RUN mkdir -p /app/data

# Set environment variables
ENV DISCORD_BOT_TOKEN=""
ENV DISCORD_CHANNEL_ID=""
ENV GOOGLE_DRIVE_FILE_ID=""
ENV GOOGLE_CREDENTIALS_PATH="/app/credentials.json"
ENV REWARDS_CSV_PATH="/app/data/rewards.csv"
ENV POST_TIME="20:00:00"
ENV REWARDS_INTERVAL_MINUTES="5"

# Expose the default port for the API server
EXPOSE 5000

# Entry point for the application
ENTRYPOINT ["dotnet", "Recuerdense-Bot.dll"]
