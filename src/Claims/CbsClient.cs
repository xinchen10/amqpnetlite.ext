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

namespace Amqp.Claims
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Amqp.Framing;
    using Amqp.Handler;
    using Amqp.Types;
    using Amqp.Request;

    /// <summary>
    /// A client performs security token based authentication as specified in the AMQP CBS specification.
    /// </summary>
    public class CbsClient : IHandler
    {
        const string CapabilityName = "AMQP_CBS_V1_0";
        const string CbsNodeName = "$cbs";
        const int DefaultTokenExpiryInSeconds = 20 * 60;
        readonly ITokenProvider tokenProvider;
        readonly Dictionary<string, Entry> renewEntries;
        Connection connection;
        ICbsClient cbsClient;
        Timer renewTimer;
        DateTime timerExpiry;

        /// <summary>
        /// The event handlers to be notified for token renewal errors.
        /// </summary>
        public event EventHandler<TokenEventArgs> OnError;

        /// <summary>
        /// Creates a claims client object.
        /// </summary>
        /// <param name="tokenProvider">The token provider to get security tokens.</param>
        public CbsClient(ITokenProvider tokenProvider)
        {
            this.tokenProvider = tokenProvider;
            this.renewEntries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            this.timerExpiry = DateTime.MaxValue;
            this.TokenDuration = TimeSpan.FromSeconds(DefaultTokenExpiryInSeconds);
        }

        /// <summary>
        /// Gets or sets the maximum duration of a token (default to 20 minutes).
        /// </summary>
        public TimeSpan TokenDuration { get; set; }

        /// <summary>
        /// Authenticates the client by sending a security token for subsequent operations.
        /// </summary>
        /// <param name="audience">The resource audience.</param>
        /// <param name="claims">The claim actions.</param>
        /// <param name="autoRenew">If true, the client will auto-renew the tokens until it is closed.</param>
        /// <param name="cancellationToken">The cancellation token associated with the async operation.</param>
        /// <returns>A task for the asynchronous operation.</returns>
        public async Task AuthenticateAsync(string audience, string[] claims, bool autoRenew, CancellationToken cancellationToken)
        {
            if (this.cbsClient == null)
            {
                throw new InvalidOperationException("Connection is not open.");
            }

            TokenInfo tokenInfo = await this.tokenProvider.GetTokenAsync(audience, claims, this.TokenDuration, cancellationToken).ConfigureAwait(false);

            await this.cbsClient.SetTokenAsync(audience, tokenInfo, cancellationToken).ConfigureAwait(false);

            if (autoRenew)
            {
                var entry = new Entry { Claims = claims, DueTime = tokenInfo.Expiry };
                lock (this.renewEntries)
                {
                    this.renewEntries[audience] = entry;
                    if (tokenInfo.Expiry < this.timerExpiry)
                    {
                        this.StartTimer(tokenInfo.Expiry);
                    }
                }
            }
        }

        /// <summary>
        /// Removes a resource and stops its token renewal.
        /// </summary>
        /// <param name="audience">The resource audience.</param>
        public void Remove(string audience)
        {
            lock (this.renewEntries)
            {
                this.renewEntries.Remove(audience);
                if (this.renewEntries.Count == 0)
                {
                    this.StopTimer();
                }
            }
        }

        /// <summary>
        /// Closes the CBS client.
        /// </summary>
        public void Close()
        {
            this.StopTimer();
        }

        static void OnRenew(object state)
        {
            var thisPtr = (CbsClient)state;
            var _ = thisPtr.RenewAsync();
        }

        async Task RenewAsync()
        {
            try
            {
                var entries = new List<KeyValuePair<string, Entry>>();
                lock (this.renewEntries)
                {
                    // Disable timer start while renewing tokens
                    this.timerExpiry = DateTime.MinValue;

                    foreach (var kvp in this.renewEntries)
                    {
                        if (kvp.Value.DueTime <= DateTime.UtcNow)
                        {
                            entries.Add(kvp);
                        }
                    }
                }

                if (entries.Count > 0)
                {
                    var cts = new CancellationTokenSource(60000);
                    var tasks = new Task[entries.Count];
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var kvp = entries[i];
                        try
                        {
                            tasks[i] = this.AuthenticateAsync(kvp.Key, kvp.Value.Claims, true, cts.Token);
                        }
                        catch (Exception ex)
                        {
                            tasks[i] = Task.CompletedTask;
                            lock (this.renewEntries)
                            {
                                this.renewEntries.Remove(kvp.Key);
                            }

                            this.OnError?.Invoke(this, new TokenEventArgs { Audience = kvp.Key, Claims = kvp.Value.Claims, Error = ex });
                        }
                    }
                    cts.Dispose();

                    try
                    {
                        await Task.WhenAll(tasks).ConfigureAwait(false);
                    }
                    catch when (!this.connection.IsClosed)
                    {
                        for (int i = 0; i < tasks.Length; i++)
                        {
                            var task = tasks[i];
                            if (!task.IsCompleted)
                            {
                                var kvp = entries[i];
                                Exception ex = task.IsCanceled ? new TaskCanceledException() : task.Exception.InnerException;
                                lock (this.renewEntries)
                                {
                                    this.renewEntries.Remove(kvp.Key);
                                }

                                this.OnError?.Invoke(this, new TokenEventArgs { Audience = kvp.Key, Claims = kvp.Value.Claims, Error = ex });
                            }
                        }
                    }
                }
            }
            finally
            {
                DateTime minDueTime = DateTime.MaxValue;
                lock (this.renewEntries)
                {
                    if (!this.connection.IsClosed)
                    {
                        foreach (var kvp in this.renewEntries)
                        {
                            if (kvp.Value.DueTime < minDueTime)
                            {
                                minDueTime = kvp.Value.DueTime;
                            }
                        }
                    }

                    if (minDueTime < DateTime.MaxValue)
                    {
                        this.StartTimer(minDueTime);
                    }
                    else
                    {
                        this.StopTimer();
                    }
                }
            }
        }

        void StartTimer(DateTime expiry)
        {
            TimeSpan dueTime = expiry - DateTime.UtcNow;
            if (dueTime <= TimeSpan.Zero)
            {
                dueTime = TimeSpan.FromSeconds(1);
            }

            if (this.renewTimer == null)
            {
                this.renewTimer = new Timer(s => OnRenew(s), this, dueTime, Timeout.InfiniteTimeSpan);
            }
            else
            {
                this.renewTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
            }

            this.timerExpiry = expiry;
        }

        void StopTimer()
        {
            this.renewTimer?.Dispose();
            this.renewTimer = null;
            this.timerExpiry = DateTime.MaxValue;
        }

        bool IHandler.CanHandle(EventId id)
        {
            return id == EventId.ConnectionLocalOpen ||
                id == EventId.ConnectionRemoteOpen;
        }

        void OnLocalOpen(Connection connection, Open open)
        {
            this.connection = connection;
            open.DesiredCapabilities = open.DesiredCapabilities.Add((Symbol)CapabilityName);
        }

        void OnRemoteOpen(Connection connection, Open open)
        {
            if (open.OfferedCapabilities.Contains((Symbol)CapabilityName))
            {
                string nodeName = (string)open.Properties?[(Symbol)CbsNodeName] ?? CbsNodeName;
                this.cbsClient = new LinkBasedClient(connection, nodeName);
            }
            else
            {
                this.cbsClient = new MessageBasedClient(connection);
            }
        }

        void IHandler.Handle(Event protocolEvent)
        {
            switch (protocolEvent.Id)
            {
                case EventId.ConnectionLocalOpen:
                    this.OnLocalOpen(protocolEvent.Connection, (Open)protocolEvent.Context);
                    break;
                case EventId.ConnectionRemoteOpen:
                    this.OnRemoteOpen(protocolEvent.Connection, (Open)protocolEvent.Context);
                    break;
                default:
                    break;
            }
        }

        sealed class Entry
        {
            public string[] Claims { get; set; }

            public DateTime DueTime { get; set; }
        }

        interface ICbsClient
        {
            Task SetTokenAsync(string audience, TokenInfo token, CancellationToken cancellationToken);
        }

        /// <summary>
        /// CBS client using a pair of request and response links to put token messages.
        /// Defined in the early CBS working draft.
        /// </summary>
        sealed class MessageBasedClient : RequestClient, ICbsClient
        {
            public MessageBasedClient(Connection connection)
                : base(connection, CbsNodeName)
            {
            }

            async Task ICbsClient.SetTokenAsync(string audience, TokenInfo token, CancellationToken cancellationToken)
            {
                using (var request = new Message(token.Token))
                {
                    request.Properties = new Properties();
                    request.ApplicationProperties = new ApplicationProperties();
                    request.ApplicationProperties["operation"] = "put-token";
                    request.ApplicationProperties["name"] = audience;
                    request.ApplicationProperties["type"] = token.Type;

                    using (Message response = await this.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        if (response == null)
                        {
                            throw new AmqpException("amqp:cbs:no-response", "No response received");
                        }
                        if (response.Properties == null || response.ApplicationProperties == null)
                        {
                            throw new AmqpException("amqp:cbs:invalid-response", "Response has no properties.");
                        }

                        int statusCode = (int)response.ApplicationProperties["status-code"];
                        if (statusCode != 200 && statusCode != 202)
                        {
                            string error = (Symbol)response.ApplicationProperties["error-condition"];
                            string description = (string)response.ApplicationProperties["status-description"];
                            throw new AmqpException(error, description);
                        }

                    }
                }
            }
        }

        /// <summary>
        /// CBS client using a send link to set token messages.
        /// Defined in the latest CBS Committee draft.
        /// </summary>
        sealed class LinkBasedClient : SenderLink, ICbsClient
        {
            public LinkBasedClient(Connection connection, string nodeName)
                : base(new Session(connection), nodeName, nodeName)
            {
            }

            Task ICbsClient.SetTokenAsync(string audience, TokenInfo token, CancellationToken cancellationToken)
            {
                using (var request = new Message(token.Token))
                {
                    request.Properties = new Properties();
                    request.Properties.Subject = "set-token";
                    request.ApplicationProperties = new ApplicationProperties();
                    request.ApplicationProperties["token-type"] = token.Type;

                    return this.SendAsync(request);
                }
            }
        }
    }
}