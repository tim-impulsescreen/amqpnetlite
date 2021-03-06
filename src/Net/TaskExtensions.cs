﻿//  ------------------------------------------------------------------------------------
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

namespace Amqp
{
    using System;
    using System.Threading.Tasks;
    using Amqp.Framing;
    using Amqp.Sasl;
    using Amqp.Types;

    // Task based APIs

    public partial interface IAmqpObject
    {
        /// <summary>
        /// Closes an AMQP object asynchronously using a default timeout.
        /// </summary>
        /// <returns>A Task for the asynchronous close operation.</returns>
        Task CloseAsync();

        /// <summary>
        /// Closes an AMQP object asynchronously.
        /// </summary>
        /// <param name="timeout">The time to wait for the task to complete. Refer to AmqpObject.Close for details.</param>
        /// <returns>A Task for the asynchronous close operation.</returns>
        Task CloseAsync(TimeSpan timeout);
    }

    public partial interface ISenderLink
    {
        /// <summary>
        /// Sends a message asynchronously using a default timeout.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns>A Task for the asynchronous send operation.</returns>
        Task SendAsync(Message message);

        /// <summary>
        /// Sends a message asynchronously.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="timeout">The time to wait for the task to complete.</param>
        /// <returns>A Task for the asynchronous send operation.</returns>
        Task SendAsync(Message message, TimeSpan timeout);
    }

    public partial interface IReceiverLink
    {
        /// <summary>
        /// Receives a message asynchronously.
        /// </summary>
        /// <returns>A Task for the asynchronous receive operation. The result is a Message object
        /// if available within a default timeout; otherwise a null value.</returns>
        Task<Message> ReceiveAsync();

        /// <summary>
        /// Receives a message asynchronously.
        /// </summary>
        /// <param name="timeout">The time to wait for a message.</param>
        /// <returns>A Task for the asynchronous receive operation. The result is a Message object
        /// if available within the specified timeout; otherwise a null value.</returns>
        Task<Message> ReceiveAsync(TimeSpan timeout);
    }

    public partial class AmqpObject
    {
        /// <summary>
        /// Closes an AMQP object asynchronously using a default timeout.
        /// </summary>
        /// <returns>A Task for the asynchronous close operation.</returns>
        public Task CloseAsync()
        {
            return this.CloseInternalAsync(DefaultTimeout);
        }

        /// <summary>
        /// Closes an AMQP object asynchronously.
        /// </summary>
        /// <param name="timeout">The time to wait for the task to complete. Refer to AmqpObject.Close for details.</param>
        /// <returns>A Task for the asynchronous close operation.</returns>
        public Task CloseAsync(TimeSpan timeout)
        {
            return this.CloseInternalAsync(DefaultTimeout);
        }

        internal Task CloseInternalAsync(int timeout = 60000)
        {
            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
            if (this.CloseCalled)
            {
                tcs.SetResult(null);
                return tcs.Task;
            }

            try
            {
                this.Closed += (o, e) =>
                {
                    if (e != null)
                    {
                        tcs.SetException(new AmqpException(e));
                    }
                    else
                    {
                        tcs.SetResult(null);
                    }
                };

                this.CloseInternal(0, null);
            }
            catch (Exception exception)
            {
                tcs.SetException(exception);
            }

            return tcs.Task;
        }
    }

    public partial class SenderLink
    {
        /// <summary>
        /// Sends a message asynchronously using a default timeout.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns>A Task for the asynchronous send operation.</returns>
        public Task SendAsync(Message message)
        {
            return this.SendInternalAsync(message, DefaultTimeout);
        }

        /// <summary>
        /// Sends a message asynchronously.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="timeout">The time to wait for the task to complete.</param>
        /// <returns>A Task for the asynchronous send operation.</returns>
        public Task SendAsync(Message message, TimeSpan timeout)
        {
            return this.SendInternalAsync(message, (int)(timeout.Ticks / 10000));
        }

        internal async Task SendInternalAsync(Message message, int timeoutMilliseconds)
        {
            DeliveryState txnState = null;
#if NETFX || NETFX40
            txnState = await TaskExtensions.GetTransactionalStateAsync(this);
#endif
            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
            this.Send(
                message,
                txnState,
                (l, m, o, s) =>
                {
                    var t = (TaskCompletionSource<object>)s;
                    if (o.Descriptor.Code == Codec.Accepted.Code)
                    {
                        t.SetResult(null);
                    }
                    else if (o.Descriptor.Code == Codec.Rejected.Code)
                    {
                        t.SetException(new AmqpException(((Rejected)o).Error));
                    }
                    else if (o.Descriptor.Code == Codec.Released.Code)
                    {
                        t.SetException(new AmqpException(ErrorCode.MessageReleased, null));
                    }
                    else
                    {
                        t.SetException(new AmqpException(ErrorCode.InternalError, o.Descriptor.Name));
                    }
                },
                tcs);

            await tcs.Task;
        }
    }

    public partial class ReceiverLink
    {
        /// <summary>
        /// Receives a message asynchronously.
        /// </summary>
        /// <returns>A Task for the asynchronous receive operation. The result is a Message object
        /// if available within a default timeout; otherwise a null value.</returns>
        public Task<Message> ReceiveAsync()
        {
            return this.ReceiveInternalAsync(DefaultTimeout);
        }

        /// <summary>
        /// Receives a message asynchronously.
        /// </summary>
        /// <param name="timeout">The time to wait for a message.</param>
        /// <returns>A Task for the asynchronous receive operation. The result is a Message object
        /// if available within the specified timeout; otherwise a null value.</returns>
        public Task<Message> ReceiveAsync(TimeSpan timeout)
        {
            return this.ReceiveInternalAsync((int)(timeout.Ticks / 10000));
        }

        internal Task<Message> ReceiveInternalAsync(int timeout = 60000)
        {
            TaskCompletionSource<Message> tcs = new TaskCompletionSource<Message>();
            var message = this.ReceiveInternal(
                (l, m) =>
                {
                    if (l.Error != null)
                    {
                        tcs.TrySetException(new AmqpException(l.Error));
                    }
                    else
                    {
                        tcs.TrySetResult(m);
                    }
                },
                timeout);

            if (message != null)
            {
                tcs.TrySetResult(message);
            }

            return tcs.Task;
        }
    }

    /// <summary>
    /// Provides extension methods for Task based APIs.
    /// </summary>
    public static class TaskExtensions
    {
#if NETFX || NETFX40
        internal static async Task<DeliveryState> GetTransactionalStateAsync(SenderLink sender)
        {
            return await Amqp.Transactions.ResourceManager.GetTransactionalStateAsync(sender);
        }
#endif

#if NETFX || DOTNET
        internal static Task<System.Net.IPAddress[]> GetHostAddressesAsync(string host)
        {
            return System.Net.Dns.GetHostAddressesAsync(host);
        }

        internal static Task<System.Net.IPHostEntry> GetHostEntryAsync(string host)
        {
            return System.Net.Dns.GetHostEntryAsync(host);
        }
#endif

#if NETFX40
        internal static Task<System.Net.IPAddress[]> GetHostAddressesAsync(string host)
        {
            return Task.Factory.FromAsync(
                (c, s) => System.Net.Dns.BeginGetHostAddresses(host, c, s),
                (r) => System.Net.Dns.EndGetHostAddresses(r),
                null);
        }

        internal static Task<System.Net.IPHostEntry> GetHostEntryAsync(string host)
        {
            return Task.Factory.FromAsync(
                (c, s) => System.Net.Dns.BeginGetHostEntry(host, c, s),
                (r) => System.Net.Dns.EndGetHostEntry(r),
                null);
        }

        internal static Task AuthenticateAsClientAsync(this System.Net.Security.SslStream source,
            string targetHost)
        {
            return Task.Factory.FromAsync(
                (c, s) => source.BeginAuthenticateAsClient(targetHost, c, s),
                (r) => source.EndAuthenticateAsClient(r),
                null);
        }

        internal static Task AuthenticateAsClientAsync(this System.Net.Security.SslStream source,
            string targetHost,
            System.Security.Cryptography.X509Certificates.X509CertificateCollection clientCertificates,
            System.Security.Authentication.SslProtocols enabledSslProtocols,
            bool checkCertificateRevocation)
        {
            return Task.Factory.FromAsync(
                (c, s) => source.BeginAuthenticateAsClient(targetHost, clientCertificates, enabledSslProtocols, checkCertificateRevocation, c, s),
                (r) => source.EndAuthenticateAsClient(r),
                null);
        }

        internal static Task AuthenticateAsServerAsync(this System.Net.Security.SslStream source,
            System.Security.Cryptography.X509Certificates.X509Certificate serverCertificate)
        {
            return Task.Factory.FromAsync(
                (c, s) => source.BeginAuthenticateAsServer(serverCertificate, c, s),
                (r) => source.EndAuthenticateAsServer(r),
                null);
        }

        internal static Task AuthenticateAsServerAsync(this System.Net.Security.SslStream source,
            System.Security.Cryptography.X509Certificates.X509Certificate serverCertificate,
            bool clientCertificateRequired,
            System.Security.Authentication.SslProtocols enabledSslProtocols,
            bool checkCertificateRevocation)
        {
            return Task.Factory.FromAsync(
                (c, s) => source.BeginAuthenticateAsServer(serverCertificate, clientCertificateRequired, enabledSslProtocols, checkCertificateRevocation, c, s),
                (r) => source.EndAuthenticateAsServer(r),
                null);
        }

        internal static Task<int> ReadAsync(this System.Net.Security.SslStream source,
            byte[] buffer, int offset, int count)
        {
            return Task.Factory.FromAsync(
                (c, s) => source.BeginRead(buffer, offset, count, c, s),
                (r) => source.EndRead(r),
                null);
        }

        internal static Task WriteAsync(this System.Net.Security.SslStream source,
            byte[] buffer, int offset, int count)
        {
            return Task.Factory.FromAsync(
                (c, s) => source.BeginWrite(buffer, offset, count, c, s),
                (r) => source.EndWrite(r),
                null);
        }

        internal static Task ContinueWith(this Task task, Action<Task, object> action, object state)
        {
            return task.ContinueWith(t => action(t, state));
        }
#endif

#if NETFX || NETFX40 || DOTNET
        internal static ByteBuffer GetByteBuffer(this IBufferManager bufferManager, int size)
        {
            ByteBuffer buffer;
            if (bufferManager == null)
            {
                buffer = new ByteBuffer(size, true);
            }
            else
            {
                ArraySegment<byte> segment = bufferManager.TakeBuffer(size);
                buffer = new RefCountedByteBuffer(bufferManager, segment.Array, segment.Offset, segment.Count, 0);
            }

            return buffer;
        }
#else
        internal static ByteBuffer GetByteBuffer(this IBufferManager bufferManager, int size)
        {
            return new ByteBuffer(size, true);
        }
#endif

        internal static async Task<IAsyncTransport> OpenAsync(this SaslProfile saslProfile, string hostname,
            IBufferManager bufferManager, IAsyncTransport transport, DescribedList command)
        {
            // if transport is closed, pump reader should throw exception
            TransportWriter writer = new TransportWriter(transport, e => { });

            ProtocolHeader myHeader = saslProfile.Start(writer, command);

            AsyncPump pump = new AsyncPump(bufferManager, transport);
            SaslCode code = SaslCode.Auth;

            await pump.PumpAsync(
                header =>
                {
                    saslProfile.OnHeader(myHeader, header);
                    return true;
                },
                buffer =>
                {
                    return saslProfile.OnFrame(hostname, writer, buffer, out code);
                });

            await writer.FlushAsync();

            if (code != SaslCode.Ok)
            {
                throw new AmqpException(ErrorCode.UnauthorizedAccess,
                    Fx.Format(SRAmqp.SaslNegoFailed, code));
            }

            return (IAsyncTransport)saslProfile.UpgradeTransportInternal(transport);
        }
    }
}