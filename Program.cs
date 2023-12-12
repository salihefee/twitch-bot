using twitch_bot;

var salihBot = new TwitchBot("salihefee", Environment.GetEnvironmentVariable("TWITCH_OAUTH")!, "salihefee");
await salihBot.Start();