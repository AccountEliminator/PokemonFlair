﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PokemonFlair
{
    public class RegisteredSubreddit
    {
        public RegisteredSubreddit()
        {
            BannedLinks = new BannedLink[0];
        }

        public BannedLink[] BannedLinks { get; set; }
        public string Name { get; set; }
    }
}
