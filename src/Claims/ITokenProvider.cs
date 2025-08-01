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
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A provider to generate security tokens.
    /// </summary>
    public interface ITokenProvider
    {
        /// <summary>
        /// Get a security token for accessing resources in the remote peer.
        /// </summary>
        /// <param name="audience">The resource to which the token applies, typically in the Uri format.</param>
        /// <param name="claims">The claim action to be performed on the resource.</param>
        /// <param name="duration">The desired maximum duration for the validity of the acquired token.</param>
        /// <param name="cancellationToken">The cancellation token associated with the async operation.</param>
        /// <returns>A task. When completed, returns the security token.</returns>
        Task<TokenInfo> GetTokenAsync(string audience, string[] claims, TimeSpan duration, CancellationToken cancellationToken);
    }
}