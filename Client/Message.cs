using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace paint
{
    [Serializable]
    class Message
    {
        public double X { get; set; }
        public double Y { get; set; }

        public byte[] Serialize()
        {
            byte[] result = new byte[16];// 8 bytes for each double
            BitConverter.GetBytes(X).CopyTo(result, 0);
            BitConverter.GetBytes(Y).CopyTo(result, 8);
            return result;
        }

        public static Message Deserialize(byte[] data)
        {
            if (data == null || data.Length != 16)
                throw new ArgumentException("Invalid data for deserialization.");

            return new Message
            {
                X = BitConverter.ToDouble(data, 0),
                Y = BitConverter.ToDouble(data, 8)
            };
        }
    }
}
