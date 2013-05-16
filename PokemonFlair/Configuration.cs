using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PokemonFlair
{
    public class Configuration
    {
        public Configuration()
        {
            Login = new RedditLogin();
            Subreddits = new RegisteredSubreddit[0];
            ErronousFlair = "missingno";
            ErrornousFlairMessage = "Your flair request was incorrectly formatted.";
            SuccessfulFlairMessage = "Your flair has been set.";
            SecondsBetweenUpdates = 60;
        }

        public RedditLogin Login { get; set; }
        public RegisteredSubreddit[] Subreddits { get; set; }
        public int MinimumFlairNumber { get; set; }
        public int MaximumFlairNumber { get; set; }
        public string ErronousFlair { get; set; }
        public string ErrornousFlairMessage { get; set; }
        public string SuccessfulFlairMessage { get; set; }
        public int SecondsBetweenUpdates { get; set; }
    }
}
