using Meadow.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meadow.Foundation.Sensors.Atmospheric
{
    internal class Bme680I2C : Bme680Comms
    {
        private readonly II2cPeripheral _i2CPeripheral;

        internal Bme680I2C(II2cBus i2c, byte busAddress)
        {
            if ((busAddress != 0x76) && (busAddress != 0x77))
            {
                throw new ArgumentOutOfRangeException(nameof(busAddress), "Address should be 0x76 or 0x77");
            }

            _i2CPeripheral = new I2cPeripheral(i2c, (byte)busAddress);
        }

        public override byte ResetRegister { get; } = 0xE0;

        public override byte ChipIdRegister { get; } = 0xD0;

        public override byte ReadRegister(Register register)
        {
            return ReadRegister((byte)register);
        }

        public override byte ReadRegister(byte register)
        {
            return _i2CPeripheral.ReadRegister(register);
        }

        public override byte[] ReadRegisters(byte startRegister, ushort readCount)
        {
            return _i2CPeripheral.ReadRegisters(startRegister, readCount);
        }

        public override void WriteRegister(Register register, byte value)
        {
            WriteRegister((byte)register, value);
        }

        public override void WriteRegister(byte register, byte value)
        {
            _i2CPeripheral.WriteRegister(register, value);
        }
    }
}
