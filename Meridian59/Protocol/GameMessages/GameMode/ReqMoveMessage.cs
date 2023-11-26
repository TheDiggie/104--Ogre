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
using Meridian59.Common.Enums;
using Meridian59.Common.Constants;

namespace Meridian59.Protocol.GameMessages
{
    [Serializable]
    public class ReqMoveMessage : GameModeMessage
    {        
        #region IByteSerializable implementation
        public override int ByteLength
        {
            get
            {
#if !VANILLA && !OPENMERIDIAN
                return base.ByteLength + TypeSizes.SHORT + TypeSizes.SHORT + TypeSizes.BYTE + TypeSizes.INT + TypeSizes.SHORT;
#else
                return base.ByteLength + TypeSizes.SHORT + TypeSizes.SHORT + TypeSizes.BYTE + TypeSizes.INT;
#endif
            }
        }

        public override int WriteTo(byte[] Buffer, int StartIndex = 0)
        {
            int cursor = StartIndex;

            cursor += base.WriteTo(Buffer, StartIndex);

            Array.Copy(BitConverter.GetBytes(Y), 0, Buffer, cursor, TypeSizes.SHORT);
            cursor += TypeSizes.SHORT;

            Array.Copy(BitConverter.GetBytes(X), 0, Buffer, cursor, TypeSizes.SHORT);
            cursor += TypeSizes.SHORT;

            Buffer[cursor] = MoveMode;
            cursor++;

            Array.Copy(BitConverter.GetBytes(CurrentMapID), 0, Buffer, cursor, TypeSizes.INT);
            cursor += TypeSizes.INT;

#if !VANILLA && !OPENMERIDIAN
            Array.Copy(BitConverter.GetBytes(Angle), 0, Buffer, cursor, TypeSizes.SHORT);
            cursor += TypeSizes.SHORT;
#endif
            return cursor - StartIndex;
        }

        public override int ReadFrom(byte[] Buffer, int StartIndex = 0)
        {
            int cursor = StartIndex;

            cursor += base.ReadFrom(Buffer, StartIndex);

            Y = BitConverter.ToUInt16(Buffer, cursor);
            cursor += TypeSizes.SHORT;

            X = BitConverter.ToUInt16(Buffer, cursor);
            cursor += TypeSizes.SHORT;

            MoveMode = Buffer[cursor];
            cursor++;

            CurrentMapID = BitConverter.ToUInt32(Buffer, cursor);
            cursor += TypeSizes.INT;

#if !VANILLA && !OPENMERIDIAN
            Angle = BitConverter.ToUInt16(Buffer, cursor);
            cursor += TypeSizes.SHORT;
#endif
            return cursor - StartIndex;
        }
#endregion

        public ushort Y { get; set; }
        public ushort X { get; set; }
        public byte MoveMode { get; set; }
        public uint CurrentMapID { get; set; }
        public ushort Angle { get; set; }

        public ReqMoveMessage(ushort X, ushort Y, byte MoveMode, uint CurrentMapID, ushort Angle) 
            : base(MessageTypeGameMode.ReqMove)
        {
            this.Y = Y;
            this.X = X;
            this.MoveMode = MoveMode;
            this.CurrentMapID = CurrentMapID;
            this.Angle = Angle;
        }

        public ReqMoveMessage(byte[] Buffer, int StartIndex = 0, bool IsTCP = true) 
            : base (Buffer, StartIndex, IsTCP) { }        
    }
}
