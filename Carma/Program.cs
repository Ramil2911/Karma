using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using System.Text;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carma
{
    class Program
    {
        private CommandService _commands;
        private DiscordSocketClient _client;
        private IServiceProvider _services;
        private ApplicationContext db;
        private Random rand = new Random();

        static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult(); //use TAP

        private async Task MainAsync()
        {
            db = new ApplicationContext();
            _client = new DiscordSocketClient();
            _commands = new CommandService();
            _services = BuildServiceProvider();
            await InstallCommandsAsync();

            Console.OutputEncoding = Encoding.Unicode;


            _client.Log += Log;
            await _client.LoginAsync(TokenType.Bot,
                "Njk3ODU5MzY5ODg2Mjg1OTI3.XpAYkw.OcQJB4VBfjVteMF3444UqZJTB6I"); //auth


            await _client.StartAsync(); //start
            _client.UserJoined += UserJoinedHandler;
            _client.UserLeft += UserLeavedHandler;
            _client.ReactionAdded += ReactionAddedHandler;
            _client.ReactionRemoved += ReactionRemovedHandler; //add handers to events

            await Task.Delay(-1); //make it unstoppable!
        }

        private IServiceProvider BuildServiceProvider() => new ServiceCollection()
            .AddSingleton(_client)
            .AddSingleton(_commands)
            .AddSingleton<ApplicationContext>(db)
            .BuildServiceProvider();

        private async Task InstallCommandsAsync()
        {
            // Hook the MessageReceived event into our command handler
            _client.MessageReceived += HandleCommandAsync;

            _commands.AddTypeReader(typeof(List<IUser>), new MentionedUsersReader());
            _commands.AddTypeReader(typeof(string), new TextReader());

            // Here we discover all of the command modules in the entry 
            // assembly and load them. Starting from Discord.NET 2.0, a
            // service provider is required to be passed into the
            // module registration method to inject the 
            // required dependencies.
            //
            // If you do not use Dependency Injection, pass null.
            // See Dependency Injection guide for more information.
            _commands.CommandExecuted += PostProcess;
            _commands.Log += Log;
            await _commands.AddModulesAsync(assembly: Assembly.GetEntryAssembly(),
                services: _services);
        }

        private async Task HandleCommandAsync(SocketMessage messageParam)
        {
            // Don't process the command if it was a system message
            var message = messageParam as SocketUserMessage;
            if (message == null) return;

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;

            // Determine if the message is a command based on the prefix and make sure no bots trigger commands
            if (!(message.HasStringPrefix("k.", ref argPos) ||
                  message.HasMentionPrefix(_client.CurrentUser, ref argPos)) ||
                message.Author.IsBot)
            {
                if (!message.Author.IsBot)
                {
                    await message.AddReactionAsync(new Emoji("⬆️"));
                    await message.AddReactionAsync(new Emoji("⬇️"));
                }
                return;
            }


            // Create a WebSocket-based command context based on the message
            var context = new SocketCommandContext(_client, message);

            // Execute the command with the command context we just
            // created, along with the service provider for precondition checks.
            await _commands.ExecuteAsync(
                context: context,
                argPos: argPos,
                services: _services);
            
            
        }

        private async Task PostProcess(Optional<CommandInfo> command, ICommandContext context, IResult result)
        {
            // We have access to the information of the command executed,
            // the context of the command, and the result returned from the
            // execution in this event.

            // We can tell the user what went wrong
            if (!string.IsNullOrEmpty(result?.ErrorReason))
            {
                var embed = new EmbedBuilder();
                embed.WithTitle("Error")
                    .WithColor(Color.Red)
                    .WithDescription(result.ErrorReason);
                await context.Channel.SendMessageAsync("", false, embed.Build());
            }

            // ...or even log the result (the method used should fit into
            // your existing log handler)
            var commandName = command.IsSpecified ? command.Value.Name : "A command";
            await Log(new LogMessage(LogSeverity.Info, 
                "CommandExecution", 
                $"{commandName} was executed at {DateTime.UtcNow}."));
        }

        private async Task UserJoinedHandler(SocketGuildUser user)
        {
            await Task.Run(() =>
            {
                if (!user.IsBot & db.Persons.Any(x => x.Id == user.Id))
                {
                    db.Persons.Add(new Person() {karma = 0, Id = user.Id});
                    db.SaveChanges();
                }
            });
        }

        private async Task UserLeavedHandler(SocketGuildUser user)
        {
            await Task.Run(() =>
            {
                if (!user.IsBot & db.Persons.Any(x => x.Id == user.Id))
                {
                    db.Persons.Remove(new Person() {Id = user.Id});
                    db.SaveChanges();
                }
            });
        }

        private Task Log(LogMessage message)
        {
            Console.WriteLine(message.ToString());
            return Task.CompletedTask;
        }

        private async Task ReactionAddedHandler(Cacheable<IUserMessage, ulong> before, ISocketMessageChannel after,
            SocketReaction reaction)
        {
            await Task.Run(() =>
            {
                if (_client.GetUser(reaction.UserId).IsBot) return;
                if ((reaction.Emote.Name == "⬆️" || reaction.Emote.Name == ":RankUp:" ||
                     reaction.Emote.Name == ":arrow_up:") & reaction.UserId != reaction.Channel
                    .GetMessageAsync(reaction.MessageId, CacheMode.AllowDownload, RequestOptions.Default).Result
                    .Author.Id)
                {
                    Console.WriteLine("Adding Karma!");
                    if (db.Persons.Any(x =>
                        x.Id == Convert.ToUInt64(reaction.Channel
                            .GetMessageAsync(reaction.MessageId, CacheMode.AllowDownload, RequestOptions.Default).Result
                            .Author.Id)))
                    {
                        db.Persons.First(x =>
                            x.Id == Convert.ToUInt64(reaction.Channel.GetMessageAsync(reaction.MessageId,
                                CacheMode.AllowDownload, RequestOptions.Default).Result.Author.Id)).karma++;
                        db.SaveChanges();
                    }
                    else
                    {
                        Console.WriteLine("Creating db data!");
                        db.Persons.Add(new Person()
                        {
                            karma = 0,
                            Id = Convert.ToUInt64(reaction.Channel.GetMessageAsync(reaction.MessageId,
                                CacheMode.AllowDownload, RequestOptions.Default).Result.Author.Id)
                        });
                        db.SaveChanges();
                        db.Persons.First(x =>
                            x.Id == Convert.ToUInt64(reaction.Channel.GetMessageAsync(reaction.MessageId,
                                CacheMode.AllowDownload, RequestOptions.Default).Result.Author.Id)).karma++;
                        db.SaveChanges();
                    }
                }
            });
        }

        private async Task ReactionRemovedHandler(Cacheable<IUserMessage, ulong> before, ISocketMessageChannel after,
            SocketReaction reaction)
        {
            await Task.Run(() =>
            {
                Console.WriteLine(
                    $"Reaction added!- {reaction.User} at {(reaction.Channel as SocketGuildChannel).Guild.Name} added {reaction.Emote.Name}"); //log this!
                if ((reaction.Emote.Name == "⬇️" | reaction.Emote.Name == ":RankDown:") & reaction.UserId !=
                    reaction.Channel
                        .GetMessageAsync(reaction.MessageId, CacheMode.AllowDownload, RequestOptions.Default).Result
                        .Author.Id)
                {
                    if (db.Persons.Any(x =>
                        x.Id == Convert.ToUInt64(reaction.Channel
                            .GetMessageAsync(reaction.MessageId, CacheMode.AllowDownload, RequestOptions.Default).Result
                            .Author.Id)))
                    {
                        db.Persons.First(x =>
                            x.Id == Convert.ToUInt64(reaction.Channel.GetMessageAsync(reaction.MessageId,
                                CacheMode.AllowDownload, RequestOptions.Default).Result.Author.Id)).karma--;
                        db.SaveChanges();
                    }
                    else
                    {
                        Console.WriteLine("Creating db data!");
                        db.Persons.Add(new Person()
                        {
                            karma = 0,
                            Id = Convert.ToUInt64(reaction.Channel.GetMessageAsync(reaction.MessageId,
                                CacheMode.AllowDownload, RequestOptions.Default).Result.Author.Id)
                        });
                        db.SaveChanges();
                        db.Persons.First(x =>
                            x.Id == Convert.ToUInt64(reaction.Channel.GetMessageAsync(reaction.MessageId,
                                CacheMode.AllowDownload, RequestOptions.Default).Result.Author.Id)).karma--;
                        db.SaveChanges();
                    }
                }
            });
        }
    }

    public class CommandModule : ModuleBase<SocketCommandContext>
    {
        private ApplicationContext db;

        public CommandModule(IServiceProvider services)
        {
            db = services.GetService<ApplicationContext>();
        }

        [Command("sample")]
        public async Task Sample()
        {
            var embed = new EmbedBuilder();
            embed.WithTitle("Карма")
                .WithColor(Color.Red)
                .WithDescription("Ты не должен был видеть это");
            await ReplyAsync("", false, embed.Build());
        }

        [Command("karma")]
        public async Task Karma(List<IUser> users)
        {
            Console.WriteLine("Getting karma;");
            foreach (IUser user in users)
            {

                if (user.Id == Context.Client.CurrentUser.Id) continue;
                if (db.Persons.Any(x => x.Id == user.Id))
                {
                    var embed = new EmbedBuilder();
                    embed.WithTitle("Карма")
                        .WithColor(Color.Blue)
                        .WithDescription($"{user.Username}'s karma: {db.Persons.First(x => x.Id == user.Id).karma}");
                    await ReplyAsync("", false, embed.Build());
                }
                else
                {
                    if (!user.IsBot & !db.Persons.Any(x => x.Id == user.Id))
                    {
                        await db.Persons.AddAsync(new Person() {karma = 0, Id = user.Id});
                        await db.SaveChangesAsync();
                    }

                    var embed = new EmbedBuilder();
                    embed.WithTitle("Карма")
                        .WithColor(Color.Blue)
                        .WithDescription($"{user.Username}'s karma: {db.Persons.First(x => x.Id == user.Id).karma}");
                    await ReplyAsync("", false, embed.Build());
                }
            }
        }


        [Command("help", RunMode = RunMode.Async)]
        public async Task Help()
        {
            var embed = new EmbedBuilder();
            embed.WithTitle("Помощь")
                .WithAuthor(Context.Client.CurrentUser)
                .WithColor(Color.Blue)
                .WithDescription("k. - standard call of Karma-kun," +
                                 "\nk.help - call for help!" +
                                 "\nk.karma @somebody1 @somebody2 @somebodyN - get karma of person" +
                                 "\nk.lottery - run a lottery! If your karma<0, you can make it karma/2, karma*2 or karma*3! If it is >0, you can lose everything, make it karma*-1 or karma*2!" +
                                 "\nk.duel @mention - start a duel with @mention!" +
                                 "\nk.q - quote!");
            await ReplyAsync("", false, embed.Build());
        }

        [Command("lottery")]
        public async Task Lottery()
        {
            var embed = new EmbedBuilder();
            embed.WithTitle("Лотерея")
                .WithColor(Color.Blue);
            var rand = new Random();
            if (db.Persons.First(x => x.Id == Context.User.Id).karma == 0)
            {
                embed.WithDescription("Вы не можете учатсвовать в лотерее, так как ваша карма равна 0!");
                await ReplyAsync("", false, embed.Build());
                return;
            }

            switch (rand.Next(0, 10))
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                    if (db.Persons.First(x => x.Id == Context.User.Id).karma < 0)
                    {
                        embed.WithDescription("You lose! Karma * 3!");
                        await ReplyAsync("", false, embed.Build());
                        db.Persons.First(x => x.Id == Context.User.Id).karma *= 3;
                        await db.SaveChangesAsync();
                    }
                    else
                    {
                        embed.WithDescription("You lose! Karma * 0.75!");
                        await ReplyAsync("", false, embed.Build());
                        db.Persons.First(x => x.Id == Context.User.Id).karma =
                            Convert.ToInt32(db.Persons.First(x => x.Id == Context.User.Id).karma * 0.75f);
                        await db.SaveChangesAsync();
                    }

                    break;
                case 6:
                case 7:
                case 8:
                    if (db.Persons.First(x => x.Id == Context.User.Id).karma < 0)
                    {
                        embed.WithDescription("You lose! Karma * 2!");
                        await ReplyAsync("", false, embed.Build());
                        db.Persons.First(x => x.Id == Context.User.Id).karma *= 2;
                        await db.SaveChangesAsync();
                    }
                    else
                    {
                        embed.WithDescription("You lose! Karma *0.5!");
                        await ReplyAsync("", false, embed.Build());
                        db.Persons.First(x => x.Id == Context.User.Id).karma =
                            Convert.ToInt32(db.Persons.First(x => x.Id == Context.User.Id).karma * -0.5f);
                        await db.SaveChangesAsync();
                    }

                    break;
                case 9:
                case 10:
                    if (db.Persons.First(x => x.Id == Context.User.Id).karma < 0)
                    {
                        embed.WithDescription("You won! Karma = 0!");
                        await ReplyAsync("", false, embed.Build());
                        db.Persons.First(x => x.Id == Context.User.Id).karma = 0;
                        await db.SaveChangesAsync();
                    }
                    else if (db.Persons.First(x => x.Id == Context.User.Id).karma == 0)
                        await ReplyAsync("Oh! Your carma is 0, you can't run lottery.");
                    else
                    {
                        embed.WithDescription("You won! Karma*2!");
                        await ReplyAsync("", false, embed.Build());
                        db.Persons.First(x => x.Id == Context.User.Id).karma *= 2;
                        await db.SaveChangesAsync();
                    }

                    break;
            }
        }

        [Command("q", RunMode = RunMode.Async)]
        public async Task Quote(params string[] args)
        {
            var text = Context.Message.Content.Split(" ", StringSplitOptions.RemoveEmptyEntries);
            ulong quotMessageId;
            try
            {
                quotMessageId = UInt64.Parse(text[1]);
            }
            catch (Exception e)
            {
                var embed = new EmbedBuilder();
                embed.WithTitle("Error")
                    .WithColor(Color.Red)
                    .WithAuthor(Context.Client.CurrentUser)
                    .WithDescription("Cannot get message by ID!");
                await ReplyAsync("", false, embed.Build());
                return;
            }

            IMessage requestedMessage;
            try
            {
                requestedMessage = await Context.Message.Channel.GetMessageAsync(quotMessageId, CacheMode.AllowDownload,
                    RequestOptions.Default);
            }
            catch (Exception e)
            {
                var embed = new EmbedBuilder();
                embed.WithTitle("Error")
                    .WithColor(Color.Red)
                    .WithAuthor(Context.Client.CurrentUser)
                    .WithDescription("Cannot get message by ID!");
                await ReplyAsync("", false, embed.Build());
                return;
            }

            if (requestedMessage == null)
            {
                var embed = new EmbedBuilder();
                embed.WithTitle("Error")
                    .WithColor(Color.Red)
                    .WithAuthor(Context.Client.CurrentUser)
                    .WithDescription("Cannot get message by ID!");
                await ReplyAsync("", false, embed.Build());
            }

            var embedReply = new EmbedBuilder()
            {
                Title = "Reply"
            };
                            
            var embedOn = new EmbedBuilder()
            {
                Title = "On"
            };

            embedReply.WithColor(Color.Blue)
                .WithTitle("Ответ:")
                .WithAuthor(Context.Message.Author)
                .WithDescription($"\n{Context.Message.Content.Substring(Context.Message.Content.IndexOf(' ', Context.Message.Content.IndexOf(' ') + 1))}");
            embedOn.WithColor(Color.Blue)
                .WithTitle("Сообщение:")
                .WithAuthor(requestedMessage.Author)
                .WithDescription($"\n{requestedMessage.Content}");

            await ReplyAsync("", false, embedOn.Build());
            await ReplyAsync("", false, embedReply.Build());
            await Context.Message.DeleteAsync();
        }

        [Command("setkarma")]
        public async void Setkarma(params string[] args)
        {
            long karma = Int64.Parse(Context.Message.Content.Split(" ")[1]);
            foreach (var user in Context.Message.MentionedUsers)
            {
                db.Persons.First(x => x.Id == user.Id).karma = karma;
            }
        }
    }
}


public class TextReader : TypeReader
    {
        public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
        {
            
            return Task.FromResult(TypeReaderResult.FromSuccess(input));
        }
    }

    public class MentionedUsersReader : TypeReader
    {
        public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
        {
            List<IUser> users = new List<IUser>();
            foreach (var userId in context.Message.MentionedUserIds)
            {
                users.Add(context.Client.GetUserAsync(userId).Result);
            }
                
            return Task.FromResult(TypeReaderResult.FromSuccess(users));
        }
    }

    public class ApplicationContext : DbContext
    {
        public DbSet<Person> Persons { get; set; }

        public ApplicationContext()
        {
            Database.EnsureCreated();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Filename=karmadb.db");
        }
    }

    public class Person
    {
        [Key]
        public ulong Id { get; set; }
        public long karma { get; set; }
    }