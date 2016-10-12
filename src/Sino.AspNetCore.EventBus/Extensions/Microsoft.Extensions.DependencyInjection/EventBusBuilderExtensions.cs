using Microsoft.Extensions.DependencyInjection.Extensions;
using Sino.AspNetCore.EventBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Extensions.DependencyInjection
{

    public static class EventBusBuilderExtensions
    {

        public static void AddEventBus<TEventBus, TEventBusOptions>(this IServiceCollection services, Action<TEventBusOptions> setupAction)
            where TEventBus : class, IEventBus
            where TEventBusOptions : EventBusOptions
        {
            AddEventBus<IEventBus, TEventBus, TEventBusOptions>(services, setupAction);
        }

        public static void AddEventBus<TIEventBus, TEventBus, TEventBusOptions>(this IServiceCollection services, Action<TEventBusOptions> setupAction)
            where TIEventBus : class, IEventBus
            where TEventBus : class, TIEventBus
            where TEventBusOptions : EventBusOptions
        {
            if (services == null)
            {
                throw new ArgumentNullException("services");
            }
            if (setupAction == null)
            {
                throw new ArgumentNullException("setupAction");
            }
            AddEventBus<TIEventBus, TEventBus>(services);
            if (setupAction != null)
            {
                services.Configure(setupAction);
            }
        }

        public static void AddEventBus<TEventBus>(this IServiceCollection services) where TEventBus : class, IEventBus
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IEventBus, TEventBus>());
        }

        public static void AddEventBus<TIEventBus, TEventBus>(this IServiceCollection services)
            where TIEventBus : class, IEventBus
            where TEventBus : class, TIEventBus
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<TIEventBus, TEventBus>());
        }

    }

}
