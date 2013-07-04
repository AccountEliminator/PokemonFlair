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
			JsonConvert.PopulateObject(File.ReadAllText("config.json"), Config);
			SaveConfig(); // To include any new properties added during an upgrade or the like
            Console.WriteLine("Logging into Reddit as /u/{0}", Config.Login.Username);
            Reddit.UserAgent = "/u/pokemon-flair: /r/pokemon flair and moderation bot";
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

        private static void SaveConfig()
        {
            File.WriteAllText("config.json", JsonConvert.SerializeObject(Config, Formatting.Indented));
        }

        private static void DoUpdates(object discarded)
        {
            Console.WriteLine("Running updates...");
            try
            {
                CheckInbox();
                CheckNewLinks();
				CheckSilentBans();
                Console.WriteLine("Update completed successfully.");
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception occured during update:");
                Console.WriteLine(e.ToString());
            }
        }

		private static void CheckSilentBans()
		{
			Console.WriteLine("Checking for posts from silenced users...");
			foreach (var subreddit in Config.Subreddits)
			{
				foreach (var _user in subreddit.SilencedUsers)
				{
					var user = Reddit.GetUser(_user);
					var overview = user.GetOverview().Take(25).ToArray();
					var relevantEntries = overview.Where(e =>
                    {
						if (e is Post)
							return "/r/" + (e as Post).Subreddit == subreddit.Name;
						else
							return "/r/" + (e as Comment).Subreddit == subreddit.Name;
					}).ToArray();
					foreach (var entry in relevantEntries)
					{
						if (entry is Post)
						{
							var post = entry as Post;
							if (string.IsNullOrEmpty(post.BannedBy))
							{
								Console.WriteLine("Removing \"{0}\", by /u/{1}", post.Title, post.AuthorName);
								post.Remove();
							}
						}
						else if (entry is Comment)
						{
							var comment = entry as Comment;
							if (string.IsNullOrEmpty(comment.BannedBy))
							{
								Console.WriteLine("Removing comment {0} by /u/{1}", comment.FullName, comment.Author);
								comment.Remove();
							}
						}
					}
				}
			}
		}

        private static void CheckNewLinks()
        {
            Console.WriteLine("Checking /new for banned links...");
 	        foreach (var subreddit in Subreddits)
            {
                var registeredSubreddit = Config.Subreddits.FirstOrDefault(s => s.Name == "/r/" + subreddit.Name);
                if (!registeredSubreddit.BannedDomainsEnabled) continue;
                var newLinks = subreddit.GetNew().Take(25);
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
            Console.WriteLine("Checking inbox...");
            var messages = Reddit.User.GetUnreadMessages();
            foreach (var message in messages)
            {
                message.SetAsRead();
                if (Config.TrustedUsers.Contains(message.Author))
                    HandleTrustedMessage(message);
                if (!message.IsComment && message.Subject.StartsWith("invitation to moderate /r/"))
                    HandleModerationInvite(message);
                if (!message.IsComment && message.Subject == "Set flair")
                {
                    int flair;
                    if (int.TryParse(message.Body, out flair) && flair >= Config.MinimumFlairNumber && flair <= Config.MaximumFlairNumber)
                    {
                        Console.WriteLine("Setting /u/{0} to {1}", message.Author, flair);
                        foreach (var subreddit in Subreddits)
                        {
                            var registeredSubreddit = Config.Subreddits.FirstOrDefault(s => s.Name == "/r/" + subreddit.Name);
                            if (!registeredSubreddit.FlairEnabled) continue;
                            subreddit.SetUserFlair(message.Author, flair.ToString(), null);
                        }
                        message.Reply(Config.SuccessfulFlairMessage);
                    }
                    else
                    {
                        Console.WriteLine("Setting /u/{0} to {1}", message.Author, Config.ErronousFlair);
                        foreach (var subreddit in Subreddits)
                        {
                            var registeredSubreddit = Config.Subreddits.FirstOrDefault(s => s.Name == "/r/" + subreddit.Name);
                            if (!registeredSubreddit.FlairEnabled) continue;
                            subreddit.SetUserFlair(message.Author, Config.ErronousFlair, string.Empty);
                        }
                        message.Reply(Config.ErrornousFlairMessage);
                    }
                }
            }
            Console.WriteLine("Checking modmail...");
            var modmail = Reddit.User.GetModMail().Take(25);
            foreach (var mail in modmail)
            {
                if (mail.Subject.StartsWith("Moderation of /r/"))
                    HandleModerationRequest(mail);
            }
        }

        private static void HandleModerationRequest(PrivateMessage message)
        {
            if (Config.Subreddits.Any(s => s.Name == "/r/" + message.Subreddit) || Config.BlacklistedReddits.Contains("/r/" + message.Subreddit))
                return;
            if (message.Replies == null)
                return;
            if (message.Replies.Length == 1)
            {
                if (message.Replies[0].Body == "activate /r/" + message.Subreddit)
                {
                    Console.WriteLine("Activating /r/" + message.Subreddit);
                    message.Replies[0].Reply("Alright, I'll get everything set up. Welcome to the family!");
                    var subreddit = new RegisteredSubreddit();
                    subreddit.Name = "/r/" + message.Subreddit;
                    subreddit.BannedDomainsEnabled = false;
                    subreddit.FlairEnabled = true;
                    Config.Subreddits = Config.Subreddits.Concat(new[] { subreddit }).ToArray();
                    SaveConfig();
                    var redditSr = Reddit.GetSubreddit(subreddit.Name);
                    UpdateSubreddit(redditSr);
                    Subreddits = Subreddits.Concat(new[] { redditSr }).ToArray();
                    foreach (var user in Config.TrustedUsers)
                        Reddit.ComposePrivateMessage("Subreddit added", "I have added " + subreddit.Name + " to the /r/pokemon flair network.", user);
                }
            }
        }

        private static void HandleModerationInvite(PrivateMessage message)
        {
            var subreddit = Reddit.GetSubreddit(message.Subject.Substring("invitation to moderate ".Length));
            subreddit.AcceptModeratorInvite();
            Console.WriteLine("Invited to moderate /r/" + subreddit.Name);
            Reddit.ComposePrivateMessage("Moderation of /r/" + subreddit.Name,
                "Greetings! I've recieved your request for moderation on your subreddit. I can include your subreddit in the /r/pokemon flair system. " +
                "If you choose to continue, all flair set on /r/pokemon will also be set in your subreddit. I will do the following things to your subreddit " +
                "once you decide to move forward:\n\n" +
                "* Modify your CSS\n" +
                "* Modify your sidebar\n" +
                "* Automatically update both when /u/sircmpwn changes the flair system\n\n" +
                "Interested? Alright, reply to this message with only the text `activate /r/" + subreddit.Name + "`",
                "/r/" + subreddit.Name);
        }

        private static void HandleTrustedMessage (PrivateMessage message)
		{
			if (message.Subject == "REMOVE SUBREDDIT")
			{
				Console.WriteLine("Removing subreddit per trusted request");
				var subreddit = Subreddits.FirstOrDefault(r => "/r/" + r.Name == message.Body);
				subreddit.RemoveModerator("t2_" + Reddit.User.Id);
				Reddit.ComposePrivateMessage("Leaving subreddit", "I have been instructed to remove this subreddit from the /r/pokemon family. It's been fun, guys.", "/r/" + subreddit.Name);
				var registeredSubreddit = Config.Subreddits.FirstOrDefault(s => s.Name == "/r/" + subreddit.Name);
				Subreddits = Subreddits.Where(s => s != subreddit).ToArray();
				Config.Subreddits = Config.Subreddits.Where(s => s != registeredSubreddit).ToArray();
				SaveConfig();
				message.Reply("Removed /r/" + subreddit.Name);
			}
			if (message.Subject == "BLACKLIST SUBREDDIT")
			{
				Console.WriteLine("Blacklisting subreddit per trusted request");
				Config.BlacklistedReddits = Config.BlacklistedReddits.Concat(new[] { message.Body }).ToArray();
				SaveConfig();
				message.Reply("Blacklisted " + message.Body);
			}
			if (message.Subject == "PUSH UPDATE")
			{
				Console.WriteLine("Pushing updates to all subreddits per trusted request");
				message.Reply("Pushing updates to all subreddits.");
				foreach (var subreddit in Subreddits)
					UpdateSubreddit(subreddit);
			}
			if (message.Subject == "SILENCE USER")
			{
				Console.WriteLine("Silencing user per trusted request");
				var lines = message.Body.Replace("\r", string.Empty).Split('\n');
				var subreddit = Config.Subreddits.FirstOrDefault(s => s.Name == lines[1]);
				if (subreddit == null)
					message.Reply("I'm not tracking that subreddit.");
				else
				{
					subreddit.SilencedUsers = subreddit.SilencedUsers.Concat(new[] { lines[0] }).ToArray();
					SaveConfig();
					message.Reply("User silenced.");
					Reddit.ComposePrivateMessage("User silenced", string.Format("I have silenced /u/{0}, at the request of /u/{1}." +
						" This means that I will now automatically remove all comments and posts this user makes in {2}.",
                        lines[0], message.Author, lines[1]), lines[1]);
				}
			}
        }

        private static void UpdateSubreddit(Subreddit subreddit)
        {
            var registeredSubreddit = Config.Subreddits.FirstOrDefault(s => s.Name == "/r/" + subreddit.Name);
            if (!registeredSubreddit.FlairEnabled)
                return;
            var stylesheet = subreddit.GetStylesheet();
            if (stylesheet.CSS.Contains("/**BEGIN FLAIR CSS**/"))
            {
                // Remove old CSS
                int startIndex = stylesheet.CSS.IndexOf("/**BEGIN FLAIR CSS**/");
                int endIndex = stylesheet.CSS.IndexOf("/**END FLAIR CSS**/");
                if (endIndex != -1)
                {
                    stylesheet.CSS = stylesheet.CSS.Substring(0, startIndex) +
                        stylesheet.CSS.Substring(endIndex + "/**END FLAIR CSS**/".Length);
                }
            }
            stylesheet.CSS += "\n\n" + File.ReadAllText("flair.css");
            if (stylesheet.Images.Any(i => i.Name == "flairsheet"))
                stylesheet.Images.FirstOrDefault(i => i.Name == "flairsheet").Delete();
            stylesheet.UploadImage("flairsheet", ImageType.PNG, File.ReadAllBytes("flairsheet.png"));
            if (stylesheet.Images.Any(i => i.Name == "missingno"))
                stylesheet.Images.FirstOrDefault(i => i.Name == "missingno").Delete();
            stylesheet.UploadImage("missingno", ImageType.PNG, File.ReadAllBytes("missingno.png"));
            stylesheet.UpdateCss();
            var settings = subreddit.GetSettings();
            if (!settings.Sidebar.Contains("(/r/pokemon/wiki/flair)"))
            {
                settings.Sidebar = "[Edit your Pokemon flair](/r/pokemon/wiki/flair)\n\n" + settings.Sidebar;
                settings.UpdateSettings();
            }
            Reddit.ComposePrivateMessage("Flair CSS", "Your subreddit has been updated with the latest /r/pokemon flair changes.", "/r/" + subreddit.Name);
        }
    }
}
