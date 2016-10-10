using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sino.AspNetCore.EventBus
{

    public class RabbitMqEventBusOptions
        : EventBusOptions
    {

        public string HostName { get; set; }

        public int Port { get; set; } = 6375;

        public string Queue { get; set; }

        public string Topic { get; set; }

        public bool Durable { get; set; } = false;

        public bool Exclusive { get; set; } = false;

        public bool AutoDelete { get; set; } = false;

        public IDictionary<string, object> Arguments { get; set; } = null;

        public uint PrefetchSize { get; set; } = 0;

        public ushort PrefetchCount { get; set; } = 1;

        public bool IsGlobal { get; set; } = false;

        public bool UseLocalWhenException { get; set; } = false;

    }

}