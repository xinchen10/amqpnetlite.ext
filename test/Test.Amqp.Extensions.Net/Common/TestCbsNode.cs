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

namespace Test.Amqp.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography.X509Certificates;
    using global::Amqp;
    using global::Amqp.Claims;
    using global::Amqp.Framing;
    using global::Amqp.Listener;
    using Listener.IContainer;
    using Test.Common;

    public class TestCbsNode : INode
    {
        public string Name => CbsClient.CbsNodeName;

        bool INode.AttachLink(ListenerConnection connection, ListenerSession session, ListenerLink link, Attach attach)
        {
            attach.SndSettleMode = SenderSettleMode.Settled;
            var cache = connection.GetOrAddProperty<TokenCache>(Name, () => new TokenCache());
            return cache.OnLink(link, attach);
        }

        sealed class TokenCache
        {
            readonly Dictionary<string, string> tokens;
            ListenerLink sender;
            ListenerLink receiver;

            public TokenCache()
            {
                this.tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            public bool OnLink(Link link, Attach attach)
            {
                string address = attach.Role ? ((Source)attach.Source).Address : ((Target)attach.Target).Address;
                if (string.Equals(CbsClient.CbsNodeName, address, StringComparison.Ordinal))
                {
                    if (!attach.Role)
                    {
                        this.receiver = (ListenerLink)link;
                        this.receiver.InitializeReceiver(10, OnMessage, this);
                    }
                    else
                    {
                        this.sender = (ListenerLink)link;
                    }

                    return true;
                }

                // Authorize
                if (!Uri.TryCreate(address, UriKind.Absolute, out _))
                {
                    var open = link.Session.Connection.GetOrAddProperty<Open>(TestAmqpBroker.RemoteOpenName);
                    if (open != null)
                    {
                        address = $"http://{open.HostName}/{address}";
                    }
                }

                if (!this.tokens.TryGetValue(address, out string token))
                {
                    throw new UnauthorizedAccessException();
                }

                return false; 
            }

            static void OnMessage(ListenerLink link, Message request, DeliveryState deliveryState, object state)
            {
                var thisPtr = (TokenCache)state;
                if (thisPtr.sender == null)
                {
                    var _ = thisPtr.receiver.CloseAsync(TimeSpan.FromSeconds(20), new Error("cbs:missing-response-link"));
                    return;
                }

                link.DisposeMessage(request, new Accepted(), true);
                request = TestAmqpBroker.Decode(request);
                var audience = (string)request.ApplicationProperties["name"];
                thisPtr.tokens[audience] = (string)request.Body;

                var response = new Message();
                response.Properties = new Properties();
                response.Properties.CorrelationId = request.Properties.MessageId;
                response.ApplicationProperties = new ApplicationProperties();
                response.ApplicationProperties["status-code"] = 200;

                thisPtr.sender.SendMessage(response);
            }
        }
    }
}
