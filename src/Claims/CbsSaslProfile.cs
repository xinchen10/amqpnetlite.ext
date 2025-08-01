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
    using System.Text;
    using Amqp;
    using Amqp.Sasl;
    using Amqp.Types;

    /// <summary>
    /// The SASL profile for claims-based authentication.
    /// </summary>
    public class CbsSaslProfile : SaslProfile
    {
        /// <summary>
        /// The mechanism integrates AMQP CBS capabilities into the SASL authentication exchange.
        /// </summary>
        public const string Name = "AMQPCBS";

        IReadOnlyList<TokenInfo> tokens;

        /// <summary>
        /// Creates a new CbsSaslProfile object.
        /// </summary>
        /// <param name="tokens">The initial tokens on the client side.</param>
        public CbsSaslProfile(IReadOnlyList<TokenInfo> tokens = null)
            : base(Name)
        {
            this.tokens = tokens;
        }
        
        /// <summary>
        /// <inheritdoc cref="SaslProfile.GetStartCommand(string)"/>
        /// </summary>
        protected override DescribedList GetStartCommand(string hostname)
        {
            if (this.tokens != null)
            {
                return GetClientInit(hostname, this.tokens);
            }

            throw new InvalidOperationException("Client must provide initial tokens.");
        }

        /// <summary>
        /// <inheritdoc cref="SaslProfile.OnCommand(DescribedList)"/>
        /// </summary>
        protected override DescribedList OnCommand(DescribedList command)
        {
            // SaslInit
            if (command.Descriptor.Code == 0x0000000000000041)
            {
                SaslInit init = (SaslInit)command;
                SaslCode code = TryGetTokens(init, out var tokens);
                if (code == SaslCode.Ok)
                {
                    this.tokens = tokens;
                }

                return new SaslOutcome() { Code = code };
            }

            return null;
        }

        /// <summary>
        /// <inheritdoc cref="SaslProfile.UpgradeTransport(ITransport)"/>
        /// </summary>
        protected override ITransport UpgradeTransport(ITransport transport)
        {
            return transport;
        }

        static SaslInit GetClientInit(string hostname, IReadOnlyList<TokenInfo> tokens)
        {
            var builder = new StringBuilder(1024);
            foreach (var tokenInfo in tokens)
            {
                builder.Append(tokenInfo.Type)
                    .Append(tokenInfo.Token)
                    .Append('\0');
            }
            builder.Append('\0');

            SaslInit init = new SaslInit()
            {
                Mechanism = Name,
                InitialResponse = Encoding.UTF8.GetBytes(builder.ToString()),
                HostName = hostname
            };

            return init;
        }

        static SaslCode TryGetTokens(SaslInit init, out IReadOnlyList<TokenInfo> tokens)
        {
            tokens = Array.Empty<TokenInfo>();
            byte[] response = init.InitialResponse;
            if (response.Length > 0)
            {
                string message = Encoding.UTF8.GetString(response, 0, response.Length);
                string[] items = message.Split('\0');
                var list = new List<TokenInfo>(items.Length);
                for (int i = 0; i < items.Length; i++)
                {
                    string[] pair = items[i].Split(' ');
                    if (pair.Length != 2)
                    {
                        return SaslCode.Auth;
                    }

                    list.Add(new TokenInfo()
                    {
                        Type = pair[0], Token = pair[1]
                    });
                }

                if (list.Count > 0)
                {
                    tokens = list;
                    return SaslCode.Ok;
                }
            }

            return SaslCode.Auth;
        }
    }
}