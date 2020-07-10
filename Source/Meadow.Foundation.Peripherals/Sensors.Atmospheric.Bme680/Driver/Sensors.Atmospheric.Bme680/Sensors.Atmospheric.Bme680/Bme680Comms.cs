using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meadow.Foundation.Sensors.Atmospheric
{
    internal abstract class Bme680Comms
    {
        /// <summary>
        ///     Registers used to control the BME680.
        /// </summary>
        internal enum Register : byte
        {
            Heat0Register = 0x5A,
            GasWait0Register = 0x64,
            MeasurementRegister = 0x74,
            ConfigRegister = 0x75,
            HumidityControlRegister = 0x72,
            GasControlRegister = 0x71
        }

        public abstract void WriteRegister(Register register, byte value);

        public abstract void WriteRegister(byte register, byte value);

        public abstract byte[] ReadRegisters(byte startRegister, ushort readCount);

        public abstract byte ReadRegister(byte register);

        public abstract byte ReadRegister(Register register);

        public abstract byte ResetRegister { get; }

        public abstract byte ChipIdRegister { get; }
    }
}
