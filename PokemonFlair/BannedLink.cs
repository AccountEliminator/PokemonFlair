using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PokemonFlair
{
    public class BannedLink
    {
        public string LinkRegex { get; set; }
        public string ThreadComment { get; set; }
        public bool Spam { get; set; }
    }
}
