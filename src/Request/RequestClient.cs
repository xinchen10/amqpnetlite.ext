//  ------------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation
//  All rights reserved. 
//  
//  Licensed under the Apache License, Version 2.0 (the ""License""); you may not use this 
//  file except in compliance with the License. You may obtain a copy of the License at 
//  http://www.apache.org/licenses/LICENSE-2.0  
//  
//  THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, 
//  EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY IMPLIED WARRANTIES OR 
//  CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE, MERCHANTABLITY OR 
//  NON-INFRINGEMENT. 
// 
//  See the Apache Version 2.0 License for specific language governing permissions and 
//  limitations under the License.
//  ------------------------------------------------------------------------------------

namespace Amqp.Request
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Amqp.Framing;

    /// <summary>
    /// A client provides request-response based bidirectional communication with two AMQP links.
    /// </summary>
    public class RequestClient : AmqpObject
    {
        readonly Connection connection;
        readonly string nodeName;
        readonly string replyToNodeName;
        readonly ConcurrentQueue<IWork> workQueue;
        readonly Dictionary<string, RequestTaskSource> requests;
        long queueCount;
        long requestId;
        Session session;
        SenderLink sender;
        ReceiverLink receiver;

        /// <summary>
        /// Creates a request client object.
        /// </summary>
        /// <param name="connection">The connection in which to create the AMQP links.</param>
        /// <param name="nodeName">The target name on the peer side.</param>
        public RequestClient(Connection connection, string nodeName)
        {
            this.connection = connection;
            this.nodeName = nodeName;
            this.replyToNodeName = this.nodeName + ".reply-to";
            this.workQueue = new ConcurrentQueue<IWork>();
            this.requests = new Dictionary<string, RequestTaskSource>();
        }

        /// <summary>
        /// Sends a request message to the target node.
        /// </summary>
        /// <param name="request">The request message to send.</param>
        /// <param name="cancellationToken">The cancellation token associated with the async operation.</param>
        /// <returns>A task for the send operation. When completed, returns the response <see cref="Message"/>.</returns>
        public Task<Message> SendAsync(Message request, CancellationToken cancellationToken)
        {
            if (this.IsClosed)
            {
                throw new ObjectDisposedException(this.GetType().Name, this.nodeName);
            }

            if (this.connection.IsClosed)
            {
                throw new InvalidOperationException($"Connection is closed. A new request client to '{this.nodeName}' must be created.");
            }

            if (request.Properties == null)
            {
                request.Properties = new Properties();
            }

            if (request.Properties.MessageId != null)
            {
                throw new InvalidOperationException($"Request message must not set message-id property '{request.Properties.MessageId}'.");
            }

            if (request.Properties.ReplyTo != null)
            {
                throw new InvalidOperationException($"Request message must not set reply-to property '{request.Properties.ReplyTo}'.");
            }

            request.Properties.MessageId = $"{this.nodeName}-{Interlocked.Increment(ref this.requestId)}";
            request.Properties.ReplyTo = this.replyToNodeName;

            var tcs = new RequestTaskSource(this, request, cancellationToken);
            this.Process(tcs);

            return tcs.Task;
        }

        /// <summary>
        /// <inheritdoc cref="AmqpObject.OnClose(Error)"/>
        /// </summary>
        protected override bool OnClose(Error error)
        {
            foreach (var req in this.requests)
            {
                req.Value.OnCancel(false);
            }

            return true;
        }

        void Process(IWork work)
        {
            this.workQueue.Enqueue(work);

            long count = Interlocked.Increment(ref this.queueCount);
            while (count == 1)
            {
                long processed = 0;
                while (this.workQueue.TryDequeue(out IWork head))
                {
                    try
                    {
                        head.Execute(this);
                    }
                    catch
                    {
                    }

                    processed++;
                }

                count = Interlocked.Add(ref this.queueCount, -processed);
            }
        }

        bool Setup()
        {
            if (this.IsClosed || this.connection.IsClosed)
            {
                return false;
            }

            if (this.session == null || this.session.IsClosed || this.sender.IsClosed || this.receiver.IsClosed)
            {
                this.session?.Close(TimeSpan.Zero);

                this.session = new Session(this.connection);

                int iteration = this.session.GetHashCode();
                this.sender = new SenderLink(this.session, $"client-sender{iteration}", this.nodeName);

                var receiverAttach = new Attach()
                {
                    Source = new Source() { Address = this.nodeName },
                    Target = new Target() { Address = this.replyToNodeName }
                };

                this.receiver = new ReceiverLink(session, $"client-receiver{iteration}", receiverAttach, null);

                this.receiver.Start(50, (r, m) => this.OnResonse(m));
            }

            return true;
        }

        void OnResonse(Message response)
        {
            this.receiver.Accept(response);
            this.Process(new CompleteWork() { Response = response });
        }

        interface IWork
        {
            void Execute(RequestClient client);
        }

        sealed class RequestTaskSource : TaskCompletionSource<Message>, IWork
        {
            readonly RequestClient client;
            readonly Message request;
            readonly CancellationToken cancellationToken;
            CancellationTokenRegistration registration;
            int state;  //0: start, 1:cancel, 2:exception, 3:complete
            Exception exception;
            Message response;

            public RequestTaskSource(RequestClient client, Message request, CancellationToken cancellationToken)
            {
                this.client = client;
                this.request = request;
                this.cancellationToken = cancellationToken;
            }

            public void Execute(RequestClient client)
            {
                switch (this.state)
                {
                    case 0:
                        this.Start();
                        break;
                    case 1:
                        this.TrySetCanceled();
                        break;
                    case 2:
                        this.TrySetException(this.exception);
                        break;
                    case 3:
                        this.TrySetResult(this.response);
                        break;
                    default:
                        break;
                }
            }

            public void CancelInPlace()
            {
                if (Interlocked.CompareExchange(ref this.state, 1, 0) == 0)
                {
                    this.Execute(this.client);
                }
            }

            public void OnCancel(bool isAsync)
            {
                this.HandleCompletion(false, 1, isAsync);
            }

            public void OnException(Exception exception, bool isAsync)
            {
                this.exception = exception;
                this.HandleCompletion(exception, 2, isAsync);
            }

            public void OnComplete(Message response, bool isAsync)
            {
                this.response = response;
                this.HandleCompletion(response, 3, isAsync);
            }

            void HandleCompletion<T>(T t, int s, bool isAsync)
            {
                if (Interlocked.CompareExchange(ref this.state, s, 0) == 0)
                {
                    if (isAsync)
                    {
                        this.client.Process(this);
                    }
                    else
                    {
                        this.client.requests.Remove(this.request.Properties.MessageId);
                        this.Execute(this.client);
                    }
                }
            }

            void Start()
            {
                client.requests.Add(this.request.Properties.MessageId, this);
                if (this.cancellationToken.CanBeCanceled)
                {
                    this.registration = this.cancellationToken.Register(s => ((RequestTaskSource)s).OnCancel(true), this);
                }

                try
                {
                    if (client.Setup())
                    {
                        client.sender.Send(this.request, null, null);
                    }
                    else
                    {
                        this.OnCancel(false);
                    }
                }
                catch (Exception ex)
                {
                    this.OnException(ex, false);
                }
            }
        }

        sealed class CompleteWork : IWork
        {
            public Message Response { get; set; }

            void IWork.Execute(RequestClient client)
            {
                if (this.Response.Properties != null && client.requests.TryGetValue(this.Response.Properties.CorrelationId, out var task))
                {
                    task.OnComplete(this.Response, false);
                }
                else
                {
                    this.Response.Dispose();
                }
            }
        }

        sealed class CloseWork : IWork
        {
            void IWork.Execute(RequestClient client)
            {
                foreach (var req in client.requests)
                {
                    req.Value.CancelInPlace();
                }

                client.requests.Clear();
            }
        }
    }
}