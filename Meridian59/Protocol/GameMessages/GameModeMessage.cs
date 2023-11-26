﻿/*
 Copyright (c) 2012-2013 Clint Banzhaf
 This file is part of "Meridian59 .NET".

 "Meridian59 .NET" is free software: 
 You can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, 
 either version 3 of the License, or (at your option) any later version.

 "Meridian59 .NET" is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
 without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
 See the GNU General Public License for more details.

 You should have received a copy of the GNU General Public License along with "Meridian59 .NET".
 If not, see http://www.gnu.org/licenses/.
*/

using System;
using Meridian59.Protocol.Enums;

namespace Meridian59.Protocol.GameMessages
{
    /// <summary>
    /// Abstract base implementation for messages in protocol mode 'game'
    /// </summary>
    [Serializable]
    public abstract class GameModeMessage : GameMessage
    {
        public override string Description { get { return ((MessageTypeGameMode)PI).ToString(); } }

        public GameModeMessage()
            : base() { }

        public GameModeMessage(MessageTypeGameMode MessageType)
            : base((byte)MessageType) { }

        public GameModeMessage(byte[] Buffer, int StartIndex = 0, bool IsTCP = true)
            : base(Buffer, StartIndex, IsTCP) { }

        public unsafe GameModeMessage(ref byte* Buffer, bool IsTCP = true)
            : base(ref Buffer, IsTCP) { }
    }
}
