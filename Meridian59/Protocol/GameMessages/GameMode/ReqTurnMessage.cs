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
using Meridian59.Common.Constants;

namespace Meridian59.Protocol.GameMessages
{
    [Serializable]
    public class ReqTurnMessage : GameModeMessage
    {       
        #region IByteSerializable implementation
        public override int ByteLength
        {
            get
            {
                return base.ByteLength + TypeSizes.INT + TypeSizes.SHORT;
            }
        }

        public override int WriteTo(byte[] Buffer, int StartIndex = 0)
        {
            int cursor = StartIndex;

            cursor += base.WriteTo(Buffer, StartIndex);

            Array.Copy(BitConverter.GetBytes(AvatarID), 0, Buffer, cursor, TypeSizes.INT);
            cursor += TypeSizes.INT;

            Array.Copy(BitConverter.GetBytes(ViewDir), 0, Buffer, cursor, TypeSizes.SHORT);
            cursor += TypeSizes.SHORT;

            return cursor - StartIndex;
        }

        public override int ReadFrom(byte[] Buffer, int StartIndex = 0)
        {
            int cursor = StartIndex;

            cursor += base.ReadFrom(Buffer, StartIndex);

            AvatarID = BitConverter.ToUInt32(Buffer, cursor);
            cursor += TypeSizes.INT;

            ViewDir = BitConverter.ToUInt16(Buffer, cursor);
            cursor += TypeSizes.SHORT;
            
            return cursor - StartIndex;
        }
        #endregion

        public uint AvatarID { get; set; }
        public ushort ViewDir { get; set; }
        
        public ReqTurnMessage(ushort ViewDir, uint AvatarID) 
            : base(MessageTypeGameMode.ReqTurn)
        {
            this.ViewDir = ViewDir;
            this.AvatarID = AvatarID;
        }

        public ReqTurnMessage(byte[] Buffer, int StartIndex = 0, bool IsTCP = true) 
            : base (Buffer, StartIndex = 0, IsTCP) { }
    }
}
