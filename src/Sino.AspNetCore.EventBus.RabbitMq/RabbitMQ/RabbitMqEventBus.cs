using Sino.AspNetCore.EventBus;
using Sino.AspNetCore.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Reflection;
using System.IO;

namespace Sino.AspNetCore.EventBus
{

    public class RabbitMqEventBus
        : OrderedEventBus, IEventBus, IRabbitMqEventBus
    {

        #region Events
        #endregion

        #region Fields
        private readonly ISerializer _serializer;
        private readonly IEventBus _eventBus;
        #endregion

        #region Properties
        public RabbitMqEventBusOptions Options { get; private set; }

        public ConnectionFactory RabbitMqConnectionFactory { get; private set; }

        public IConnection RabbitMqConnection { get; private set; }

        public IModel RabbitMqChannel { get; private set; }

        public QueueingBasicConsumer RabbitMqConsumer { get; private set; }

        /// <summary>
        /// The pending event number which does not yet dispatched.
        /// </summary>
        public override long PendingEventNumber
        {
            get
            {
                throw new NotSupportedException();
            }
        }

        public override bool IsRunning
        {
            get
            {
                return this.RabbitMqConnection != null && this.RabbitMqChannel != null && this.RabbitMqChannel.IsOpen;
            }
        }
        #endregion

        #region Constructors
        /// <summary>
        /// The constructor of RabbitMqEventBus.
        /// </summary>
        /// <param name="maxPendingEventNumber">The maximum pending event number which does not yet dispatched</param>
        public RabbitMqEventBus(ILoggerFactory loggerFactory, ISerializer serializer, IEventBus eventBus, IOptions<RabbitMqEventBusOptions> options)
            : base(loggerFactory, options.Value?.MaxPendingEventNumber32 ?? 0, false)
        {
            this._serializer = serializer;
            this._eventBus = eventBus;
            this.Options = options.Value;

            this.Start();
        }
        #endregion

        #region Methods
        public override void Start()
        {
            if (this.IsRunning)
            {
                return;
            }
            this.RabbitMqConnectionFactory = new ConnectionFactory()
            {
                HostName = this.Options.HostName,
                Port = this.Options.Port
            };
            this.RabbitMqConnection = this.RabbitMqConnectionFactory.CreateConnection(this.GetType().FullName);
            this.RabbitMqChannel = this.RabbitMqConnection.CreateModel();
            //this.RabbitMqChannel.QueueDeclare(
            //    queue: this.Options.Queue,
            //    durable: this.Options.Durable,
            //    exclusive: this.Options.Exclusive,
            //    autoDelete: this.Options.AutoDelete,
            //    arguments: this.Options.Arguments);
            this.RabbitMqChannel.ExchangeDeclare(this.Options.Queue, "fanout");
            var queueName = this.RabbitMqChannel.QueueDeclare().QueueName;
            this.RabbitMqChannel.QueueBind(
                queue: queueName,
                exchange: this.Options.Queue,
                routingKey: "");
            //this.RabbitMqChannel.BasicQos(this.Options.PrefetchSize, this.Options.PrefetchCount, this.Options.IsGlobal);
            this.RabbitMqConsumer = new QueueingBasicConsumer(this.RabbitMqChannel);
            this.RabbitMqChannel.BasicConsume(
                queue: queueName,
                noAck: true,
                consumer: this.RabbitMqConsumer);
            base.Start(true);
        }

        public override void Stop(int timeout = 2000)
        {
            this.RabbitMqChannel.Dispose();
            this.RabbitMqConnection.Dispose();
        }

        /// <summary>
        /// Post an event to the event bus, dispatched after the specific time.
        /// </summary>
        /// <remarks>If you do not need the event processed in the delivery order, use SimpleEventBus instead.</remarks>
        /// <param name="eventObject">The event object</param>
        /// <param name="dispatchDelay">The delay time before dispatch this event</param>
        public override void Post(object eventObject, TimeSpan dispatchDelay)
        {
            BrokeredMessage message = null;
            try
            {
                if (eventObject == null)
                {
                    throw new ArgumentNullException($"{nameof(eventObject)}", $"Parameter {nameof(eventObject)} cannot be null.");
                }

                var eventType = eventObject.GetType();
                var messageBody = this._serializer.SerializeObjectToBytes(null, eventObject);

                message = new BrokeredMessage()
                {
                    Type = eventType.FullName,
                    Body = messageBody
                };

                var responseBytes = this._serializer.SerializeObjectToBytes(null, message);

                if (this.IsRunning)
                {
                    var props = this.RabbitMqChannel.CreateBasicProperties();
                    props.ReplyTo = this.Options.Queue;
                    props.CorrelationId = Guid.NewGuid().ToString();
                    this._logger.LogInformation("Starting to post the event with Id={0}.", props.CorrelationId);
                    this.RabbitMqChannel.BasicPublish(
                        exchange: this.Options.Queue,
                        routingKey: "",
                        basicProperties: props,
                        body: responseBytes);
                    this._logger.LogDebug("The message with Id={0} has been sent.", props.CorrelationId);
                    return;
                }

                this._logger.LogWarning("Can't post the event, the connection to the server has been closed.");
                if (this.Options.UseLocalWhenException)
                {
                    this._eventBus.Post(eventObject, dispatchDelay);
                }
            }
            catch (Exception ex)
            {
                this._logger.LogError("Failed to post the event.", ex);
                this._eventBus.Post(eventObject, dispatchDelay);
            }
        }

        protected virtual async Task<ReceiveResult> Receive(BrokeredMessage message, string messageId)
        {
            try
            {
                this._logger.LogInformation("Received an event with Id={0}.", messageId);
                var bodyType = this.GetMessageBodyType(message.Type);
                var targetObject = this._serializer.DeserializeObject(message.Body, bodyType);
                return await Task.Run(() =>
                {
                    var rpcIsVoid = false;
                    var isInvokeSucceeded = false;
                    object rpcResult = null;
                    Exception rpcException = null;
                    Type rpcReturnType = null;
                    this.InvokeEventHandler(targetObject, (isVoid, ex, result, returnType) =>
                    {
                        rpcIsVoid = isVoid;
                        rpcException = ex;
                        rpcReturnType = returnType;
                        if (ex == null)
                        {
                            this._logger.LogInformation("The event with Id={0} has been processed.", messageId);
                            isInvokeSucceeded = true;
                        }
                        else
                        {
                            this._logger.LogError(string.Format("Failed to process the event with Id={0}.", messageId) +
                                Environment.NewLine + ex?.Message + Environment.NewLine + ex?.StackTrace);
                            isInvokeSucceeded = false;
                        }
                        rpcResult = result;
                    });
                    return new ReceiveResult(isInvokeSucceeded, rpcIsVoid, rpcResult, rpcException, rpcReturnType);
                });
            }
            catch (Exception ex)
            {
                this._logger.LogInformation(string.Format("Failed to process the event with Id={0}, Message={1}.", messageId, ex.Message + Environment.NewLine + ex.StackTrace));
                return new ReceiveResult(false, false, (object)null, ex);
            }
        }
        #endregion

        #region Utilities
        protected async override Task<bool> Process()
        {
            string response = null;
            var ea = this.RabbitMqConsumer.Queue.Dequeue();

            var body = ea.Body;
            var props = ea.BasicProperties;
            var replyProps = this.RabbitMqChannel.CreateBasicProperties();
            replyProps.CorrelationId = props.CorrelationId;

            try
            {
                var bodyContent = Encoding.UTF8.GetString(body);
                this._logger.LogInformation($"Received a raw message from RabbitMQ: \r\n{bodyContent}.");
                var message = this._serializer.DeserializeObject<BrokeredMessage>(body);
                var result = await this.Receive(message, props.CorrelationId);
                if (result.IsSucceeded && !result.IsVoid)
                {
                    var responseMessage = new BrokeredMessage()
                    {
                        Type = result.ResultType?.FullName,
                        Body = this._serializer.SerializeObjectToBytes(null, result.Result)
                    };
                    response = (string)this._serializer.SerializeObject(responseMessage);
                }
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex.Message + Environment.NewLine + ex.StackTrace);
                response = "";
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(response))
                {
                    var responseBytes = Encoding.UTF8.GetBytes(response);
                    this.RabbitMqChannel.BasicPublish(
                        exchange: "",
                        routingKey: props.ReplyTo,
                        basicProperties: replyProps,
                        body: responseBytes);
                    this.RabbitMqChannel.BasicAck(
                        deliveryTag: ea.DeliveryTag,
                        multiple: false);
                }
            }

            return await Task.FromResult(true);
        }

        protected override EventHandlerHolder CreateEventHandlerHolder(object handler, MethodInfo methodInfo, Type parameterType)
        {
            return base.CreateEventHandlerHolder(handler, methodInfo, parameterType);
        }

        protected virtual string GetMessageBodyTypeName(Type type)
        {
            return type.AssemblyQualifiedName;
        }

        protected virtual Type GetMessageBodyType(string typeName)
        {
            return TypeMappingUtils.GetType(typeName);
        }
        #endregion

        #region IDisposable
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
        #endregion

    }

}