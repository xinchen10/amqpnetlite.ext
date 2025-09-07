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
    using System.Threading;
    using System.Threading.Tasks;
    using global::Amqp.Claims;

    public class TestTokenProvider : ITokenProvider
    {
        const string type = "test-token";

        public Task<TokenInfo> GetTokenAsync(string audience, string[] claims, TimeSpan duration, CancellationToken cancellationToken)
        {
            var tokenInfo = new TokenInfo()
            {
                Type = type,
                Token = string.Join(",", claims),
                Expiry = DateTime.UtcNow + duration
            };

            return Task.FromResult(tokenInfo);
        }
    }
}
