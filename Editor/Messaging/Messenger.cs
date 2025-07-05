/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Hackerzhuli.Code.Editor.Messaging
{
    internal class Messenger : IDisposable
    {
        private readonly object _disposeLock = new();
        private readonly ConcurrentQueue<Message> _messageQueue = new();

        private readonly UdpSocket _socket;
        private bool _disposed;

        protected Messenger(int port)
        {
            _socket = new UdpSocket();
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

#if UNITY_EDITOR_WIN
            // Explicitely disable inheritance for our UDP socket handle 
            // We found that Unity is creating a fork when importing new assets that can clone our socket
            SetHandleInformation(_socket.Handle, HandleFlags.Inherit, HandleFlags.None);
#endif

            _socket.Bind(IPAddress.Any, port);

            BeginReceiveMessage();
        }

        public void Dispose()
        {
            lock (_disposeLock)
            {
                _disposed = true;
                _socket.Close();
            }
        }

        public event EventHandler<ExceptionEventArgs> MessengerException;

        private void BeginReceiveMessage()
        {
            var buffer = new byte[UdpSocket.BufferSize];
            var any = UdpSocket.Any();

            try
            {
                lock (_disposeLock)
                {
                    if (_disposed)
                        return;

                    _socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref any,
                        ReceiveMessageCallback, buffer);
                }
            }
            catch (SocketException se)
            {
                MessengerException?.Invoke(this, new ExceptionEventArgs(se));

                BeginReceiveMessage();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void ReceiveMessageCallback(IAsyncResult result)
        {
            try
            {
                var endPoint = UdpSocket.Any();

                lock (_disposeLock)
                {
                    if (_disposed)
                        return;

                    _socket.EndReceiveFrom(result, ref endPoint);
                }

                var message = DeserializeMessage(UdpSocket.BufferFor(result));
                if (message != null)
                {
                    message.Origin = (IPEndPoint)endPoint;

                    if (IsValidTcpMessage(message, out var port, out var bufferSize))
                        // switch to TCP mode to handle big messages
                        TcpClient.Queue(message.Origin.Address, port, bufferSize, buffer =>
                        {
                            var originalMessage = DeserializeMessage(buffer);
                            originalMessage.Origin = message.Origin;
                            _messageQueue.Enqueue(originalMessage);
                        });
                    else
                        _messageQueue.Enqueue(message);
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception e)
            {
                RaiseMessengerException(e);
            }

            BeginReceiveMessage();
        }

        private static bool IsValidTcpMessage(Message message, out int port, out int bufferSize)
        {
            port = 0;
            bufferSize = 0;
            if (message.Value == null)
                return false;
            if (message.Type != MessageType.Tcp)
                return false;
            var parts = message.Value.Split(':');
            if (parts.Length != 2)
                return false;
            if (!int.TryParse(parts[0], out port))
                return false;
            return int.TryParse(parts[1], out bufferSize);
        }

        private void RaiseMessengerException(Exception e)
        {
            MessengerException?.Invoke(this, new ExceptionEventArgs(e));
        }

        private static Message MessageFor(MessageType type, string value)
        {
            return new Message { Type = type, Value = value };
        }

        public void SendMessage(IPEndPoint target, MessageType type, string value = "")
        {
            var message = MessageFor(type, value);
            var buffer = SerializeMessage(message);

            try
            {
                lock (_disposeLock)
                {
                    if (_disposed)
                        return;

                    if (buffer.Length >= UdpSocket.BufferSize)
                    {
                        // switch to TCP mode to handle big messages
                        var port = TcpListener.Queue(buffer);
                        if (port > 0)
                        {
                            // success, replace original message with "switch to tcp" marker + port information + buffer length
                            message = MessageFor(MessageType.Tcp, string.Concat(port, ':', buffer.Length));
                            buffer = SerializeMessage(message);
                        }
                    }

                    _socket.BeginSendTo(buffer, 0, Math.Min(buffer.Length, UdpSocket.BufferSize), SocketFlags.None,
                        target, SendMessageCallback, null);
                }
            }
            catch (SocketException se)
            {
                MessengerException?.Invoke(this, new ExceptionEventArgs(se));
            }
        }

        private void SendMessageCallback(IAsyncResult result)
        {
            try
            {
                lock (_disposeLock)
                {
                    if (_disposed)
                        return;

                    _socket.EndSendTo(result);
                }
            }
            catch (SocketException se)
            {
                MessengerException?.Invoke(this, new ExceptionEventArgs(se));
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        ///     Sends a message to the specified endpoint using a blocking send operation with timeout.
        ///     This method is intended for rare cases that must use a blocking send operation.
        ///     Large messages that exceed UDP buffer size will be discarded.
        /// </summary>
        /// <param name="target">The target endpoint to send the message to</param>
        /// <param name="type">The type of message to send</param>
        /// <param name="value">The message value (optional)</param>
        /// <param name="timeoutMs">Timeout in milliseconds for the send operation</param>
        /// <returns>True if the message was sent successfully, false if it failed, timed out, or was too large</returns>
        public bool SendMessageBlocking(IPEndPoint target, MessageType type, string value = "", int timeoutMs = 1000)
        {
            var message = MessageFor(type, value);
            var buffer = SerializeMessage(message);

            // Discard large messages to keep this method simple
            if (buffer.Length >= UdpSocket.BufferSize)
                return false;

            try
            {
                lock (_disposeLock)
                {
                    if (_disposed)
                        return false;
                    // Get original timeout and set new one
                    var originalTimeout =
                        (int)_socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout);
                    _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, timeoutMs);
                    var timeoutChanged = true;
                    var bytesSent = _socket.SendTo(buffer, 0, buffer.Length, SocketFlags.None, target);
                    if (timeoutChanged)
                        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout,
                            originalTimeout);
                    return bytesSent > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static byte[] SerializeMessage(Message message)
        {
            var serializer = new Serializer();
            serializer.WriteInt32((int)message.Type);
            serializer.WriteString(message.Value);

            return serializer.Buffer();
        }

        private static Message DeserializeMessage(byte[] buffer)
        {
            if (buffer.Length < 4)
                return null;

            var deserializer = new Deserializer(buffer);
            var type = (MessageType)deserializer.ReadInt32();
            var value = deserializer.ReadString();

            return new Message { Type = type, Value = value };
        }

        public bool TryDequeueMessage(out Message message)
        {
            return _messageQueue.TryDequeue(out message);
        }

        public static Messenger BindTo(int port)
        {
            return new Messenger(port);
        }

#if UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetHandleInformation(IntPtr hObject, HandleFlags dwMask, HandleFlags dwFlags);

        [Flags]
        private enum HandleFlags : uint
        {
            None = 0,
            Inherit = 1,
            ProtectFromClose = 2
        }
#endif
    }
}