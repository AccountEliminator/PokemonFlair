using Newtonsoft.Json;
using RedditSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PokemonFlair
{
    class Program
    {
        static Reddit Reddit { get; set; }
        static Subreddit[] Subreddits { get; set; }
        static Configuration Config { get; set; }

        static void Main(string[] args)
        {
            Config = new Configuration();
            if (!File.Exists("config.json"))
            {
                PopulateDummyConfig(Config);
                File.WriteAllText("config.json", JsonConvert.SerializeObject(Config, Formatting.Indented));
                Console.WriteLine("A new configuration file has been generated and saved to config.json.");
                return;
            }
            Config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText("config.json"));
            Console.WriteLine("Logging into Reddit as /u/{0}", Config.Login.Username);
            Reddit = new Reddit();
            try
            {
                Reddit.LogIn(Config.Login.Username, Config.Login.Password);
            }
            catch
            {
                Console.WriteLine("Login failed!");
                return;
            }
            Subreddits = new Subreddit[Config.Subreddits.Length];
            for (int i = 0; i < Subreddits.Length; i++)
                Subreddits[i] = Reddit.GetSubreddit(Config.Subreddits[i].Name);

            var timer = new Timer(DoUpdates, null, 0, 1000 * Config.SecondsBetweenUpdates);

            Console.WriteLine("Press 'q' to exit.");
            ConsoleKeyInfo input;
            do
            {
                input = Console.ReadKey(true);
            } while (input.KeyChar != 'q');
        }

        private static void PopulateDummyConfig(Configuration config)
        {
            config.Subreddits = new[]
            {
                new RegisteredSubreddit
                {
                    Name = "/r/example",
                    BannedLinks = new[]
                    {
                        new BannedLink
                        {
                            LinkRegex = ".*\\.?google\\.com",
                            ThreadComment = "This has been removed."
                        }
                    }
                }
            };
        }

        private static void DoUpdates(object discarded)
        {
            Console.WriteLine("Running updates...");
            try
            {
                CheckInbox();
                CheckNewLinks();
                Console.WriteLine("Update completed successfully.");
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception occured during update:");
                Console.WriteLine(e.ToString());
            }
        }

        private static void CheckNewLinks()
        {
            Console.WriteLine("Checking /new for banned links...");
 	        foreach (var subreddit in Subreddits)
            {
                var newLinks = subreddit.GetNew();
                var registeredSubreddit = Config.Subreddits.FirstOrDefault(s => s.Name == "/r/" + subreddit.Name);
                foreach (var link in newLinks)
                {
                    foreach (var bannedLink in registeredSubreddit.BannedLinks)
                    {
                        if (Regex.IsMatch(link.Domain, bannedLink.LinkRegex))
                        {
                            Console.WriteLine("Removing {0} by /u/{1}: {2} is banned", link.Title, link.Author, link.Domain);
                            if (bannedLink.Spam)
                                link.RemoveSpam();
                            else
                                link.Remove();
                            var comment = link.Comment(bannedLink.ThreadComment);
                            comment.Distinguish(DistinguishType.Moderator);
                        }
                    }
                }
            }
        }

        private static void CheckInbox()
        {
            Console.WriteLine("Checking inbox for flair requests...");
            var messages = Reddit.User.GetUnreadMessages();
            foreach (var message in messages)
            {
                message.SetAsRead();
                if (!message.IsComment && message.Subject == "Set flair")
                {
                    int flair;
                    if (int.TryParse(message.Body, out flair))
                    {
                        Console.WriteLine("Setting /u/{0} to {1}", message.Author, flair);
                        foreach (var subreddit in Subreddits)
                            subreddit.SetUserFlair(message.Author, flair.ToString(), null);
                        message.Reply(Config.SuccessfulFlairMessage);
                    }
                    else
                    {
                        Console.WriteLine("Setting /u/{0} to {1}", message.Author, Config.ErronousFlair);
                        foreach (var subreddit in Subreddits)
                            subreddit.SetUserFlair(message.Author, flair.ToString(), Config.ErronousFlair);
                        message.Reply(Config.ErrornousFlairMessage);
                    }
                }
            }
        }
    }
}
