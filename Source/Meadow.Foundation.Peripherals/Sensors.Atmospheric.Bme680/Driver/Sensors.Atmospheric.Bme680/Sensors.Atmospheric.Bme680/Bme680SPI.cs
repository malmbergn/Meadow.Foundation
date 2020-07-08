using Meadow.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meadow.Foundation.Sensors.Atmospheric
{
    /// <summary>
    /// NOT TESTED
    /// </summary>
    internal class Bme680SPI : Bme680Comms
    {
        private ISpiBus _spi;
        private IDigitalOutputPort _chipSelect;

        internal Bme680SPI(ISpiBus spi, IDigitalOutputPort chipSelect)
        {
            _spi = spi;
            _chipSelect = chipSelect;
        }

        public override byte ResetRegister  {get {return 0x60;} }

        public override byte ChipIdRegister { get { return 0x50; } }

        public override byte ReadRegister(byte register)
        {
            return ReadRegisters(register, 1).First();
        }

        public override byte ReadRegister(Register register)
        {
            return ReadRegister((byte)register);
        }

        public override byte[] ReadRegisters(byte startRegister, ushort readCount)
        {
            // the buffer needs to be big enough for the output and response
            var buffer = new byte[readCount + 1];
            var bufferTx = new byte[readCount + 1];
            buffer[0] = startRegister;

            //  var rx = _spi.ExchangeData(_chipSelect, buffer);

            var rx = _spi.ReceiveData(_chipSelect, readCount + 1);

            // skip past the byte where we clocked out the register address
            var registerData = rx.Skip(1).Take(readCount).ToArray();

            return registerData;
        }

        public override void WriteRegister(Register register, byte value)
        {
            WriteRegister(register, value);
        }

        public override void WriteRegister(byte register, byte value)
        {
            _spi.SendData(_chipSelect, register, value);
        }
    }
}
