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
using System.Text;
using Meridian59.Common.Constants;
using Meridian59.Protocol.Enums;
using Meridian59.Common.Enums;
using Meridian59.Common;

namespace Meridian59.Protocol.GameMessages
{
    [Serializable]
    public class SayToMessage : GameModeMessage
    {      
        #region IByteSerializable implementation
        public override int ByteLength
        {
            get
            {
                return base.ByteLength + TypeSizes.BYTE + TypeSizes.SHORT + Message.Length;
            }
        }

        public override int WriteTo(byte[] Buffer, int StartIndex = 0)
        {
            int cursor = StartIndex;

            cursor += base.WriteTo(Buffer, cursor);

            Buffer[cursor] = (byte)SendChatMessageType;
            cursor++;

            Array.Copy(BitConverter.GetBytes(Convert.ToUInt16(Message.Length)), 0, Buffer, cursor, TypeSizes.SHORT);
            cursor += TypeSizes.SHORT;

            Array.Copy(Util.Encoding.GetBytes(Message), 0, Buffer, cursor, Message.Length);
            cursor += Message.Length;

            return cursor - StartIndex;
        }

        public override int ReadFrom(byte[] Buffer, int StartIndex = 0)
        {
            int cursor = StartIndex;

            cursor += base.ReadFrom(Buffer, cursor);

            SendChatMessageType = (ChatTransmissionType)Buffer[cursor];
            cursor++;

            ushort len = BitConverter.ToUInt16(Buffer, cursor);
            cursor += TypeSizes.SHORT;

            Message = Util.Encoding.GetString(Buffer, cursor, len);
            cursor += len;

            return cursor - StartIndex;
        }
        #endregion
        
        public ChatTransmissionType SendChatMessageType { get; set; }
        public string Message { get; set; }

        public SayToMessage(ChatTransmissionType SendChatMessageType, string Message) 
            : base(MessageTypeGameMode.SayTo)
        {
            this.SendChatMessageType = SendChatMessageType;
            this.Message = Message;
        }

        public SayToMessage(byte[] Buffer, int StartIndex = 0) 
            : base (Buffer, StartIndex = 0) { }        
    }
}
