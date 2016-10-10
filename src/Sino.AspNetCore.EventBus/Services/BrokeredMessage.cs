using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sino.AspNetCore.EventBus
{

    public class BrokeredMessage
    {

        public byte[] Body { get; set; }

        public string Type { get; set; }

    }

}