/*
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
using Meridian59.Common.Enums;
using Meridian59.Common.Constants;

namespace Meridian59.Data.Models
{
    [Serializable]
    public class UserCommandMarketplaceList : UserCommand
    {
        public override UserCommandType CommandType { get { return UserCommandType.MarketplaceList; } }

        #region IByteSerializable
        public override int ByteLength
        {
            get
            {
                return TypeSizes.BYTE + ID.ByteLength + TypeSizes.INT + TypeSizes.INT + TypeSizes.INT;
            }
        }

        public override int WriteTo(byte[] Buffer, int StartIndex = 0)
        {
            int cursor = StartIndex;

            Buffer[cursor] = (byte)CommandType;
            cursor++;

            cursor += ID.WriteTo(Buffer, cursor);

            Array.Copy(BitConverter.GetBytes(Quantity), 0, Buffer, cursor, TypeSizes.INT);
            cursor += TypeSizes.INT;

            Array.Copy(BitConverter.GetBytes(UnitPricePlat), 0, Buffer, cursor, TypeSizes.INT);
            cursor += TypeSizes.INT;

            Array.Copy(BitConverter.GetBytes(UnitPriceShills), 0, Buffer, cursor, TypeSizes.INT);
            cursor += TypeSizes.INT;

            return cursor - StartIndex;
        }

        public override int ReadFrom(byte[] Buffer, int StartIndex = 0)
        {
            int cursor = StartIndex;

            if ((UserCommandType)Buffer[cursor] != CommandType)
                throw new Exception(ERRORWRONGTYPEBYTE);
            else
            {
                cursor++;

                Quantity = BitConverter.ToUInt32(Buffer, cursor);
                cursor += TypeSizes.INT;

                UnitPricePlat = BitConverter.ToUInt32(Buffer, cursor);
                cursor += TypeSizes.INT;

                UnitPriceShills = BitConverter.ToUInt32(Buffer, cursor);
                cursor += TypeSizes.INT;
            }

            return cursor - StartIndex;
        }
        #endregion

        public ObjectID ID { get; set; }
        public uint Quantity { get; set; }
        public uint UnitPricePlat { get; set; }
        public uint UnitPriceShills { get; set; }

        public UserCommandMarketplaceList(ObjectID ID, uint Quantity, uint UnitPricePlat, uint UnitPriceShills)
        {
            this.ID = ID;
            this.Quantity = Quantity;
            this.UnitPricePlat = UnitPricePlat;
            this.UnitPriceShills = UnitPriceShills;
        }

        public UserCommandMarketplaceList(byte[] Buffer, int StartIndex = 0)
        {
            ReadFrom(Buffer, StartIndex);
        }
    }
}
