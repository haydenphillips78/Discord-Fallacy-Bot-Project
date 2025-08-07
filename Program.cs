using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using Betalgo.Ranul.OpenAI;
using Betalgo.Ranul.OpenAI.Managers;
using Betalgo.Ranul.OpenAI.Extensions;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Betalgo.Ranul.OpenAI.ObjectModels.ResponseModels;

class ApiConfig
{
    public string DiscordToken { get; set; } = "";
    public string OpenAIKey { get; set; } = "";
}


class Program
{
    private static DiscordClient? discord;
    private static SlashCommandsExtension? slash;
    private static OpenAIService? openAi;
    static readonly string configFile = "config.txt";
static ApiConfig apiConfig = new ApiConfig();

static async Task<bool> LoadApiConfigAsync()
    {
        try
        {
            if (!File.Exists(configFile))
            {
                string template =
    @"OpenAI API key: your_openai_key_here
    Discord token: your_discord_token_here
    ";
                await File.WriteAllTextAsync(configFile, template);
                Console.WriteLine($"👋 Please edit the file '{configFile}' and insert your tokens.");
                return false;
            }

            var lines = await File.ReadAllLinesAsync(configFile);
            foreach (var line in lines)
            {
                if (line.StartsWith("OpenAI API key:", StringComparison.OrdinalIgnoreCase))
                    apiConfig.OpenAIKey = line.Substring("OpenAI API key:".Length).Trim();
                else if (line.StartsWith("Discord token:", StringComparison.OrdinalIgnoreCase))
                    apiConfig.DiscordToken = line.Substring("Discord token:".Length).Trim();
            }

            if (string.IsNullOrWhiteSpace(apiConfig.DiscordToken) || string.IsNullOrWhiteSpace(apiConfig.OpenAIKey))
            {
                Console.WriteLine($"❗️ config.txt is missing values. Please fill both Discord token and OpenAI API key.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to load config: {ex.Message}");
            return false;
        }
    }


    class Settings
    {
        public List<ulong> Channels { get; set; } = new List<ulong>();
        public string Mode { get; set; } = "all"; // "all" or "reaction"
    }

    static Settings settings = new Settings();
    static readonly string settingsFile = "settings.json";
    static string systemPrompt = "";

    static async Task Main(string[] args)
    {
        // Load prompt.txt
        if (!File.Exists("prompt.txt"))
        {
            Console.WriteLine("prompt.txt not found! Please create it.");
            return;
        }
        systemPrompt = await File.ReadAllTextAsync("prompt.txt");

        // Load settings.json or create default
        if (File.Exists(settingsFile))
        {
            try
            {
                string json = await File.ReadAllTextAsync(settingsFile);
                settings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
            catch
            {
                settings = new Settings();
            }
        }
        else
        {
            await SaveSettingsAsync();
        }

        // Read environment variables
        if (!await LoadApiConfigAsync())
            return;

        var DiscordToken = apiConfig.DiscordToken;
        var OpenAIKey = apiConfig.OpenAIKey;

        if (string.IsNullOrEmpty(DiscordToken) || string.IsNullOrEmpty(OpenAIKey))
        {
            Console.WriteLine("set ur fockin api keys lad (they r not working aye blud)");
            return;
        }

        // Setup Discord client
        discord = new DiscordClient(new DiscordConfiguration()
        {
            Token = DiscordToken,
            TokenType = TokenType.Bot,
            Intents = DiscordIntents.Guilds | DiscordIntents.GuildMessages | DiscordIntents.MessageContents | DiscordIntents.GuildMessageReactions,
        });
        Console.WriteLine("✅ Discord bot online!");

        slash = discord.UseSlashCommands();
        slash.RegisterCommands<BotCommands>();

        // Initialize OpenAI client
        try 
        {
            openAi = new OpenAIService(new OpenAIOptions()
            {
                ApiKey = OpenAIKey
            });
            Console.WriteLine("✅ OpenAI connection successful!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to initialize OpenAI client: {ex.Message}");
            return;
        }


        discord.MessageCreated += async (s, e) =>
        {
            if (e.Author.IsBot) return;

            if (settings.Mode == "all" && settings.Channels.Contains(e.Channel.Id))
            {
                await HandleFallacyCheck(e.Message);
            }
        };

        discord.MessageReactionAdded += async (s, e) =>
        {
            if (e.User.IsBot) return;
            if (e.Emoji.GetDiscordName() != "🔍") return;

            if (settings.Mode == "reaction" && settings.Channels.Contains(e.Channel.Id))
            {
                var message = e.Message;
                if (message != null)
                {
                    await HandleFallacyCheck(e.Message);
                }
            }
        };

        await discord.ConnectAsync();
        Console.WriteLine("Bot connected! Press Ctrl+C to exit.");

        await Task.Delay(-1); // Keep running
    }

static async Task HandleFallacyCheck(DiscordMessage message)
{
    if (openAi == null || discord == null) return;

    try
    {
       var chatRequest = new ChatCompletionCreateRequest
        {
            Model = "gpt-3.5-turbo",
            Messages = new List<ChatMessage>
            {
                ChatMessage.FromSystem(systemPrompt),
                ChatMessage.FromUser(message.Content)
            }
        };


        var chatResponse = await openAi.ChatCompletion.CreateCompletion(chatRequest);
        if (chatResponse == null || chatResponse.Choices.Count == 0)
        {
            await message.CreateReactionAsync(DiscordEmoji.FromName(discord, ":warning:"));
            return;                                                 
        }                                                                                                                   
        string reply = chatResponse.Choices.FirstOrDefault()?.Message.Content ?? "";

        if (!string.IsNullOrEmpty(reply) &&
            !reply.ToLower().Contains("no fallacy") &&
            !reply.ToLower().Contains("none detected"))
        {
            await message.RespondAsync(reply);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error in HandleFallacyCheck: {ex.Message}");
        await message.CreateReactionAsync(DiscordEmoji.FromName(discord, ":warning:"));
    }
}

    static async Task SaveSettingsAsync()
            {
                try
                {
                    string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(settingsFile, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save settings: {ex.Message}");
                }
            }

    // Slash commands implementation
    public class BotCommands : ApplicationCommandModule
    {
        [SlashCommand("setmode", "Set mode to all or reaction")]
        public async Task SetMode(InteractionContext ctx,
            [Option("mode", "all or reaction")] string mode)
        {
            mode = mode.ToLower();
            if (mode != "all" && mode != "reaction")
            {
                await ctx.CreateResponseAsync(new DiscordInteractionResponseBuilder()
                    .WithContent("❌ Invalid mode. Use 'all' or 'reaction'.")
                    .AsEphemeral(true));
                return;
            }

            settings.Mode = mode;
            await SaveSettingsAsync();

            await ctx.CreateResponseAsync($"✅ Mode set to **{mode}**.");
        }

        [SlashCommand("addchannel", "Add current channel to fallacy detection list")]
        public async Task AddChannel(InteractionContext ctx)
        {
            if (!settings.Channels.Contains(ctx.Channel.Id))
            {
                settings.Channels.Add(ctx.Channel.Id);
                await SaveSettingsAsync();
                await ctx.CreateResponseAsync("✅ This channel is now monitored for fallacies.");
            }
            else
            {
                await ctx.CreateResponseAsync("ℹ️ This channel is already being monitored.");
            }
        }

        [SlashCommand("removechannel", "Remove current channel from fallacy detection list")]
        public async Task RemoveChannel(InteractionContext ctx)
        {
            if (settings.Channels.Contains(ctx.Channel.Id))
            {
                settings.Channels.Remove(ctx.Channel.Id);
                await SaveSettingsAsync();
                await ctx.CreateResponseAsync("✅ Channel removed from monitoring list.");
            }
            else
            {
                await ctx.CreateResponseAsync("ℹ️ This channel is not being monitored.");
            }
        }

        [SlashCommand("listchannels", "List all active fallacy detection channels")]
        public async Task ListChannels(InteractionContext ctx)
        {
            if (settings.Channels.Count == 0)
            {
                await ctx.CreateResponseAsync("📭 No channels currently being monitored.");
            }
            else
            {
                string list = string.Join("\n", settings.Channels.ConvertAll(id => $"<#{id}>"));
                await ctx.CreateResponseAsync($"🧾 Currently monitoring {settings.Channels.Count} channel(s):\n{list}");
            }
        }
    }
}
