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

    /// <summary>
    /// Arguments from a token event notification.
    /// </summary>
    public class TokenEventArgs : EventArgs
    {
        /// <summary>
        /// The resource audience.
        /// </summary>
        public string Audience { get; set; }

        /// <summary>
        /// The claim actions.
        /// </summary>
        public string[] Claims { get; set; }

        /// <summary>
        /// The exception, if any, from the notification sender.
        /// </summary>
        public Exception Error { get; set; }
    }
}