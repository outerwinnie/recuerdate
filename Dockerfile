# Use the .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /app

# Copy the project files and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy the rest of the files and build the project
COPY . ./
RUN dotnet publish -c Release -o out

# Use the .NET Runtime image to run the application
FROM mcr.microsoft.com/dotnet/runtime:7.0
WORKDIR /app
COPY --from=build /app/out ./

# Set environment variables
ENV DISCORD_BOT_TOKEN=""
ENV DISCORD_CHANNEL_ID=""
ENV CSV_FILE_PATH="/app/data/data.csv"
ENV POST_TIME="20:00:00"
ENV GUILD_ID=""

# Entry point for the application
ENTRYPOINT ["dotnet", "Recuerdense-Bot.dll"]
