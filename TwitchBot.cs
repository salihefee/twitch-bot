using System.Net.Sockets;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace twitch_bot;

public partial class TwitchBot
{
    private const string twitchIp = "irc.chat.twitch.tv";
    private const int twitchPort = 6667;
    private const string osuIp = "irc.ppy.sh";
    private const int osuPort = 6667;

    private readonly string nick;
    private readonly string password;
    private readonly string channel;
    private StreamReader? twitchIncoming;
    private StreamWriter? twitchOutgoing;
    private StreamWriter? osuOutgoing;
    private static readonly HttpClient httpClient = new HttpClient();
    private static readonly string apiKey = Environment.GetEnvironmentVariable("OSU_API_KEY")!;
    private static readonly string osuIrcPass = Environment.GetEnvironmentVariable("OSU_IRC_PASS")!;

    public TwitchBot(string nick, string password, string channel)
    {
        this.nick = nick;
        this.password = password;
        this.channel = channel;
    }

    public async Task Start()
    {
        var twitchTcp = new TcpClient();
        var osuTcp = new TcpClient();
        await twitchTcp.ConnectAsync(twitchIp, twitchPort);
        await osuTcp.ConnectAsync(osuIp, osuPort);
        twitchIncoming = new StreamReader(twitchTcp.GetStream());
        twitchOutgoing = new StreamWriter(twitchTcp.GetStream()) { NewLine = "\r\n", AutoFlush = true };
        osuOutgoing = new StreamWriter(osuTcp.GetStream()) { NewLine = "\r\n", AutoFlush = true };

        // Twitch login
        await twitchOutgoing.WriteLineAsync($"PASS {password}");
        await twitchOutgoing.WriteLineAsync($"NICK {nick}");
        await twitchOutgoing.WriteLineAsync($"JOIN #{channel}");
        await twitchOutgoing.WriteLineAsync($"PRIVMSG #{channel} :Bot started.");
        
        // osu! login
        await osuOutgoing.WriteLineAsync($"PASS {osuIrcPass}");
        await osuOutgoing.WriteLineAsync($"NICK salihefee");
        await osuOutgoing.WriteLineAsync($"PRIVMSG salihefee :Bot started");

        while (true)
        {
            var twitchLine = await twitchIncoming.ReadLineAsync();
            Console.WriteLine(twitchLine);

            var split = twitchLine!.Split(" ");

            if (twitchLine.StartsWith("PING"))
            {
                await twitchOutgoing.WriteLineAsync($"PONG {split[1]}");
                Console.WriteLine($"PONG {split[1]}");
            }

            if (split.Length <= 1 || split[1] != "PRIVMSG") continue;
            var exclamationMarkPos = split[0].IndexOf("!", StringComparison.Ordinal);
            var username = split[0].Substring(1, exclamationMarkPos - 1);

            var secondColonPos = twitchLine.IndexOf(":", 1, StringComparison.Ordinal);
            var message = twitchLine[(secondColonPos + 1)..];

            foreach (var word in message.Split(" "))
            {
                if (!BeatmapRegex().IsMatch(word)) continue;
                var mapId = MapIdRegex().Match(word);

                HttpResponseMessage mapResponse;

                try
                {
                    mapResponse =
                        await httpClient.GetAsync($"https://osu.ppy.sh/api/get_beatmaps?k={apiKey}&b={mapId}");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                    continue;
                }

                var responseContent = mapResponse.Content.ReadAsStringAsync().Result;
                
                if (responseContent == "[]")
                {
                    await twitchOutgoing.WriteLineAsync($"PRIVMSG #{channel} :The specified beatmap was not found.");
                }
                else
                {
                    dynamic responseObjects = JsonConvert.DeserializeObject(await mapResponse.Content.ReadAsStringAsync())!;
                    var responseObject = responseObjects[0];
                    await twitchOutgoing.WriteLineAsync(
                        $"PRIVMSG #{channel} :Received beatmap {responseObject.artist} - {responseObject.title} [{responseObject.version}] from user {username}");
                    await osuOutgoing.WriteLineAsync(
                        $"PRIVMSG salihefee :[{word} {responseObject.artist} - {responseObject.title} [{responseObject.version}]] sent by {username}");
                }
            }
        }
    }

    [GeneratedRegex(@"(https?://osu\.ppy\.sh/(beatmaps|beatmapsets|b)/)[\w\d]+")]
    private static partial Regex BeatmapRegex();

    [GeneratedRegex(@"\d{2,}(?=[^\d]*\d{0,2}[^\d]*$)")]
    private static partial Regex MapIdRegex();
}