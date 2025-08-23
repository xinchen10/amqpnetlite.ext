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
    using System.Threading;
    using System.Threading.Tasks;
    using global::Amqp;
    using global::Amqp.Claims;
    using global::Amqp.Sasl;
    using Listener.IContainer;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class CbsTests
    {
        const string address = "amqp://localhost:5672";
        TestAmqpBroker broker;

        static CbsTests()
        {
            //Trace.TraceLevel = TraceLevel.Frame;
            //Trace.TraceListener = (l, f, a) => System.Diagnostics.Trace.WriteLine(System.DateTime.Now.ToString("[hh:mm:ss.fff]") + " " + string.Format(f, a));
        }

        public void TestInit(string capability)
        {
            broker = new TestAmqpBroker(new[] { address }, null, null, null);
            broker.AddNode(new TestCbsNode() { Capability = capability });
            broker.Start();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            broker?.Stop();
        }

        [TestMethod]
        public async Task CbsRequestResponseTest()
        {
            TestInit(null);
            await RunCbsSendReceiveTest(nameof(CbsRequestResponseTest));
        }

        [TestMethod]
        public async Task CbsLinkTest()
        {
            TestInit(CbsClient.CapabilityName);
            await RunCbsSendReceiveTest(nameof(CbsLinkTest));
        }

        async Task RunCbsSendReceiveTest(string entity)
        {
            var factory = new ConnectionFactory();
            factory.SASL.Profile = SaslProfile.Anonymous;

            var addressUri = new Address(address);

            var cbs = new CbsClient(new TestTokenProvider());
            var connection = await factory.CreateAsync(addressUri, cbs);

            await cbs.AuthenticateAsync($"http://{addressUri.Host}/{entity}", new[] { "Send", "Listen" }, true, CancellationToken.None);

            var session = new Session(connection);
            var sender = new SenderLink(session, "queue-sender", entity);
            await sender.SendAsync(new Message("test"));
            await sender.CloseAsync();

            var receiver = new ReceiverLink(session, "queue-receiver", entity);
            var message = await receiver.ReceiveAsync();
            receiver.Accept(message);
            await receiver.CloseAsync();

            await cbs.CloseAsync();
            await session.CloseAsync();
            await connection.CloseAsync();
        }

        [TestMethod]
        public async Task CbsRequestLinkCreditTest()
        {
            TestInit(null);
            await RunCbsLinkCreditTest(nameof(CbsRequestLinkCreditTest));
        }

        [TestMethod]
        public async Task CbsLinkCreditTest()
        {
            TestInit(CbsClient.CapabilityName);
            await RunCbsLinkCreditTest(nameof(CbsLinkCreditTest));
        }

        public async Task RunCbsLinkCreditTest(string entity)
        {
            var factory = new ConnectionFactory();
            factory.SASL.Profile = SaslProfile.Anonymous;

            var addressUri = new Address(address);

            var cbs = new CbsClient(new TestTokenProvider());
            var connection = await factory.CreateAsync(addressUri, cbs);

            for (int i = 0; i < 100; i++)
            {
                await cbs.AuthenticateAsync($"http://{addressUri.Host}/{entity}", new[] { "Send", "Listen" }, true, CancellationToken.None);
            }

            await connection.CloseAsync();
        }
    }
}
