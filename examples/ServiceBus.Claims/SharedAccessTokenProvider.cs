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
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using Amqp.Claims;

    public class SharedAccessTokenProvider : ITokenProvider
    {
        const string TokenType = "servicebus.windows.net:sastoken";

        readonly string keyName;
        readonly string keyValue;

        public SharedAccessTokenProvider(string keyName, string keyValue)
        {
            this.keyName = keyName;
            this.keyValue = keyValue;
        }

        Task<TokenInfo> ITokenProvider.GetTokenAsync(string audience, string[] claims, TimeSpan duration, CancellationToken cancellationToken)
        {
            DateTime expiryUtc = DateTime.UtcNow + duration;
            long expirySeconds = (long)(expiryUtc - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            string encodedUri = HttpUtility.UrlEncode(audience);

            string sig;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(keyValue)))
            {
                sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(encodedUri + "\n" + expirySeconds)));
            }

            string token = string.Format(
                "SharedAccessSignature sig={0}&se={1}&skn={2}&sr={3}",
                HttpUtility.UrlEncode(sig),
                expirySeconds,
                HttpUtility.UrlEncode(keyName),
                encodedUri);

            var claimsToken = new TokenInfo
            {
                Token = token,
                Type = TokenType,
                Expiry = expiryUtc
            };

            return Task.FromResult(claimsToken);
        }
    }
}
