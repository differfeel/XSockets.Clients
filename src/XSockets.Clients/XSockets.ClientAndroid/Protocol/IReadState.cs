﻿using System.Collections.Generic;

namespace XSockets.ClientAndroid.Protocol
{
    public interface IReadState
    {
        List<byte> Data { get; }
        FrameType? FrameType { get; set; }
        void Clear();
    }
}