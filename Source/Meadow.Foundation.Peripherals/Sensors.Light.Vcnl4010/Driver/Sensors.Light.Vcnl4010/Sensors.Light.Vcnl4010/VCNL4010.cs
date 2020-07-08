using Meadow.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Meadow.Foundation.Sensors.Light
{
    public class VCNL4010
    {
        //measurements/sec
        public enum Frequency : byte
        {
            _1_95 = 0x00,    // 1.95
            _3_90625 = 0x01, // 3.90625
            _7_8125 = 0x02,  // 7.8125
            _16_625 = 0x03,  // 16.625
            _31_25 = 0x04,   // 31.25
            _62_5 = 0x05,    // 62.5
            _125 = 0x06,     // 125
            _250 = 0x07,     // 250
        }

        protected const byte DefaultAddress = 0x13;
        protected const byte CommandRegister = 0x80;
        protected const byte ProductIdRegister = 0x81;
        protected const byte ProxRateRegister = 0x82;
        protected const byte IrLedRegister = 0x83;
        protected const byte AmbientParameterRegister = 0x84;
        protected const byte AmbientDataRegister = 0x85;
        protected const byte ProximityDataRegister = 0x87;
        protected const byte InterruptControlRegister = 0x89;
        protected const byte ProximityAdjustRegister = 0x8A;
        protected const byte IntStatRegister = 0x8E;
        protected const byte ModeTimingRegister = 0x8F;
        protected const byte MeasureAmbientRegister = 0x10;
        protected const byte MeasureProximityRegister = 0x08;
        protected const byte AmbientReadyRegister = 0x40;
        protected const byte ProximityReadyRegister = 0x20;
        protected const float AmbientLuxScale = 0.25f;

        protected readonly II2cPeripheral i2CPeripheral;

        public VCNL4010(II2cBus i2CBus)
        {
            i2CPeripheral = new I2cPeripheral(i2CBus, DefaultAddress);
        }

        public void Initialize()
        {
            var revision = i2CPeripheral.ReadRegister(ProductIdRegister);
            Console.WriteLine($"id: {revision}");

            if ((revision & 0xF0) != 0x20)
                throw new Exception("Failed to find VCNL4010, check wiring!");

            SetLedCurrent(20);
            SetFrequency(Frequency._16_625); // 16.625 readings/second

            i2CPeripheral.WriteRegister(InterruptControlRegister, 0x08);
        }

        void SetLedCurrent(byte value)
        {
            if (value < 0)
                value = 0;

            if (value > 20)
                value = 20;


            i2CPeripheral.WriteRegister(IrLedRegister, value);

        }

        byte GetLedCurrent()
        {
            return (byte)(i2CPeripheral.ReadRegister(ProxRateRegister) & 0x3F);
        }

        void SetFrequency(Frequency frequency)
        {
            i2CPeripheral.WriteRegister(ProxRateRegister, (byte)frequency);

        }

        byte GetFrequency()
        {
            return (byte)((i2CPeripheral.ReadRegister(ModeTimingRegister) >> 3) & 0x03);
        }

        public ushort Proximity()
        {
            var status = i2CPeripheral.ReadRegister(IntStatRegister);
            status = (byte)(status & 0b11100111);

            i2CPeripheral.WriteRegister(IntStatRegister, status);

            i2CPeripheral.WriteRegister(CommandRegister, MeasureProximityRegister);

            for (int i = 0; i < 20; i++)
            {
                byte result = i2CPeripheral.ReadRegister(CommandRegister);
                Console.WriteLine(result);
                if ((result & ProximityReadyRegister) == 1)
                    return i2CPeripheral.ReadUShort(ProximityDataRegister, Meadow.ByteOrder.BigEndian);

                Thread.Sleep(1);
            }

            return i2CPeripheral.ReadUShort(ProximityDataRegister, Meadow.ByteOrder.BigEndian);
        }

        public ushort Ambient()
        {
            var status = i2CPeripheral.ReadRegister(IntStatRegister);
            status = (byte)(status & 0b11100111);

            i2CPeripheral.WriteRegister(IntStatRegister, status);

            i2CPeripheral.WriteRegister(CommandRegister, MeasureAmbientRegister);

            for (int i = 0; i < 20; i++)
            {
                byte result = i2CPeripheral.ReadRegister(CommandRegister);
                Console.WriteLine(result);
                if ((result & ProximityReadyRegister) == 1)
                    return i2CPeripheral.ReadUShort(AmbientDataRegister, Meadow.ByteOrder.BigEndian);

                Thread.Sleep(1);
            }

            return i2CPeripheral.ReadUShort(AmbientDataRegister, Meadow.ByteOrder.BigEndian);
        }

        public float AmbientLux()
        {
            return Ambient() * AmbientLuxScale;
        }
    }
}
