using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sino.AspNetCore.EventBus
{

    public interface IEventBusFactory
    {

        IEventBus CreateEventBus<TEventBus>() where TEventBus : IEventBus;

        IEventBus CreateEventBus<TEventBus>(long maxPendingEventNumber) where TEventBus : IEventBus;

    }

}