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
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amqp;
    using Amqp.Claims;
    using Amqp.Sasl;

    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine(typeof(Program).Namespace + " [SB_ConnectionString]");
                return;
            }

            Trace.TraceLevel = TraceLevel.Frame;
            Trace.TraceListener = (l, f, a) => Console.WriteLine(System.DateTime.Now.ToString("[hh:mm:ss.fff]") + " " + string.Format(f, a));
            
            // Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=someKeyName;SharedAccessKey=someKeyValue;EntityPath=foo
            var kvp = args[0].Split(';').Select(s => s.Split(new[] { '=' }, 2)).ToDictionary(a => a[0], a => a[1]);
            var tokenProvider = new SharedAccessTokenProvider(kvp["SharedAccessKeyName"], kvp["SharedAccessKey"]);

            ConnectionFactory factory = new ConnectionFactory();
            factory.SASL.Profile = SaslProfile.Anonymous;
            Address address = new Address($"amqps://{new Uri(kvp["Endpoint"]).Host}");

            var cbs = new CbsClient(tokenProvider);
            var connection = await factory.CreateAsync(address, cbs);

            string entity = kvp["EntityPath"];
            await cbs.AuthenticateAsync($"http://{address.Host}/{entity}", new[] { "Send", "Listen" }, true, CancellationToken.None);

            var session = new Session(connection);
            var sender = new SenderLink(session, "queue-sender", entity);
            await sender.SendAsync(new Message("test"));
            await sender.CloseAsync();

            var receiver = new ReceiverLink(session, "queue-receiver", entity);
            var message = await receiver.ReceiveAsync();
            receiver.Accept(message);
            await receiver.CloseAsync();

            cbs.Close();
            await session.CloseAsync();
            await connection.CloseAsync();
        }
    }
}
