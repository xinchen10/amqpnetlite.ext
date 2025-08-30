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

namespace ServiceBus.Claims
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
    using Amqp.Claims;
    using Azure.Core;
    using Azure.Identity;

    public class AzureCredentialTokenProvider : ITokenProvider
    {
        public const string ServiceBusAudience = "https://servicebus.azure.net/";
        public const string EventHubsAudience = "https://eventhubs.azure.net/";
        const string TokenType = "jwt";
        const int RefreshMinSeconds = 20;
        readonly TokenCredential tokenCredential;
        readonly ConcurrentDictionary<string, TokenInfo> tokenCache;

        public AzureCredentialTokenProvider(TokenCredential tokenCredential = null)
        {
            // TokenCredential details:
            // https://learn.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential?view=azure-dotnet
            this.tokenCredential = tokenCredential ?? new DefaultAzureCredential(true);
            this.tokenCache = new ConcurrentDictionary<string, TokenInfo>(3, 5, StringComparer.Ordinal);
        }

        async Task<TokenInfo> ITokenProvider.GetTokenAsync(string audience, string[] claims, TimeSpan duration, CancellationToken cancellationToken)
        {
            if (this.tokenCache.TryGetValue(audience, out TokenInfo tokenInfo))
            {
                if (tokenInfo.Expiry > DateTime.UtcNow.AddSeconds(RefreshMinSeconds))
                {
                    return tokenInfo;
                }
            }

            // use the default scope for the audience
            // ServiceBusDefaultScope = "https://servicebus.azure.net/.default";
            // EventHubDefaultScope = "https://eventhubs.azure.net/.default";
            var context = new TokenRequestContext(new string[] { audience + ".default" });
            var token = await this.tokenCredential.GetTokenAsync(context, cancellationToken);

            tokenInfo = new TokenInfo
            {
                Token = token.Token,
                Type = TokenType,
                Expiry = token.ExpiresOn.UtcDateTime
            };

            this.tokenCache[audience] = tokenInfo;
            var utcNow = DateTime.UtcNow;
            foreach (var kvp in this.tokenCache)
            {
                if (!string.Equals(kvp.Key, audience) && kvp.Value.Expiry >= utcNow)
                {
                    this.tokenCache.TryRemove(kvp.Key, out _);
                }
            }

            return tokenInfo;
        }
    }
}
