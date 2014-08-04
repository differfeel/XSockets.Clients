﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using XSockets.Client40.Common.Event.Arguments;
using XSockets.Client40.Helpers;
using XSockets.Client40.Model;
using XSockets.Client40.Protocol;
using XSockets.Client40.Protocol.FrameBuilders;
using XSockets.Client40.Protocol.Readers;

namespace XSockets.Client40
{
    /// <summary>
    /// A client for communicating with XSockets over pub/sub
    /// </summary>
    public partial class XSocketClient
    {
        //Receive & Frame methods
        private Rfc6455DataFrame GetDataFrame(FrameType frameType, byte[] payload)
        {
            var frame = new Rfc6455DataFrame
            {
                FrameType = frameType,
                IsFinal = true,
                IsMasked = true,
                MaskKey = new Random().Next(0, 34298),
                Payload = payload
            };
            return frame;
        }
        
        private void ReceiveData(List<byte> data, IReadState readState, Action<FrameType, byte[]> processFrame)
        {
            while (data.Count >= 2)
            {
                bool isFinal = (data[0] & 128) != 0;
                var frameType = (FrameType)(data[0] & 15);
                int length = (data[1] & 127);
                int index = 2;
                int payloadLength;
                if (length == 127)
                {
                    if (data.Count < index + 8)
                        return; //Not complete
                    payloadLength = data.Skip(index).Take(8).ToArray().ToLittleEndianInt();
                    index += 8;
                }
                else if (length == 126)
                {
                    if (data.Count < index + 2)
                        return; //Not complete
                    payloadLength = data.Skip(index).Take(2).ToArray().ToLittleEndianInt();
                    index += 2;
                }
                else
                {
                    payloadLength = length;
                }

                if (data.Count < index + 4)
                    return; //Not complete

                if (data.Count < index + payloadLength)
                    return; //Not complete
                IEnumerable<byte> payload = data
                    .Skip(index)
                    .Take(payloadLength)
                    .Select(b => b);
                readState.Data.AddRange(payload);
                data.RemoveRange(0, index + payloadLength);
                if (frameType != FrameType.Continuation)
                    readState.FrameType = frameType;
                if (!isFinal || !readState.FrameType.HasValue) continue;
                byte[] stateData = readState.Data.ToArray();
                FrameType? stateFrameType = readState.FrameType;
                readState.Clear();
                processFrame(stateFrameType.Value, stateData);
            }
        }

        private void ProcessFrame(FrameType frameType, IEnumerable<byte> data, Action<List<byte>, FrameType> completed)
        {
            completed(data.ToList(), frameType);
        }

        private void Receive()
        {
            _frameHandler = Create((payload, op) =>
                {
                    switch (op)
                    {
                        case FrameType.Text:
                            FireOnMessage(this.Deserialize<Message>(Encoding.UTF8.GetString(payload.ToArray())));
                            break;
                        case FrameType.Binary:
                            FireOnBlob(new Message(payload, MessageType.Binary));
                            break;
                        case FrameType.Ping:
                            SendControlFrame(FrameType.Pong, payload.ToArray());
                            if (this.OnPing != null)
                                this.OnPing(this, new Message(payload,MessageType.Ping));
                            break;
                        case FrameType.Pong:
                            SendControlFrame(FrameType.Ping, payload.ToArray());
                            if (this.OnPong != null)
                                this.OnPong(this, new Message(payload,MessageType.Pong));
                            break;
                        case FrameType.Close:
                            FireOnDisconnected();
                            break;
                    }                    
                });
            Read(new byte[1024]);
        }

        private IXFrameHandler Create(Action<List<byte>, FrameType> onCompleted)
        {
            var readState = new ReadState();
            return new Rfc6455FrameHandler
            {
                ReceiveData =
                    d => ReceiveData(d, readState, (op, data) => ProcessFrame(op, data, onCompleted))
            };
        }

        private void Read(byte[] buffer)
        {
            Socket.Receive(buffer, result =>
                {
                    if (result <= 0)
                    {
                        return;
                    }
                    _frameHandler.Receive(buffer.Take(result));
                    Read(buffer);
                }, ex => FireOnDisconnected());
        }
    }
}